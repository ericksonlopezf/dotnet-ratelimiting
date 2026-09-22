// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.RateLimiting.AspNetCore;

/// <summary>
/// Disables rate limiting on the target endpoint or action, overriding any parent or default policies.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class DisableRateLimitingAttribute : Attribute, IDisableRateLimitingMetadata
{
}
