using Microsoft.AspNetCore.Mvc;

namespace RateLimiter.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class RateLimitingController : ControllerBase
    {
        private readonly IRateLimitedService _rateLimitedService;
        private readonly IConfiguration _configuration;

        public RateLimitingController(IRateLimitedService rateLimitedService, IConfiguration configuration)
        {
            _rateLimitedService = rateLimitedService;
            _configuration = configuration;
        }

        [HttpGet]
        public async Task<IActionResult> GetData()
        {
            var getTechnique = _configuration["Technique"];

            var clientKey = getTechnique + ":" + HttpContext.Connection.RemoteIpAddress?.ToString();
            Console.WriteLine($"{getTechnique} rate limiting check initiated for client: {clientKey}");

            try
            {
                await _rateLimitedService.ExecuteWithRateLimitAsync(getTechnique, clientKey);
                Console.WriteLine($"Request allowed for client: {clientKey} using {getTechnique}.");
                return Ok("Request allowed");
            }
            catch (RateLimitExceededException)
            {
                Console.WriteLine($"Rate limit exceeded for client: {clientKey} using {getTechnique}.");
                return StatusCode(429, "Too many requests");
            }
        }
    }
}
