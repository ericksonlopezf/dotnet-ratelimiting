// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.RateLimiting;

/// <summary>
/// Represents the result of an attempt to acquire permits from a rate limiter.
/// </summary>
/// <param name="IsAcquired">A value indicating whether the requested permits were successfully leased</param>
/// <param name="RemainingPermits">The number of permits remaining in the active window</param>
/// <param name="RetryAfter">The duration to wait before retrying when the lease is rejected</param>
/// <param name="ResetTime">The absolute point in time when the rate limit counter resets</param>
/// <param name="DisposeAction">The optional callback invoked when the lease is disposed</param>
/// <param name="Limit">The total permit limit or quota configured for the rate limiter</param>
public readonly record struct RateLimitLease(
    bool IsAcquired,
    int RemainingPermits,
    TimeSpan? RetryAfter = null,
    DateTimeOffset? ResetTime = null,
    Action? DisposeAction = null,
    int? Limit = null) : IDisposable
{
    /// <inheritdoc/>
    public void Dispose() => DisposeAction?.Invoke();

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitLease"/> struct without a disposal callback or limit quota.
    /// </summary>
    /// <param name="isAcquired">A value indicating whether the requested permits were successfully leased</param>
    /// <param name="remainingPermits">The number of permits remaining in the active window</param>
    /// <param name="retryAfter">The duration to wait before retrying when rejected</param>
    /// <param name="resetTime">The absolute point in time when the rate limit counter resets</param>
    public RateLimitLease(
        bool isAcquired,
        int remainingPermits,
        TimeSpan? retryAfter,
        DateTimeOffset? resetTime)
        : this(isAcquired, remainingPermits, retryAfter, resetTime, null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitLease"/> struct with a disposal callback.
    /// </summary>
    /// <param name="isAcquired">A value indicating whether the requested permits were successfully leased</param>
    /// <param name="remainingPermits">The number of permits remaining in the active window</param>
    /// <param name="retryAfter">The duration to wait before retrying when rejected</param>
    /// <param name="resetTime">The absolute point in time when the rate limit counter resets</param>
    /// <param name="disposeAction">The optional callback invoked when the lease is disposed</param>
    public RateLimitLease(
        bool isAcquired,
        int remainingPermits,
        TimeSpan? retryAfter,
        DateTimeOffset? resetTime,
        Action? disposeAction)
        : this(isAcquired, remainingPermits, retryAfter, resetTime, disposeAction, null)
    {
    }

    /// <summary>
    /// Deconstructs the lease into its acquisition state, remaining permits, retry interval, and reset time.
    /// </summary>
    /// <param name="isAcquired">When this method returns, contains a value indicating whether permits were acquired</param>
    /// <param name="remainingPermits">When this method returns, contains the remaining permits</param>
    /// <param name="retryAfter">When this method returns, contains the retry-after duration</param>
    /// <param name="resetTime">When this method returns, contains the reset timestamp</param>
    public void Deconstruct(
        out bool isAcquired,
        out int remainingPermits,
        out TimeSpan? retryAfter,
        out DateTimeOffset? resetTime)
    {
        isAcquired = IsAcquired;
        remainingPermits = RemainingPermits;
        retryAfter = RetryAfter;
        resetTime = ResetTime;
    }

    /// <summary>
    /// Deconstructs the lease into its acquisition state, remaining permits, retry interval, reset time, and disposal callback.
    /// </summary>
    /// <param name="isAcquired">When this method returns, contains a value indicating whether permits were acquired</param>
    /// <param name="remainingPermits">When this method returns, contains the remaining permits</param>
    /// <param name="retryAfter">When this method returns, contains the retry-after duration</param>
    /// <param name="resetTime">When this method returns, contains the reset timestamp</param>
    /// <param name="disposeAction">When this method returns, contains the disposal callback</param>
    public void Deconstruct(
        out bool isAcquired,
        out int remainingPermits,
        out TimeSpan? retryAfter,
        out DateTimeOffset? resetTime,
        out Action? disposeAction)
    {
        isAcquired = IsAcquired;
        remainingPermits = RemainingPermits;
        retryAfter = RetryAfter;
        resetTime = ResetTime;
        disposeAction = DisposeAction;
    }

    /// <summary>
    /// Creates a successful lease with the specified remaining permits and optional reset time.
    /// </summary>
    /// <param name="remainingPermits">The number of permits remaining in the active window</param>
    /// <param name="resetTime">The absolute point in time when the rate limit counter resets</param>
    /// <returns>A new <see cref="RateLimitLease"/> representing a granted lease.</returns>
    public static RateLimitLease Successful(int remainingPermits, DateTimeOffset? resetTime = null) =>
        new(true, remainingPermits, null, resetTime, null, null);

    /// <summary>
    /// Creates a successful lease with the specified remaining permits, reset time, and total permit quota.
    /// </summary>
    /// <param name="remainingPermits">The number of permits remaining in the active window</param>
    /// <param name="resetTime">The absolute point in time when the rate limit counter resets</param>
    /// <param name="limit">The total permit limit configured for the rate limiter policy</param>
    /// <returns>A new <see cref="RateLimitLease"/> representing a granted lease.</returns>
    public static RateLimitLease Successful(
        int remainingPermits,
        DateTimeOffset? resetTime,
        int? limit) =>
        new(true, remainingPermits, null, resetTime, null, limit);

    /// <summary>
    /// Creates a successful lease with an optional disposal callback.
    /// </summary>
    /// <param name="remainingPermits">The number of permits remaining in the active window</param>
    /// <param name="resetTime">The absolute point in time when the rate limit counter resets</param>
    /// <param name="disposeAction">The callback invoked when the lease is disposed</param>
    /// <returns>A new <see cref="RateLimitLease"/> representing a granted lease.</returns>
    public static RateLimitLease Successful(
        int remainingPermits,
        DateTimeOffset? resetTime,
        Action? disposeAction) =>
        new(true, remainingPermits, null, resetTime, disposeAction, null);

    /// <summary>
    /// Creates a successful lease with an optional disposal callback and policy quota limit.
    /// </summary>
    /// <param name="remainingPermits">The number of permits remaining in the active window</param>
    /// <param name="resetTime">The absolute point in time when the rate limit counter resets</param>
    /// <param name="disposeAction">The callback invoked when the lease is disposed</param>
    /// <param name="limit">The total permit limit configured for the rate limiter policy</param>
    /// <returns>A new <see cref="RateLimitLease"/> representing a granted lease.</returns>
    public static RateLimitLease Successful(
        int remainingPermits,
        DateTimeOffset? resetTime,
        Action? disposeAction,
        int? limit) =>
        new(true, remainingPermits, null, resetTime, disposeAction, limit);

    /// <summary>
    /// Creates a rejected lease with the specified retry duration, reset time, and permit quota.
    /// </summary>
    /// <param name="retryAfter">The duration to wait before retrying the request</param>
    /// <param name="resetTime">The absolute point in time when the rate limit counter resets</param>
    /// <param name="limit">The total permit limit configured for the rate limiter policy</param>
    /// <returns>A new <see cref="RateLimitLease"/> representing a rejected lease.</returns>
    public static RateLimitLease Rejected(TimeSpan retryAfter, DateTimeOffset? resetTime = null, int? limit = null) =>
        new(false, 0, retryAfter, resetTime, null, limit);
}
