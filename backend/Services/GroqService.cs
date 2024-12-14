using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using SentimatrixAPI.Models;
using SentimatrixAPI.Models.Settings;
using SentimatrixAPI.Services;

public class GroqService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GroqSettings _settings;
    private readonly RedisService _redisService;
    private readonly ILogger<GroqService> _logger;
    private readonly SemaphoreSlim _semaphore;
    private readonly ConcurrentQueue<string> _apiKeys;
    private const string SENTIMENT_CACHE_KEY = "sentiment:";

    public GroqService(
        IHttpClientFactory httpClientFactory,
        IOptions<GroqSettings> settings,
        RedisService redisService,
        ILogger<GroqService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _redisService = redisService;
        _logger = logger;
        _semaphore = new SemaphoreSlim(_settings.MaxParallelRequests);
        _apiKeys = new ConcurrentQueue<string>(_settings.ApiKeys);
    }

    private string GetNextApiKey()
    {
        if (_apiKeys.TryDequeue(out string? apiKey))
        {
            _apiKeys.Enqueue(apiKey); // Put it back at the end
            return apiKey;
        }
        throw new InvalidOperationException("No API keys available");
    }

    public async Task<List<(string EmailId, int Score)>> AnalyzeEmailsBatch(List<Email> emails)
    {
        var results = new List<(string EmailId, int Score)>();
        var uncachedEmails = new List<Email>();

        // First check cache for each email
        foreach (var email in emails)
        {
            var cacheKey = $"{SENTIMENT_CACHE_KEY}{ComputeHash(email.Body)}";
            var cachedScore = await _redisService.GetAsync<int?>(cacheKey);
            
            if (cachedScore.HasValue)
            {
                _logger.LogInformation($"Cache hit for email {email.Id}");
                results.Add((email.Id, cachedScore.Value));
            }
            else
            {
                uncachedEmails.Add(email);
            }
        }

        // Process uncached emails in parallel
        if (uncachedEmails.Any())
        {
            var tasks = uncachedEmails.Select(async email =>
            {
                await _semaphore.WaitAsync();
                try
                {
                    var score = await AnalyzeSentiment(email.Body);
                    var cacheKey = $"{SENTIMENT_CACHE_KEY}{ComputeHash(email.Body)}";
                    
                    // Cache the result
                    await _redisService.SetAsync(cacheKey, score, TimeSpan.FromDays(7));
                    
                    return (email.Id, score);
                }
                finally
                {
                    _semaphore.Release();
                }
            });

            var newResults = await Task.WhenAll(tasks);
            results.AddRange(newResults);
        }

        return results;
    }

    public async Task<int> AnalyzeSentiment(string content)
    {
        var apiKey = GetNextApiKey();
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        try
        {
            _logger.LogInformation($"Making request to Groq API: {_settings.BaseUrl}");
            
            var request = new
            {
                model = _settings.ModelName,
                messages = new[]
                {
                    new { role = "system", content = @"You are a sentiment analyzer. Analyze the sentiment of the given text and return ONLY a number between 1 and 100.
                        - 1-25: Very Positive
                        - 26-45: Somewhat Positive
                        - 46-55: Neutral
                        - 56-75: Somewhat Negative
                        - 76-100: Very Negative
                        Respond with ONLY the number, no explanation or additional text." },
                    new { role = "user", content = content }
                },
                temperature = 0.3,  // Lower temperature for more consistent results
                max_tokens = 10     // Limit response length
            };

            var jsonContent = JsonSerializer.Serialize(request);
            _logger.LogInformation($"Request payload: {jsonContent}");

            var response = await client.PostAsync(
                _settings.BaseUrl,
                new StringContent(jsonContent, Encoding.UTF8, "application/json")
            );

            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogInformation($"Groq API Response: {responseContent}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Groq API error: {response.StatusCode} - {responseContent}");
                throw new HttpRequestException($"Groq API error: {response.StatusCode} - {responseContent}");
            }

            var responseObject = JsonSerializer.Deserialize<JsonElement>(responseContent);
            
            var scoreText = responseObject
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()?
                .Trim();

            _logger.LogInformation($"Raw score text: {scoreText}");

            // Extract just the number from the response
            var numberMatch = System.Text.RegularExpressions.Regex.Match(scoreText ?? "", @"\d+");
            if (numberMatch.Success && int.TryParse(numberMatch.Value, out int score))
            {
                // Ensure score is within bounds
                score = Math.Max(1, Math.Min(100, score));
                _logger.LogInformation($"Parsed sentiment score: {score}");
                return score;
            }

            throw new Exception($"Failed to parse sentiment score from response: {scoreText}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Groq API");
            throw;
        }
    }

    public async Task<string> GenerateResponse(string prompt)
    {
        var apiKey = GetNextApiKey();
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        var request = new
        {
            model = _settings.ModelName,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var response = await client.PostAsync(
            _settings.BaseUrl,
            new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        );

        response.EnsureSuccessStatusCode();
        var responseContent = await response.Content.ReadAsStringAsync();
        var responseObject = JsonSerializer.Deserialize<JsonElement>(responseContent);
        
        return responseObject
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }

    private static string ComputeHash(string input)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}
