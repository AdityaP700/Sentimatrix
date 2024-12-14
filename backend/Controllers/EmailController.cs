using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SentimatrixAPI.Models;
using SentimatrixAPI.Services;
using Microsoft.Extensions.Options;
using SentimatrixAPI.Data;
using Microsoft.AspNetCore.SignalR;
using SentimatrixAPI.Hubs;

namespace SentimatrixAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmailController : ControllerBase
    {
        private readonly EmailService _emailService;
        private readonly ILogger<EmailController> _logger;
        private readonly ILogger<EmailProcessController> _emailProcessLogger;
        private readonly IMongoCollection<EmailData> _emailCollection;
        private readonly GroqService _groqService;
        private readonly IHubContext<TicketHub> _hubContext;

        public EmailController(
            EmailService emailService, 
            ILogger<EmailController> logger, 
            IMongoDatabase database,
            IOptions<MongoDBSettings> settings,
            GroqService groqService,
            IHubContext<TicketHub> hubContext,
            ILogger<EmailProcessController> emailProcessLogger)
        {
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _emailCollection = database.GetCollection<EmailData>(settings.Value.EmailsCollectionName);
            _groqService = groqService ?? throw new ArgumentNullException(nameof(groqService));
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
            _emailProcessLogger = emailProcessLogger ?? throw new ArgumentNullException(nameof(emailProcessLogger));
            _logger.LogInformation("Successfully connected to the Email database.");
        }

        [HttpGet("sentiment/{period}")]
        public async Task<IActionResult> GetSentimentTrend(string period)
        {
            try
            {
                DateTime startDate = period.ToUpper() switch
                {
                    "1D" => DateTime.UtcNow.AddDays(-1),
                    "5D" => DateTime.UtcNow.AddDays(-5),
                    "1W" => DateTime.UtcNow.AddDays(-7),
                    "1M" => DateTime.UtcNow.AddMonths(-1),
                    _ => throw new ArgumentException("Invalid time period")
                };

                var results = await _emailCollection
                    .Aggregate()
                    .Match(Builders<EmailData>.Filter.Gte(e => e.ReceivedDate, startDate))
                    .Group(e => e.ReceivedDate.Date, 
                        g => new SentimentData 
                        {
                            Period = g.Key.ToString("yyyy-MM-dd"),
                            AverageScore = g.Average(x => x.Score),
                            Count = g.Count()
                        })
                    .Sort(Builders<SentimentData>.Sort.Ascending(x => x.Period))
                    .ToListAsync();

                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving sentiment trend");
                return StatusCode(500, new { message = "Internal server error", details = ex.Message });
            }
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<EmailData>>> GetAllEmails()
        {
            try
            {
                _logger.LogInformation("Getting all emails");
                var emails = await _emailCollection.Find(new BsonDocument())
                                                 .SortByDescending(e => e.ReceivedDate)
                                                 .ToListAsync();
                _logger.LogInformation($"Found {emails.Count} emails");
                return Ok(emails);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all emails");
                return StatusCode(500, new { message = "Internal server error", details = ex.Message });
            }
        }

        [HttpGet("positive")]
        public async Task<ActionResult<IEnumerable<EmailData>>> GetPositiveEmails(
            [FromQuery] int page = 1, 
            [FromQuery] int pageSize = 10)
        {
            try
            {
                var emails = await _emailService.GetEmailsByScoreRangeAsync(70, 100);
                var paginatedEmails = emails
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                return Ok(new 
                { 
                    Data = paginatedEmails, 
                    Page = page, 
                    PageSize = pageSize,
                    TotalCount = emails.Count()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving positive emails");
                return StatusCode(500, new { message = "Internal server error", details = ex.Message });
            }
        }

        [HttpGet("negative")]
        public async Task<ActionResult<IEnumerable<EmailData>>> GetNegativeEmails(
            [FromQuery] int page = 1, 
            [FromQuery] int pageSize = 10)
        {
            try
            {
                var emails = await _emailService.GetEmailsByScoreRangeAsync(0, 30);
                var paginatedEmails = emails
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                return Ok(new 
                { 
                    Data = paginatedEmails, 
                    Page = page, 
                    PageSize = pageSize,
                    TotalCount = emails.Count()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving negative emails");
                return StatusCode(500, new { message = "Internal server error", details = ex.Message });
            }
        }

        [HttpGet("sender/{email}")]
        public async Task<ActionResult<IEnumerable<EmailData>>> GetEmailsBySender(
            string email, 
            [FromQuery] int page = 1, 
            [FromQuery] int pageSize = 10)
        {
            try
            {
                var emails = await _emailService.GetEmailsBySenderAsync(email);
                var paginatedEmails = emails
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                return Ok(new 
                { 
                    Data = paginatedEmails, 
                    Page = page, 
                    PageSize = pageSize,
                    TotalCount = emails.Count()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving emails by sender");
                return StatusCode(500, new { message = "Internal server error", details = ex.Message });
            }
        }

        [HttpGet("stats")]
        public async Task<ActionResult<DashboardStats>> GetDashboardStats()
        {
            try
            {
                var stats = await _emailService.GetDashboardStatsAsync();
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving dashboard stats");
                return StatusCode(500, new { message = "Internal server error", details = ex.Message });
            }
        }

        /// <summary>
        /// Test the email processing functionality without hitting TruBot
        /// </summary>
        /// <returns>Processing result with status and message</returns>
        /// <response code="200">Returns the processing result when successful</response>
        /// <response code="500">If there's an error processing the email</response>
        [HttpGet("test-email")]
        public async Task<IActionResult> TestEmailProcessing()
        {
            // Create a test email data object
            var testEmailData = new EmailData
            {
                Subject = "Good product",
                Body = "<p>hey the coffe machine was good</p>",
                SenderEmail = "testuser@example.com",
                ReceiverEmail = "support@company.com"
            };

            // Call the existing ProcessEmail method from EmailProcessController
            return await new EmailProcessController(_emailProcessLogger, _groqService, _hubContext, _emailService)
                .ProcessEmail(testEmailData);
        }
    }
}