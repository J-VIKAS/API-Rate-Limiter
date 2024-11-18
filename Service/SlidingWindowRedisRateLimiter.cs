using StackExchange.Redis;
using System;
using System.Threading.Tasks;

public interface ISlidingWindowRedisRateLimiter
{
    Task<bool> IsAllowedAsync(string clientKey);
}

public class SlidingWindowRedisRateLimiter : ISlidingWindowRedisRateLimiter
{
    private readonly IDatabase _db;
    private readonly int _limit; // 600 requests
    private readonly TimeSpan _timeWindow; // 1 hour

    public SlidingWindowRedisRateLimiter(IConnectionMultiplexer redis, IConfiguration configuration)
    {
        _db = redis.GetDatabase();
        _limit = configuration.GetValue<int>("RateLimiting:SlidingWindow:Limit");
        _timeWindow = TimeSpan.FromHours(configuration.GetValue<int>("RateLimiting:SlidingWindow:WindowHours"));

        // Log initialization details
        Console.WriteLine($"SlidingWindow Rate Limiter initialized with limit: {_limit} requests per {_timeWindow.TotalHours} hours.");
    }

    public async Task<bool> IsAllowedAsync(string clientKey)
    {
        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var windowStart = currentTime - _timeWindow.TotalSeconds;

        // Log request details
        Console.WriteLine($"Checking sliding window rate limit for client: {clientKey}. Current time: {currentTime}, Window start: {windowStart}");

        var transaction = _db.CreateTransaction();
        var queueKey = $"{clientKey}:requests";

        // Remove outdated requests
        Console.WriteLine($"Removing outdated requests for client: {clientKey} before timestamp: {windowStart}");
        _ = transaction.SortedSetRemoveRangeByScoreAsync(queueKey, 0, windowStart);

        var currentCountTask = transaction.SortedSetLengthAsync(queueKey);

        // Add current request
        Console.WriteLine($"Adding current request for client: {clientKey} with timestamp: {currentTime}");
        _ = transaction.SortedSetAddAsync(queueKey, currentTime, currentTime);

        // Set the key expiry to match the sliding window time
        Console.WriteLine($"Setting key expiration for {queueKey} to {_timeWindow.TotalHours} hours.");
        _ = transaction.KeyExpireAsync(queueKey, _timeWindow);

        await transaction.ExecuteAsync();
        var currentCount = await currentCountTask;

        // Log the current count of requests in the sliding window
        Console.WriteLine($"Client {clientKey} has made {currentCount} requests in the current sliding window.");

        if (currentCount > _limit)
        {
            // Log when rate limit is exceeded
            Console.WriteLine($"Rate limit exceeded for client: {clientKey}. Current count: {currentCount}, Limit: {_limit}.");
            return false; // Request throttled
        }

        // Log when the request is allowed
        Console.WriteLine($"Request allowed for client: {clientKey}. Current count: {currentCount}, Limit: {_limit}.");
        return true; // Request allowed
    }
}
