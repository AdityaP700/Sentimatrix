using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SentimatrixAPI.Models
{
    public class EmailData
    {
        public string Id { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string SenderEmail { get; set; } = string.Empty;
        public string ReceiverEmail { get; set; } = string.Empty;
        public DateTime Time { get; set; }
    }
}