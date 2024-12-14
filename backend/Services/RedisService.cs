using StackExchange.Redis;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SentimatrixAPI.Models.Settings;

namespace SentimatrixAPI.Services
{
    public class RedisService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _db;
        private readonly RedisSettings _settings;
        private readonly ILogger<RedisService> _logger;

        public RedisService(IOptions<RedisSettings> settings, ILogger<RedisService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
            try
            {
                _logger.LogInformation($"Attempting to connect to Redis at {_settings.ConnectionString}");
                var options = ConfigurationOptions.Parse(_settings.ConnectionString);
                options.AbortOnConnectFail = false; // Don't throw if can't connect
                
                _redis = ConnectionMultiplexer.Connect(options);
                _db = _redis.GetDatabase();
                
                // Test the connection
                var pingResult = _db.Ping();
                _logger.LogInformation($"Successfully connected to Redis. Ping: {pingResult.TotalMilliseconds}ms");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to Redis. Error details: {ErrorMessage}", ex.Message);
                throw;
            }
        }

        public bool IsConnected => _redis?.IsConnected ?? false;

        public async Task<T?> GetAsync<T>(string key)
        {
            try
            {
                _logger.LogInformation($"Attempting to get value for key: {key}");
                var value = await _db.StringGetAsync(key);
                if (!value.HasValue)
                {
                    _logger.LogInformation($"No value found for key: {key}");
                    return default;
                }
                return JsonSerializer.Deserialize<T>(value!);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting value for key: {key}");
                throw;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
        {
            try
            {
                var serializedValue = JsonSerializer.Serialize(value);
                await _db.StringSetAsync(
                    key,
                    serializedValue,
                    expiry ?? TimeSpan.FromMinutes(_settings.DefaultExpirationMinutes)
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error setting value for key: {key}");
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string key)
        {
            try
            {
                return await _db.KeyDeleteAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting key: {key}");
                throw;
            }
        }
    }
}