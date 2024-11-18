using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using System;
using System.Threading.Tasks;

public interface IRateLimitedService
{
    Task MakeApiCallAsync(string technique, string clientKey);
    Task ExecuteWithRateLimitAsync(string technique, string clientKey);
}

public class RateLimitedService : IRateLimitedService
{
    private readonly IFixedWindowRedisRateLimiter _fixedWindowRateLimiter;
    private readonly ISlidingWindowRedisRateLimiter _slidingWindowRateLimiter;
    private readonly ITokenBucketRedisRateLimiter _tokenBucketRateLimiter;
    private readonly IConcurrencyRedisRateLimiter _concurrencyRateLimiter;
    private readonly AsyncRetryPolicy _retryPolicy;

    public RateLimitedService(
        IFixedWindowRedisRateLimiter fixedWindowRateLimiter,
        ISlidingWindowRedisRateLimiter slidingWindowRateLimiter,
        ITokenBucketRedisRateLimiter tokenBucketRateLimiter,
        IConcurrencyRedisRateLimiter concurrencyRateLimiter,
        IOptions<PollySettings> pollySettings)
    {
        _fixedWindowRateLimiter = fixedWindowRateLimiter;
        _slidingWindowRateLimiter = slidingWindowRateLimiter;
        _tokenBucketRateLimiter = tokenBucketRateLimiter;
        _concurrencyRateLimiter = concurrencyRateLimiter;

        var settings = pollySettings.Value;

        // Define the retry policy using Polly
        _retryPolicy = Policy
            .Handle<RateLimitExceededException>()
            .WaitAndRetryAsync(
                settings.RetryCount, // Number of retries
                retryAttempt => TimeSpan.FromSeconds(settings.RetryDelaySeconds), // Delay between retries
                (exception, timeSpan, retryCount, context) =>
                {
                    // Log retry attempts
                    Console.WriteLine($"Retry {retryCount} after {timeSpan} due to: {exception.Message}");
                });
    }

    public async Task MakeApiCallAsync(string technique, string clientKey)
    {
        bool allowed = false;

        // Log which technique is being checked
        Console.WriteLine($"Checking rate limit using {technique} for client: {clientKey}");

        if (technique == "FixedWindow")
        {
            allowed = await _fixedWindowRateLimiter.IsAllowedAsync(clientKey);
        }
        else if (technique == "SlidingWindow")
        {
            allowed = await _slidingWindowRateLimiter.IsAllowedAsync(clientKey);
        }
        else if (technique == "TokenBucket")
        {
            allowed = await _tokenBucketRateLimiter.IsAllowedAsync(clientKey);
        }
        else if (technique == "Concurrency")
        {
            allowed = await _concurrencyRateLimiter.IsAllowedAsync(clientKey);
        }
        else
        {
            Console.WriteLine("Invalid rate limiting technique specified.");
            return;
        }

        // Log whether the request was allowed or blocked
        if (allowed)
        {
            Console.WriteLine($"Request allowed for client: {clientKey} using {technique}.");
        }
        else
        {
            Console.WriteLine($"Rate limit exceeded for client: {clientKey} using {technique}.");
            throw new RateLimitExceededException("Rate limit exceeded");
        }

        // Simulating API call after successful rate limiting check
        Console.WriteLine("API call succeeded.");
    }

    public async Task ExecuteWithRateLimitAsync(string technique, string clientKey)
    {
        // Log that the rate-limited execution has started
        Console.WriteLine($"Starting rate-limited execution for client: {clientKey} using {technique}.");

        await _retryPolicy.ExecuteAsync(async () =>
        {
            // Attempt the rate-limited API call
            await MakeApiCallAsync(technique, clientKey);
        });

        // Log that execution has finished after retries or successful call
        Console.WriteLine($"Finished rate-limited execution for client: {clientKey} using {technique}.");
    }
}

public class RateLimitExceededException : Exception
{
    public RateLimitExceededException(string message) : base(message) { }
}
