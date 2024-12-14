namespace SentimatrixAPI.Models.Settings
{
    public class RedisSettings
    {
        public string ConnectionString { get; set; } = "localhost:6379";
        public int DefaultExpirationMinutes { get; set; } = 10080;
    }
}