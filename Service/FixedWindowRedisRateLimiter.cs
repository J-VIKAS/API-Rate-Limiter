using StackExchange.Redis;
using System;
using System.Threading.Tasks;

public interface IFixedWindowRedisRateLimiter
{
    Task<bool> IsAllowedAsync(string clientKey);
}

public class FixedWindowRedisRateLimiter : IFixedWindowRedisRateLimiter
{
    private readonly IDatabase _db;
    private readonly int _limit; // 500 requests
    private readonly TimeSpan _timeWindow; // Per 10 minutes

    public FixedWindowRedisRateLimiter(IConnectionMultiplexer redis, IConfiguration configuration)
    {
        _db = redis.GetDatabase();
        _limit = configuration.GetValue<int>("RateLimiting:FixedWindow:Limit");
        _timeWindow = TimeSpan.FromMinutes(configuration.GetValue<int>("RateLimiting:FixedWindow:WindowMinutes"));

        // Log initialization details
        Console.WriteLine($"FixedWindow Rate Limiter initialized with limit: {_limit} requests per {_timeWindow.TotalMinutes} minutes.");
    }

    public async Task<bool> IsAllowedAsync(string clientKey)
    {
        // Log the incoming request details
        Console.WriteLine($"Checking rate limit for client: {clientKey}");

        var currentCount = await _db.StringGetAsync(clientKey);

        if (!currentCount.HasValue)
        {
            // Log the scenario where no value exists yet for this clientKey
            Console.WriteLine($"No existing record for client: {clientKey}. Initializing counter and setting expiry for {_timeWindow.TotalMinutes} minutes.");

            // Set the initial value with expiration time
            await _db.StringSetAsync(clientKey, 1, _timeWindow);
            return true; // Request allowed
        }

        int count = (int)currentCount;

        // Log the current count for this clientKey
        Console.WriteLine($"Client {clientKey} has made {count} requests so far.");

        if (count >= _limit)
        {
            // Log when rate limit is exceeded
            Console.WriteLine($"Rate limit exceeded for client: {clientKey}. Current count: {count}, Limit: {_limit}.");
            return false; // Rate limit exceeded
        }

        // Increment the counter
        await _db.StringIncrementAsync(clientKey);

        // Log successful request within rate limits
        Console.WriteLine($"Request allowed for client: {clientKey}. Incremented request count to {count + 1}.");

        return true; // Request allowed
    }
}
