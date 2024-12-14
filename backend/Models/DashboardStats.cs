using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SentimatrixAPI.Models
{
    public class DashboardStats
    {
        public int TotalEmails { get; set; }
        public int PositiveEmails { get; set; }
        public int NegativeEmails { get; set; }
        public int NeutralEmails { get; set; }
        public double AverageScore { get; set; }
        public DateTime LastUpdated { get; set; }
        public List<EmailSummary> RecentEmails { get; set; } = new();
    }

    public class EmailSummary
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Sender { get; set; } = string.Empty;
        public int Score { get; set; }
        public string Type { get; set; } = string.Empty;
        public DateTime Time { get; set; }
    }
}
