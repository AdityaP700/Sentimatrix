using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace SentimatrixAPI.Models
{
    public class Email
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonElement("subject")]
        public string Subject { get; set; } = string.Empty;

        [BsonElement("body")]
        public string Body { get; set; } = string.Empty;

        [BsonElement("sender")]
        public string Sender { get; set; } = string.Empty;

        [BsonElement("receiver")]
        public string Receiver { get; set; } = string.Empty;

        [BsonElement("time")]
        public DateTime Time { get; set; }

        [BsonElement("score")]
        public int Score { get; set; }

        [BsonElement("type")]
        public string Type { get; set; } = string.Empty;

        [BsonElement("sentimentscore")]
        public int? SentimentScore { get; set; }
    }
}
