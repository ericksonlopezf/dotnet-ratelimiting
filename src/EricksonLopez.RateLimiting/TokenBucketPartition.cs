// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.RateLimiting;

internal sealed class TokenBucketPartition
{
    private readonly double _capacity;
    private readonly double _refillRatePerSecond;
    private double _currentTokens;
    private DateTimeOffset _lastRefillTime;
    private readonly object _lock = new();

    public TokenBucketPartition(int capacity, TimeSpan window, DateTimeOffset startTime)
    {
        _capacity = capacity;
        _currentTokens = capacity;
        _refillRatePerSecond = capacity / window.TotalSeconds;
        _lastRefillTime = startTime;
    }

    public RateLimitLease TryAcquire(int permits, DateTimeOffset now)
    {
        lock (_lock)
        {
            var elapsedSeconds = (now - _lastRefillTime).TotalSeconds;
            if (elapsedSeconds > 0)
            {
                _currentTokens = Math.Min(_capacity, _currentTokens + (elapsedSeconds * _refillRatePerSecond));
                _lastRefillTime = now;
            }

            if (_currentTokens >= permits)
            {
                _currentTokens -= permits;
                var missingToCapacity = Math.Max(0.0, _capacity - _currentTokens);
                var secondsToFull = missingToCapacity / _refillRatePerSecond;
                var resetTime = now.AddSeconds(secondsToFull);
                return RateLimitLease.Successful((int)_currentTokens, resetTime, null, (int)_capacity);
            }

            var missingTokens = permits > _capacity ? _capacity : (permits - _currentTokens);
            var rawSecondsToWait = missingTokens / _refillRatePerSecond;
            var secondsToWait = Math.Min(86400.0, rawSecondsToWait);

            var retryAfter = TimeSpan.FromSeconds(Math.Max(0.001, secondsToWait));
            return RateLimitLease.Rejected(retryAfter, now.Add(retryAfter), (int)_capacity);
        }
    }

    public bool IsIdle(DateTimeOffset now)
    {
        lock (_lock)
        {
            var elapsedSeconds = Math.Max(0.0, (now - _lastRefillTime).TotalSeconds);
            var simulatedTokens = Math.Min(_capacity, _currentTokens + (elapsedSeconds * _refillRatePerSecond));
            return simulatedTokens >= _capacity;
        }
    }
}
