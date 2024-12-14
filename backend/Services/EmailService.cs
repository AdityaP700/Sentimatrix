using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using SentimatrixAPI.Models;
using SentimatrixAPI.Models.Settings;
using MongoDB.Driver.Linq;
namespace SentimatrixAPI.Services
{
    public class EmailService
    {
        private readonly IMongoCollection<Email> _emails;
        private readonly GroqService _groqService;
        private readonly ILogger<EmailService> _logger;

        public EmailService(
            IOptions<MongoDBSettings> settings,
            GroqService groqService,
            ILogger<EmailService> logger)
        {
            _logger = logger;
            try
            {
                // Register class map if not already registered
                if (!BsonClassMap.IsClassMapRegistered(typeof(Email)))
                {
                    BsonClassMap.RegisterClassMap<Email>(cm =>
                    {
                        cm.AutoMap();
                        cm.SetIgnoreExtraElements(true);  // Ignore fields in DB that aren't in model
                    });
                }

                _logger.LogInformation($"Attempting to connect to MongoDB with connection string: {settings.Value.ConnectionString}");
                var mongoClient = new MongoClient(settings.Value.ConnectionString);
                var database = mongoClient.GetDatabase(settings.Value.DatabaseName);
                _emails = database.GetCollection<Email>(settings.Value.EmailsCollectionName);
                _groqService = groqService;

                // Test the connection
                database.RunCommand((Command<BsonDocument>)"{ping:1}");
                _logger.LogInformation($"Successfully connected to MongoDB database: {settings.Value.DatabaseName}");

                // Create index only if it doesn't exist
                var indexKeysDefinition = Builders<Email>.IndexKeys
                    .Descending(x => x.Time)
                    .Ascending(x => x.Type);

                var indexName = "Time_Type_Index";
                var existingIndexes = _emails.Indexes.List().ToList();
                var indexExists = existingIndexes.Any(index => 
                    index["name"].AsString == indexName);

                if (!indexExists)
                {
                    var indexOptions = new CreateIndexOptions { Name = indexName };
                    var indexModel = new CreateIndexModel<Email>(indexKeysDefinition, indexOptions);
                    _emails.Indexes.CreateOne(indexModel);
                    _logger.LogInformation($"Created index: {indexName}");
                }
                else
                {
                    _logger.LogInformation($"Index {indexName} already exists");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to MongoDB");
                throw;
            }
        }

        public async Task<List<Email>> GetAsync()
        {
            try
            {
                return await _emails.Find(_ => true)
                                    .SortByDescending(e => e.Time)
                                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving emails: {ex.Message}");
                throw;
            }
        }

        public async Task<Email> GetAsync(string id)
        {
            try
            {
                return await _emails.Find(x => x.Id == id).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving email by ID: {ex.Message}");
                throw;
            }
        }

        public async Task CreateAsync(Email email)
        {
            try
            {
                if (string.IsNullOrEmpty(email.Id))
                {
                    email.Id = ObjectId.GenerateNewId().ToString();
                }
                await _emails.InsertOneAsync(email);
                _logger.LogInformation($"Successfully stored email from {email.Sender} with score {email.Score}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating email: {ex.Message}");
                throw;
            }
        }

        public async Task<List<Email>> GetPositiveEmailsAsync()
        {
            try
            {
                return await _emails.Find(x => x.Type == "positive")
                                    .SortByDescending(e => e.Time)
                                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving positive emails: {ex.Message}");
                throw;
            }
        }

        public async Task<List<Email>> GetNegativeEmailsAsync()
        {
            try
            {
                return await _emails.Find(x => x.Type == "negative")
                                    .SortByDescending(e => e.Time)
                                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving negative emails: {ex.Message}");
                throw;
            }
        }

        public async Task<List<Email>> GetEmailsByScoreRangeAsync(int minScore, int maxScore)
        {
            try
            {
                return await _emails.Find(x => x.Score >= minScore && x.Score <= maxScore)
                                    .SortByDescending(e => e.Time)
                                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving emails by score range: {ex.Message}");
                throw;
            }
        }

        public async Task<List<Email>> GetEmailsBySenderAsync(string senderEmail)
        {
            try
            {
                return await _emails.Find(x => x.Sender == senderEmail)
                                    .SortByDescending(e => e.Time)
                                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving emails by sender: {ex.Message}");
                throw;
            }
        }

        public async Task UpdateAsync(string id, Email updatedEmail)
        {
            try
            {
                await _emails.ReplaceOneAsync(x => x.Id == id, updatedEmail);
                _logger.LogInformation($"Successfully updated email with ID: {id}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating email: {ex.Message}");
                throw;
            }
        }

        public async Task RemoveAsync(string id)
        {
            try
            {
                await _emails.DeleteOneAsync(x => x.Id == id);
                _logger.LogInformation($"Successfully removed email with ID: {id}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error removing email: {ex.Message}");
                throw;
            }
        }

        public async Task ProcessNewEmails(List<Email> newEmails)
        {
            var sentimentResults = await _groqService.AnalyzeEmailsBatch(newEmails);

            foreach (var (emailId, score) in sentimentResults)
            {
                var email = newEmails.First(e => e.Id == emailId);
                email.Score = score;
                email.Type = DetermineEmailType(score);
                await _emails.ReplaceOneAsync(
                    e => e.Id == email.Id,
                    email,
                    new ReplaceOptions { IsUpsert = true });
            }
        }

        private string DetermineEmailType(int score)
        {
            return score switch
            {
                >= 75 => "positive",
                <= 25 => "negative",
                _ => "neutral"
            };
        }

        public async Task<List<SentimentData>> GetSentimentTrend(PipelineDefinition<Email, SentimentData> pipeline)
        {
            try
            {
                var result = await _emails.Aggregate(pipeline).ToListAsync();
                return result ?? new List<SentimentData>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving sentiment trend");
                throw;
            }
        }

        public async Task<List<SentimentData>> GetSentimentTrendByDate(DateTime startDate, DateTime endDate)
        {
            try
            {
                var pipeline = new[]
                {
                    new BsonDocument("$match", new BsonDocument
                    {
                        { "Time", new BsonDocument
                            {
                                { "$gte", startDate },
                                { "$lte", endDate }
                            }
                        }
                    }),
                    new BsonDocument("$group", new BsonDocument
                    {
                        { "_id", new BsonDocument("$dateToString", new BsonDocument
                            {
                                { "format", "%Y-%m-%d" },
                                { "date", "$Time" }
                            })
                        },
                        { "averageScore", new BsonDocument("$avg", "$Score") },
                        { "count", new BsonDocument("$sum", 1) }
                    }),
                    new BsonDocument("$sort", new BsonDocument("_id", 1)),
                    new BsonDocument("$project", new BsonDocument
                    {
                        { "_id", 0 },
                        { "date", new BsonDocument("$dateFromString", new BsonDocument
                            {
                                { "dateString", "$_id" }
                            })
                        },
                        { "averageScore", 1 },
                        { "count", 1 }
                    })
                };

                var result = await _emails.Aggregate<SentimentData>(pipeline).ToListAsync();
                return result ?? new List<SentimentData>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving sentiment trend by date");
                throw;
            }
        }

        public async Task<DashboardStats> GetDashboardStats()
        {
            try
            {
                return await GetDashboardStatsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving dashboard stats");
                throw;
            }
        }

        public async Task<DashboardStats> GetDashboardStatsAsync()
        {
            try
            {
                _logger.LogInformation("Calculating dashboard statistics...");

                // Get all emails with valid scores
                var allEmails = await _emails.Find(_ => true).ToListAsync();
                
                if (allEmails == null || !allEmails.Any())
                {
                    _logger.LogWarning("No emails found in database");
                    return new DashboardStats
                    {
                        LastUpdated = DateTime.UtcNow,
                        RecentEmails = new List<EmailSummary>()
                    };
                }

                // Calculate statistics
                var stats = new DashboardStats
                {
                    TotalEmails = allEmails.Count,
                    PositiveEmails = allEmails.Count(e => e.Score >= 75),
                    NegativeEmails = allEmails.Count(e => e.Score <= 25),
                    AverageScore = allEmails.Any() ? allEmails.Average(e => e.Score) : 0,
                    LastUpdated = DateTime.UtcNow,
                    RecentEmails = allEmails
                        .Where(e => !string.IsNullOrEmpty(e.Body)) // Only include emails with content
                        .OrderByDescending(e => e.Time)
                        .Take(5)
                        .Select(e => new EmailSummary
                        {
                            Id = e.Id,
                            Subject = string.IsNullOrEmpty(e.Subject) ? "(No Subject)" : e.Subject,
                            Sender = string.IsNullOrEmpty(e.Sender) ? "(No Sender)" : e.Sender,
                            Score = e.Score,
                            Type = DetermineEmailType(e.Score),
                            Time = e.Time
                        })
                        .ToList()
                };

                stats.NeutralEmails = stats.TotalEmails - (stats.PositiveEmails + stats.NegativeEmails);

                _logger.LogInformation($"Dashboard stats calculated successfully. " +
                    $"Total: {stats.TotalEmails}, " +
                    $"Positive: {stats.PositiveEmails}, " +
                    $"Negative: {stats.NegativeEmails}, " +
                    $"Neutral: {stats.NeutralEmails}");

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating dashboard stats");
                throw;
            }
        }

        public async Task<List<Email>> GetEmailsByIds(List<string> emailIds)
        {
            var filter = Builders<Email>.Filter.In(e => e.Id, emailIds);
            return await _emails.Find(filter).ToListAsync();
        }

        public async Task UpdateSentimentScores(List<(string EmailId, int Score)> results)
        {
            var bulkOps = results.Select(result =>
                new UpdateOneModel<Email>(
                    Builders<Email>.Filter.Eq(e => e.Id, result.EmailId),
                    Builders<Email>.Update.Set(e => e.SentimentScore, result.Score)
                )
            );

            await _emails.BulkWriteAsync(bulkOps);
        }

        public async Task<List<Email>> GetEmailsByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                var filter = Builders<Email>.Filter.And(
                    Builders<Email>.Filter.Gte(e => e.Time, startDate),
                    Builders<Email>.Filter.Lte(e => e.Time, endDate)
                );

                return await _emails.Find(filter)
                                   .SortByDescending(e => e.Time)
                                   .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving emails between {startDate} and {endDate}");
                throw;
            }
        }

