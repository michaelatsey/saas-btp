namespace SaasBtp.Architecture.Tests;

/// <summary>The assertion of the checks that collect their breaches themselves.</summary>
internal static class Breaches
{
    /// <summary>Fails with every breach in full, one per line, when there is any.</summary>
    public static void None(IReadOnlyCollection<string> breaches)
    {
        if (breaches.Count > 0)
        {
            Assert.Fail($"{breaches.Count} breach(es):{Environment.NewLine}{string.Join(Environment.NewLine, breaches)}");
        }
    }
}
