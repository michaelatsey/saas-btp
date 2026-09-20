using SaasBtp.Access.Application.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace SaasBtp.Access.Application.UnitTests;

public sealed class ScaffoldSmokeTests
{
    [Fact]
    public void Add_application_seam_returns_the_collection()
    {
        var services = new ServiceCollection();

        var result = services.AddAccessApplication();

        result.ShouldBeSameAs(services);
    }
}
