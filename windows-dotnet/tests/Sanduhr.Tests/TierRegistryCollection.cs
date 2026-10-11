namespace Sanduhr.Tests;

/// <summary>
/// <see cref="Sanduhr.Core.TierModel"/> keeps the dynamically registered scoped tiers
/// in process-wide static state. xUnit runs test classes in parallel, so a class that
/// resets or registers tiers (TierModelTests, UsageFetcherTests) could clear the
/// registry between another class's register and assert. Every class that writes
/// that registry joins this collection, which runs its classes one at a time.
/// </summary>
[CollectionDefinition(Name)]
public sealed class TierRegistryCollection
{
    public const string Name = "TierModel dynamic registry";
}
