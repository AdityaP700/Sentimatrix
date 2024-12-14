using Microsoft.AspNetCore.Mvc;
using SentimatrixAPI.Models;
using SentimatrixAPI.Services;
using System.Text.Json;
using MongoDB.Bson;

namespace SentimatrixAPI.Controllers
{
    /// <summary>
    /// Controller for processing emails received from TruBot
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class EmailProcessController : ControllerBase
    {
        private readonly EmailService _emailService;
        private readonly GroqService _groqService;
        private readonly ILogger<EmailProcessController> _logger;

        public EmailProcessController(
            EmailService emailService,
            GroqService groqService,
            ILogger<EmailProcessController> logger)
        {
            _emailService = emailService;
            _groqService = groqService;
            _logger = logger;
        }

        /// <summary>
        /// Process an email received from TruBot
        /// </summary>
        /// <param name="emailData">The email data</param>
        /// <returns>Processing result with status and message</returns>
        /// <response code="200">Returns the processing result when successful</response>
        /// <response code="500">If there's an error processing the email</response>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(EmailProcessResponse))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(EmailProcessResponse))]
        public async Task<IActionResult> ProcessEmail([FromBody] EmailData emailData)
        {
            try
            {
                _logger.LogInformation($"Received email data: {JsonSerializer.Serialize(emailData)}");

                if (emailData == null)
                {
                    _logger.LogWarning("Email data is null");
                    return BadRequest("Email data cannot be null");
                }

                // Validate required fields
                if (string.IsNullOrEmpty(emailData.Body))
                {
                    _logger.LogWarning("Email body is empty");
                    return BadRequest("Email body cannot be empty");
                }

                _logger.LogInformation($"Processing email from {emailData.SenderEmail}");

                try
                {
                    // Generate a unique ID for the email
                    var emailId = ObjectId.GenerateNewId().ToString();

                    // Analyze sentiment
                    var sentimentScore = await _groqService.AnalyzeSentiment(emailData.Body);
                    _logger.LogInformation($"Sentiment analysis complete. Score: {sentimentScore}");

                    var response = await _groqService.GenerateResponse(emailData.Body);
                    _logger.LogInformation("Generated response from Groq");

                    var email = new Email
                    {
                        Id = emailId,
                        Subject = emailData.Subject ?? "No Subject",
                        Body = emailData.Body,
                        Sender = emailData.SenderEmail ?? "unknown@sender.com",
                        Receiver = emailData.ReceiverEmail ?? "unknown@receiver.com",
                        Score = sentimentScore,
                        Time = emailData.Time != default ? emailData.Time : DateTime.UtcNow,
                        Type = DetermineEmailType(sentimentScore)
                    };

                    _logger.LogInformation("Attempting to save email to database");
                    await _emailService.CreateAsync(email);
                    _logger.LogInformation($"Email saved successfully with ID: {email.Id}");

                    return Ok(new
                    {
                        id = email.Id,
                        subject = email.Subject,
                        sender = email.Sender,
                        score = sentimentScore,
                        type = email.Type,
                        response = response,
                        timestamp = email.Time
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during email processing steps");
                    return StatusCode(500, new { 
                        error = "Processing failed", 
                        message = ex.Message,
                        details = ex.StackTrace 
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in ProcessEmail");
                return StatusCode(500, new { 
                    error = "Unhandled error", 
                    message = ex.Message,
                    details = ex.StackTrace 
                });
            }
        }

        /// <summary>
        /// Get serious tickets
        /// </summary>
        /// <returns>List of serious tickets</returns>
        /// <response code="200">Returns the list of serious tickets when successful</response>
        /// <response code="500">If there's an error retrieving the tickets</response>
        [HttpGet("serious-tickets")]
        public async Task<IActionResult> GetSeriousTickets()
        {
            try
            {
                // Get negative emails from MongoDB
                var tickets = await _emailService.GetNegativeEmailsAsync();
                return Ok(tickets);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving serious tickets: {ex.Message}");
                return StatusCode(500, new { message = "Error retrieving tickets" });
            }
        }

        /// <summary>
        /// Get dashboard statistics
        /// </summary>
        [HttpGet("dashboard-stats")]
        public async Task<IActionResult> GetDashboardStats()
        {
            try
            {
                _logger.LogInformation("Retrieving dashboard statistics...");
                var stats = await _emailService.GetDashboardStatsAsync();
                _logger.LogInformation($"Successfully retrieved dashboard stats. Total emails: {stats.TotalEmails}");
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving dashboard stats: {Message}", ex.Message);
                return StatusCode(500, new { 
                    message = "Error retrieving dashboard statistics",
                    details = ex.Message,
                    stackTrace = ex.StackTrace
                });
            }
        }

        /// <summary>
        /// Get emails by sentiment type (positive/negative/neutral)
        /// </summary>
        [HttpGet("by-sentiment/{type}")]
        public async Task<IActionResult> GetEmailsBySentiment(string type)
        {
            try
            {
                var emails = type.ToLower() switch
                {
                    "positive" => await _emailService.GetPositiveEmailsAsync(),
                    "negative" => await _emailService.GetNegativeEmailsAsync(),
                    "neutral" => await _emailService.GetEmailsByScoreRangeAsync(26, 75),
                    _ => throw new ArgumentException("Invalid sentiment type. Use 'positive', 'negative', or 'neutral'")
                };

                return Ok(new
                {
                    type = type,
                    count = emails.Count,
                    emails = emails
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving {type} emails");
                return StatusCode(500, new { message = $"Error retrieving {type} emails" });
            }
        }

        /// <summary>
        /// Get emails by date range
        /// </summary>
        [HttpGet("by-date")]
        public async Task<IActionResult> GetEmailsByDateRange(
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate)
        {
            try
            {
                var start = startDate ?? DateTime.UtcNow.AddDays(-7);
                var end = endDate ?? DateTime.UtcNow;

                var emails = await _emailService.GetEmailsByDateRangeAsync(start, end);
                return Ok(new
                {
                    startDate = start,
                    endDate = end,
                    count = emails.Count,
                    emails = emails
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving emails by date range");
                return StatusCode(500, new { message = "Error retrieving emails" });
            }
        }

        /// <summary>
        /// Get email by ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetEmailById(string id)
        {
            try
            {
                var email = await _emailService.GetAsync(id);
                if (email == null)
                {
                    return NotFound(new { message = $"Email with ID {id} not found" });
                }
                return Ok(email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving email {id}");
                return StatusCode(500, new { message = "Error retrieving email" });
            }
        }

        /// <summary>
        /// Get sentiment trend data
        /// </summary>
        [HttpGet("sentiment-trend")]
        public async Task<IActionResult> GetSentimentTrend(
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate)
        {
            try
            {
                var start = startDate ?? DateTime.UtcNow.AddDays(-30);  // Default to last 30 days
                var end = endDate ?? DateTime.UtcNow;

                var trendData = await _emailService.GetSentimentTrendByDate(start, end);
                return Ok(new
                {
                    startDate = start,
                    endDate = end,
                    data = trendData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving sentiment trend");
                return StatusCode(500, new { message = "Error retrieving sentiment trend" });
            }
        }

        /// <summary>
        /// Get sample document
        /// </summary>
        /// <returns>Sample document</returns>
        /// <response code="200">Returns the sample document when successful</response>
        /// <response code="500">If there's an error retrieving the sample document</response>
        [HttpGet("debug/sample")]
        public async Task<IActionResult> GetSampleDocument()
        {
            try
            {
                var sample = await _emailService.GetSampleDocument();
                return Ok(sample);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sample document");
                return StatusCode(500, new { message = "Error getting sample document", details = ex.Message });
            }
        }

        /// <summary>
        /// Cleanup emails
        /// </summary>
        /// <returns>Cleanup result with status and message</returns>
        /// <response code="200">Returns the cleanup result when successful</response>
        /// <response code="500">If there's an error during cleanup</response>
        [HttpPost("cleanup")]
        public async Task<IActionResult> CleanupEmails()
        {
            try
            {
                await _emailService.CleanupEmails();
                return Ok(new { message = "Email cleanup completed successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during email cleanup");
                return StatusCode(500, new { message = "Error cleaning up emails", details = ex.Message });
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
    }
}
