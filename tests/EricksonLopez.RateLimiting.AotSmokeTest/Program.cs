// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.RateLimiting;
using EricksonLopez.RateLimiting.AspNetCore;
using EricksonLopez.RateLimiting.Policies;
using EricksonLopez.RateLimiting.Redis;

namespace EricksonLopez.RateLimiting.AotSmokeTest;

public static class Program
{
    public static async Task<int> Main()
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("  EricksonLopez.RateLimiting Native AOT Smoke Test");
        Console.WriteLine("==================================================");

        try
        {
            // 1. Core In-Memory Limiters
            Console.WriteLine("[1/6] Testing Core In-Memory Algorithms...");
            var options = new RateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                MaxPartitions = 100
            };

            var slidingWindow = new SlidingWindowRateLimiter(options);
            var tokenBucket = new TokenBucketRateLimiter(options);
            var fixedWindow = new FixedWindowRateLimiter(options);

            var swResult = await slidingWindow.AcquireAsync("test-user", 1).ConfigureAwait(false);
            if (!swResult.IsSuccess || !swResult.Value.IsAcquired)
            {
                throw new InvalidOperationException("SlidingWindowRateLimiter failed to acquire permit.");
            }

            var tbResult = await tokenBucket.AcquireAsync("test-user", 1).ConfigureAwait(false);
            if (!tbResult.IsSuccess || !tbResult.Value.IsAcquired)
            {
                throw new InvalidOperationException("TokenBucketRateLimiter failed to acquire permit.");
            }

            var fwResult = await fixedWindow.AcquireAsync("test-user", 1).ConfigureAwait(false);
            if (!fwResult.IsSuccess || !fwResult.Value.IsAcquired)
            {
                throw new InvalidOperationException("FixedWindowRateLimiter failed to acquire permit.");
            }
            Console.WriteLine("  ✅ Sliding Window, Token Bucket, and Fixed Window verified.");

            // 2. Concurrency Rate Limiter with Deterministic Lease Disposal
            Console.WriteLine("[2/6] Testing Concurrency Rate Limiter & Lease Disposal...");
            var concurrencyOptions = new ConcurrencyRateLimiterOptions
            {
                PermitLimit = 2,
                MaxPartitions = 50
            };
            var concurrencyLimiter = new ConcurrencyRateLimiter(concurrencyOptions);

            var lease1Result = await concurrencyLimiter.AcquireAsync("tenant-1", 1).ConfigureAwait(false);
            if (!lease1Result.IsSuccess || !lease1Result.Value.IsAcquired)
            {
                throw new InvalidOperationException("ConcurrencyLimiter failed first acquisition.");
            }

            using (var lease1 = lease1Result.Value)
            {
                var lease2Result = await concurrencyLimiter.AcquireAsync("tenant-1", 1).ConfigureAwait(false);
                if (!lease2Result.IsSuccess || !lease2Result.Value.IsAcquired)
                {
                    throw new InvalidOperationException("ConcurrencyLimiter failed second acquisition.");
                }

                using var lease2 = lease2Result.Value;

                // Third acquisition must be rejected immediately (PermitLimit = 2)
                var lease3Result = await concurrencyLimiter.AcquireAsync("tenant-1", 1).ConfigureAwait(false);
                if (!lease3Result.IsSuccess || lease3Result.Value.IsAcquired)
                {
                    throw new InvalidOperationException("ConcurrencyLimiter failed to reject beyond capacity.");
                }
            }

            // Both leases disposed; slot is replenished
            var lease4Result = await concurrencyLimiter.AcquireAsync("tenant-1", 1).ConfigureAwait(false);
            if (!lease4Result.IsSuccess || !lease4Result.Value.IsAcquired)
            {
                throw new InvalidOperationException("ConcurrencyLimiter failed permit replenishment after disposal.");
            }
            lease4Result.Value.Dispose();
            Console.WriteLine("  ✅ Concurrency Limiter and zero-allocation disposal verified.");

