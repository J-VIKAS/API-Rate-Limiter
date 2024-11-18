using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public interface IConcurrencyRedisRateLimiter
{
    Task<bool> IsAllowedAsync(string clientKey);
    Task ReleaseAsync(string clientKey);
}

public class ConcurrencyRedisRateLimiter : IConcurrencyRedisRateLimiter
{
    private readonly IDatabase _db;
    private readonly int _maxConcurrentRequests; // Max 50 concurrent requests
    private readonly Queue<string> _requestQueue = new Queue<string>(); // Queue for additional requests

    public ConcurrencyRedisRateLimiter(IConnectionMultiplexer redis, IConfiguration configuration)
    {
        _db = redis.GetDatabase();
        _maxConcurrentRequests = configuration.GetValue<int>("RateLimiting:Concurrency:MaxConcurrentRequests");

        // Log initialization details
        Console.WriteLine($"Concurrency Rate Limiter initialized with max concurrent requests: {_maxConcurrentRequests}.");
    }

    public async Task<bool> IsAllowedAsync(string clientKey)
    {
        var concurrentRequests = await _db.StringGetAsync(clientKey);

        if (!concurrentRequests.HasValue)
        {
            // Set the initial value with expiration time
            await _db.StringSetAsync(clientKey, 1);
            Console.WriteLine($"Client: {clientKey}, First request accepted. Concurrent request count set to 1.");
            return true;
        }

        int currentCount = (int)concurrentRequests;

        Console.WriteLine($"Client: {clientKey}, Current concurrent request count: {currentCount}.");

        if (currentCount >= _maxConcurrentRequests)
        {
            // Add to the queue if limit is reached
            _requestQueue.Enqueue(clientKey);
            Console.WriteLine($"Client: {clientKey}, Added to queue. Concurrency limit exceeded.");
            return false; // Request queued
        }

        // Increment the concurrent request count
        await _db.StringIncrementAsync(clientKey);
        Console.WriteLine($"Client: {clientKey}, Request accepted. Incremented concurrent request count to {currentCount + 1}.");
        return true;
    }

    public async Task ReleaseAsync(string clientKey)
    {
        // Decrement the concurrent request count when the request finishes
        await _db.StringDecrementAsync(clientKey);
        Console.WriteLine($"Client: {clientKey}, Request finished. Decremented concurrent request count.");

        // Check if there are queued requests and process them
        if (_requestQueue.Count > 0)
        {
            var nextClientKey = _requestQueue.Dequeue();
            await IsAllowedAsync(nextClientKey); // Attempt to allow the next queued request
            Console.WriteLine($"Client: {nextClientKey} is now allowed to proceed.");
        }
    }
}
