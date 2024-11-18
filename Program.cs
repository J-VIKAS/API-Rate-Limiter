using Microsoft.AspNetCore.Mvc.Filters;
using StackExchange.Redis;
using System.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var redisOptions = new ConfigurationOptions
{
    EndPoints = { builder.Configuration["Redis:Endpoint"] },
    User = builder.Configuration["Redis:Username"],
    Password = builder.Configuration["Redis:Password"],
    AbortOnConnectFail = builder.Configuration.GetValue<bool>("Redis:AbortOnConnectFail"),
    Ssl = builder.Configuration.GetValue<bool>("Redis:Ssl")
};

var redis = ConnectionMultiplexer.Connect(redisOptions);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

builder.Services.Configure<PollySettings>(builder.Configuration.GetSection("PollySettings"));

builder.Services.AddSingleton<IRateLimitedService, RateLimitedService>();

builder.Services.AddSingleton<IFixedWindowRedisRateLimiter, FixedWindowRedisRateLimiter>();

builder.Services.AddSingleton<ISlidingWindowRedisRateLimiter, SlidingWindowRedisRateLimiter>();

builder.Services.AddSingleton<ITokenBucketRedisRateLimiter, TokenBucketRedisRateLimiter>();

builder.Services.AddSingleton<IConcurrencyRedisRateLimiter, ConcurrencyRedisRateLimiter>();


var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(); 
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
