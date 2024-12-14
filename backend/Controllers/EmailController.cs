using Microsoft.AspNetCore.Mvc;
using SentimatrixAPI.Services;
using SentimatrixAPI.Models;

namespace SentimatrixAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EmailController : ControllerBase
    {
        private readonly EmailService _emailService;
        private readonly GroqService _groqService;
        private readonly RedisService _redisService;
        private readonly ILogger<EmailController> _logger;

        public EmailController(
            EmailService emailService,
            GroqService groqService,
            RedisService redisService,
            ILogger<EmailController> logger)
        {
            _emailService = emailService;
            _groqService = groqService;
            _redisService = redisService;
            _logger = logger;
        }

        [HttpPost("analyze")]
        public async Task<IActionResult> AnalyzeEmails([FromBody] List<string> emailIds)
        {
            try
            {
                // Get emails from database
                var emails = await _emailService.GetEmailsByIds(emailIds);
                if (!emails.Any())
                {
                    return NotFound("No emails found with the provided IDs");
                }

                // Analyze sentiments using Groq (with Redis caching)
                var results = await _groqService.AnalyzeEmailsBatch(emails);

                // Update email records with sentiment scores
                await _emailService.UpdateSentimentScores(results);

                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing emails");
                return StatusCode(500, "An error occurred while analyzing emails");
            }
        }

        [HttpGet("health/redis")]
        public async Task<IActionResult> CheckRedisHealth()
        {
            try
            {
                if (!_redisService.IsConnected)
                {
                    return StatusCode(500, new { 
                        status = "unhealthy", 
                        message = "Redis is not connected",
                        details = "Connection to Redis server failed"
                    });
                }

                var testKey = "health:test";
                var testValue = DateTime.UtcNow.ToString();
                
                // Test write
                await _redisService.SetAsync(testKey, testValue, TimeSpan.FromSeconds(5));
                
                // Test read
                var result = await _redisService.GetAsync<string>(testKey);
                
                if (result == testValue)
                {
                    return Ok(new { 
                        status = "healthy", 
                        message = "Redis is working properly",
                        details = new {
                            writeTest = "successful",
                            readTest = "successful",
                            value = result,
                            connectionStatus = "connected"
                        }
                    });
                }
                else
                {
                    return StatusCode(500, new { 
                        status = "unhealthy", 
                        message = "Redis read/write test failed",
                        details = new {
                            writeTest = "successful",
                            readTest = "failed",
                            expectedValue = testValue,
                            actualValue = result,
                            connectionStatus = _redisService.IsConnected ? "connected" : "disconnected"
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis health check failed");
                return StatusCode(500, new { 
                    status = "unhealthy", 
                    message = "Redis connection failed",
                    error = ex.Message,
                    details = ex.StackTrace,
                    innerException = ex.InnerException?.Message
                });
            }
        }

        // Other endpoints...
    }
}