using SaasBtp.Access.Domain;
using Shouldly;
using Xunit;

namespace SaasBtp.Access.Domain.UnitTests;

public sealed class ScaffoldSmokeTests
{
    [Fact]
    public void Domain_assembly_marker_is_reachable()
        => typeof(AccessDomainAssemblyMarker).Assembly.ShouldNotBeNull();
}