        public async Task<BsonDocument> GetSampleDocument()
        {
            try
            {
                var sample = await _emails.Find(_ => true).Limit(1).FirstOrDefaultAsync();
                if (sample != null)
                {
                    var doc = sample.ToBsonDocument();
                    _logger.LogInformation($"Sample document: {doc}");
                    return doc;
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sample document");
                throw;
            }
        }

        public async Task CleanupEmails()
        {
            try
            {
                var updates = new List<WriteModel<Email>>();
                var emails = await _emails.Find(_ => true).ToListAsync();

                foreach (var email in emails)
                {
                    var update = Builders<Email>.Update
                        .Set(e => e.Subject, string.IsNullOrEmpty(email.Subject) ? "(No Subject)" : email.Subject)
                        .Set(e => e.Sender, string.IsNullOrEmpty(email.Sender) ? "(No Sender)" : email.Sender)
                        .Set(e => e.Type, DetermineEmailType(email.Score));

                    updates.Add(new UpdateOneModel<Email>(
                        Builders<Email>.Filter.Eq(e => e.Id, email.Id),
                        update
                    ));
                }

                if (updates.Any())
                {
                    await _emails.BulkWriteAsync(updates);
                    _logger.LogInformation($"Cleaned up {updates.Count} emails");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up emails");
                throw;
            }
        }
    }
}
