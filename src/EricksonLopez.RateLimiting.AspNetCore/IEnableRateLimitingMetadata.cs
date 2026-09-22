// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.RateLimiting.AspNetCore;

/// <summary>
/// Defines a contract for endpoint metadata that associates a named rate limiting policy.
/// </summary>
public interface IEnableRateLimitingMetadata
{
    /// <summary>
    /// Gets the unique identifier of the rate limiting policy to apply to the endpoint.
    /// </summary>
    string PolicyName { get; }
}
