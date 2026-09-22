// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.RateLimiting.AspNetCore;

/// <summary>
/// Specifies that rate limiting should be enabled on the target endpoint or controller using the given policy name.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class EnableRateLimitingAttribute : Attribute, IEnableRateLimitingMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EnableRateLimitingAttribute"/> class with the specified policy name.
    /// </summary>
    /// <param name="policyName">The unique identifier of the rate limiting policy to apply</param>
    /// <exception cref="ArgumentException"><paramref name="policyName"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public EnableRateLimitingAttribute(string policyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        PolicyName = policyName;
    }

    /// <inheritdoc />
    public string PolicyName { get; }
}
