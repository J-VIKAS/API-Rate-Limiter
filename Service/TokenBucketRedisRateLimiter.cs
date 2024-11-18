using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public interface ITokenBucketRedisRateLimiter
{
    Task<bool> IsAllowedAsync(string clientKey);
}

public class TokenBucketRedisRateLimiter : ITokenBucketRedisRateLimiter
{
    private readonly IDatabase _db;
    private readonly int _limit; // 1000 tokens
    private readonly TimeSpan _refillRate; // 1 token every 3.6 seconds
    private readonly TimeSpan _bucketExpiry; // 1-hour window
    private readonly Queue<string> _requestQueue = new Queue<string>(); // Queue for additional requests

    public TokenBucketRedisRateLimiter(IConnectionMultiplexer redis, IConfiguration configuration)
    {
        _db = redis.GetDatabase();
        _limit = configuration.GetValue<int>("RateLimiting:TokenBucket:Limit");
        _refillRate = TimeSpan.FromSeconds(configuration.GetValue<double>("RateLimiting:TokenBucket:RefillRateSeconds"));
        _bucketExpiry = TimeSpan.FromHours(configuration.GetValue<double>("RateLimiting:TokenBucket:BucketExpiryHours"));

        // Log initialization details
        Console.WriteLine($"TokenBucket Rate Limiter initialized with limit: {_limit} tokens, refill rate: {_refillRate.TotalSeconds} seconds, bucket expiry: {_bucketExpiry.TotalHours} hours.");
    }

    public async Task<bool> IsAllowedAsync(string clientKey)
    {
        var transaction = _db.CreateTransaction();
        var lastTokenKey = $"{clientKey}:lastRefill";
        var tokensKey = $"{clientKey}:tokens";

        // Get last refill time and available tokens
        var lastRefillTask = transaction.StringGetAsync(lastTokenKey);
        var tokensTask = transaction.StringGetAsync(tokensKey);

        await transaction.ExecuteAsync();

        var lastRefill = lastRefillTask.Result.HasValue ? (long)lastRefillTask.Result : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var tokens = tokensTask.Result.HasValue ? (int)tokensTask.Result : _limit;

        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var refillAmount = (int)((currentTime - lastRefill) / _refillRate.TotalSeconds);

        // Log the current state
        Console.WriteLine($"Client: {clientKey}, Current time: {currentTime}, Last refill time: {lastRefill}, Available tokens: {tokens}, Refill amount: {refillAmount}");

        // Refill tokens if needed
        tokens = Math.Min(tokens + refillAmount, _limit);
        lastRefill = refillAmount > 0 ? currentTime : lastRefill;

        if (tokens == 0)
        {
            // Add to the queue if no tokens are available
            _requestQueue.Enqueue(clientKey);
            Console.WriteLine($"Client: {clientKey}, Rate limit exceeded. No tokens left. Added to queue.");
            return false; // No tokens left, request queued
        }

        // Consume a token
        await _db.StringSetAsync(tokensKey, tokens - 1, _bucketExpiry);
        await _db.StringSetAsync(lastTokenKey, lastRefill, _bucketExpiry);

        // Log successful token consumption
        Console.WriteLine($"Client: {clientKey}, Token consumed. Remaining tokens: {tokens - 1}.");

        // Process queued requests if tokens are available
        await ProcessQueuedRequests(clientKey);

        return true;
    }

    private async Task ProcessQueuedRequests(string clientKey)
    {
        // Process requests in the queue if tokens are available
        while (_requestQueue.Count > 0)
        {
            var nextClientKey = _requestQueue.Peek();
            var tokens = (await _db.StringGetAsync($"{nextClientKey}:tokens")).HasValue ? (int)await _db.StringGetAsync($"{nextClientKey}:tokens") : _limit;

            if (tokens > 0)
            {
                // Dequeue and allow the next request
                _requestQueue.Dequeue();
                await IsAllowedAsync(nextClientKey);
                Console.WriteLine($"Client: {nextClientKey} is now allowed to proceed from the queue.");
            }
            else
            {
                // If no tokens are available, break the loop
                break;
            }
        }
    }
}
