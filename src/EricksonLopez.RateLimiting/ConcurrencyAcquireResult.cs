// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.RateLimiting;

/// <summary>
/// Defines the outcome of an attempt to acquire permits from a concurrency partition.
/// </summary>
internal enum ConcurrencyAcquireResult
{
    /// <summary>
    /// Indicates that the requested permits were successfully acquired.
    /// </summary>
    Acquired,

    /// <summary>
    /// Indicates that the permit acquisition was rejected because the concurrency limit was reached.
    /// </summary>
    Rejected,

    /// <summary>
    /// Indicates that the partition has been retired from service and cannot satisfy acquisitions.
    /// </summary>
    Retired
}