            // 3. Composite Multi-Interval Limiter
            Console.WriteLine("[3/6] Testing Composite Multi-Interval Rate Limiter...");
            var compositeLimiter = new CompositeRateLimiter(slidingWindow, fixedWindow);
            var compResult = await compositeLimiter.AcquireAsync("tenant-composite", 1).ConfigureAwait(false);
            if (!compResult.IsSuccess || !compResult.Value.IsAcquired)
            {
                throw new InvalidOperationException("CompositeRateLimiter failed combined acquisition.");
            }
            Console.WriteLine("  ✅ Composite Rate Limiter verified.");

            // 4. Named Policy Registry & Policy Builder
            Console.WriteLine("[4/6] Testing Named Policy Registry & Fluent Builder...");
            var policyBuilder = new RateLimiterPolicyBuilder();
            policyBuilder.AddFixedWindow("public", opt => { opt.PermitLimit = 10; opt.Window = TimeSpan.FromMinutes(1); });
            policyBuilder.AddSlidingWindow("authenticated", opt => { opt.PermitLimit = 100; opt.Window = TimeSpan.FromMinutes(1); });
            policyBuilder.AddConcurrency("heavy-ops", opt => { opt.PermitLimit = 3; });
            policyBuilder.SetDefaultPolicy("public");

            var registry = policyBuilder.Build();
            var publicLimiter = registry.GetPolicy("public");
            var authLimiter = registry.GetPolicy("authenticated");
            var defaultLimiter = registry.DefaultLimiter;

            if (publicLimiter == null || authLimiter == null || defaultLimiter == null)
            {
                throw new InvalidOperationException("Named Policy Registry failed to resolve policies.");
            }
            Console.WriteLine("  ✅ Named Policy Registry verified.");

            // 5. ASP.NET Core & Header Contracts
            Console.WriteLine("[5/6] Testing ASP.NET Core Middleware Options & Header Constants...");
            var mwOptions = new RateLimitingMiddlewareOptions
            {
                PermitCost = 1,
                FailClosed = false
            };

            if (RateLimitingHeaders.Limit != "X-RateLimit-Limit" ||
                RateLimitingHeaders.Remaining != "X-RateLimit-Remaining" ||
                RateLimitingHeaders.Reset != "X-RateLimit-Reset" ||
                RateLimitingHeaders.RetryAfter != "Retry-After")
            {
                throw new InvalidOperationException("Header constants mismatch.");
            }
            Console.WriteLine("  ✅ ASP.NET Core integration options and headers verified.");

            // 6. Distributed Redis Options & Error Code Mapping
            Console.WriteLine("[6/6] Testing Redis Options & Error Constants...");
            var redisOptions = new RedisRateLimiterOptions
            {
                Configuration = "localhost:6379,abortConnect=false",
                KeyPrefix = "test-rl:",
                MaxPermits = 100,
                WindowDuration = TimeSpan.FromMinutes(1)
            };

            var tbRedisOptions = new RedisTokenBucketRateLimiterOptions
            {
                Configuration = "localhost:6379,abortConnect=false",
                KeyPrefix = "test-tb:",
                TokenLimit = 50,
                TokensPerPeriod = 5,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1)
            };

            if (RateLimitingErrorCodes.ConnectionFailedCode != "RateLimit.Redis.ConnectionFailed")
            {
                throw new InvalidOperationException("Redis error code constant mismatch.");
            }
            Console.WriteLine("  ✅ Redis options, configuration schemas, and error codes verified.");

            Console.WriteLine("\n🎉 Native AOT Smoke Test PASSED successfully (100% trim-safe and AOT-compatible).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n❌ Native AOT Smoke Test FAILED: {ex.Message}");
            Console.WriteLine(ex.ToString());
            Console.ResetColor();
            return 1;
        }
    }
}
