namespace SentimatrixAPI.Models.Settings
{
    public class GroqSettings
    {
        public string[] ApiKeys { get; set; } = Array.Empty<string>();
        public int MaxParallelRequests { get; set; } = 6;
        public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1/chat/completions";
        public string ModelName { get; set; } = "mixtral-8x7b-32768";
    }
}