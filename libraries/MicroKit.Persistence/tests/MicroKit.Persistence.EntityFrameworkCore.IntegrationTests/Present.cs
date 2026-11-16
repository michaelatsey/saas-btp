using Xunit;

namespace MicroKit.Persistence.EntityFrameworkCore.IntegrationTests;

/// <summary>
/// Shouldly's <c>ShouldNotBeNull()</c> returned the value it checked, and MicroKit's tests use that
/// value; xUnit's <see cref="Assert.NotNull(object?)"/> returns nothing for a reference type.
/// Added here with the rewrite of the assertions (ADR-ARCH-035 §7).
/// </summary>
internal static class Present
{
    public static T NotNull<T>(T? value)
        where T : class
    {
        Assert.NotNull(value);
        return value;
    }
}
