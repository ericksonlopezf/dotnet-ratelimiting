// Copyright © Erickson Lopez. MIT License.
using Xunit;

namespace EricksonLopez.RateLimiting.Tests;

/// <summary>
/// Defines an xUnit test collection that disables test parallelization for OpenTelemetry metric listener test suites.
/// </summary>
[CollectionDefinition("MetricsCollection", DisableParallelization = true)]
public class MetricsCollectionDefinition
{
}
