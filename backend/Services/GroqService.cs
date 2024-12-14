using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using SentimatrixAPI.Models;
using Microsoft.Extensions.Logging;

namespace SentimatrixAPI.Services
{
    public class GroqService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly ILogger<GroqService> _logger;
        private const string GROQ_API_URL = "https://api.groq.com/openai/v1/chat/completions";
        private const string GROQ_MODEL = "llama3-8b-8192"; // Using their recommended model

        public GroqService(IConfiguration configuration, ILogger<GroqService> logger)
        {
            _logger = logger;
            _apiKey = "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";
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        }

        public async Task<int> AnalyzeSentiment(string emailContent, Email email)
        {
            try 
            {
                _logger.LogInformation("Sending request to Groq API");
                
                var request = new
                {
                    messages = new[]
                    {
                        new { role = "system", content = "You are a sentiment analyzer. Score the following email content on a scale of 1-100, where 1 is extremely positive and 100 is extremely negative. Respond with ONLY the numeric score." },
                        new { role = "user", content = emailContent ?? string.Empty }
                    },
                    model = GROQ_MODEL
                };

                var jsonRequest = JsonConvert.SerializeObject(request);
                _logger.LogInformation($"Request to Groq: {jsonRequest}");
                
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(GROQ_API_URL, content);
                response.EnsureSuccessStatusCode();

                var jsonResponse = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"Response from Groq: {jsonResponse}");
                
                var result = JsonConvert.DeserializeObject<dynamic>(jsonResponse);
                
                if (result?.choices?[0]?.message?.content == null)
                {
                    throw new Exception("Invalid response from Groq API");
                }

                string scoreText = result.choices[0].message.content.ToString();
                _logger.LogInformation($"Extracted score text: {scoreText}");
                
                if (int.TryParse(scoreText.Trim(), out int score))
                {
                    _logger.LogInformation($"Final sentiment score: {score}");
                    email.ClassifiedProductType = await ClassifyProduct(emailContent);
                    return score;
                }

                throw new Exception("Failed to parse sentiment score");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in AnalyzeSentiment: {ex.Message}");
                throw;
            }
        }

        public async Task<string> ClassifyProduct(string emailBody)
        {
            try 
            {
                var request = new
                {
                    messages = new[]
                    {
                        new { role = "system", content = @"You are an expert product classifier. Given an email about a product or service, identify the most specific and precise product category. 
                        
                        Rules for classification:
                        - Be as specific as possible (e.g., 'Wireless Noise-Cancelling Headphones' instead of just 'Electronics')
                        - If multiple products are mentioned, choose the primary product
                        - If no clear product is identified, return 'Unspecified Product'
                        - Respond with ONLY the product category/name" },
                        new { role = "user", content = emailBody ?? string.Empty }
                    },
                    model = GROQ_MODEL
                };

                var jsonRequest = JsonConvert.SerializeObject(request);
                _logger.LogInformation($"Product Classification Request to Groq: {jsonRequest}");
                
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(GROQ_API_URL, content);
                response.EnsureSuccessStatusCode();

                var jsonResponse = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"Product Classification Response from Groq: {jsonResponse}");
                
                var result = JsonConvert.DeserializeObject<dynamic>(jsonResponse);
                
                if (result?.choices?[0]?.message?.content == null)
                {
                    throw new Exception("Invalid response from Groq API for product classification");
                }

                string productType = result.choices[0].message.content.ToString().Trim();
                _logger.LogInformation($"Classified Product: {productType}");

                return productType;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in ClassifyProduct: {ex.Message}");
                return "Unspecified Product";
            }
        }

        public string GenerateResponse(int sentimentScore, string emailContent)
        {
            if (sentimentScore > 60)
            {
                return "We apologize for any inconvenience you've experienced. Your feedback is important to us, and we will look into this matter immediately. A representative will contact you shortly to address your concerns.";
            }
            else
            {
                return "Thank you for your positive feedback! We're glad to hear about your experience and appreciate you taking the time to share it with us.";
            }
        }
    }
}