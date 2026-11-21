namespace MicroKit.Persistence.Analyzers;

/// <summary>
/// The help link of every MKP diagnostic: its own section of this copy's README
/// (libraries/MicroKit.Persistence/README.md), one <c>### MKPnnn</c> heading per ID.
/// </summary>
internal static class HelpLinks
{
    private const string Readme = "https://github.com/michaelatsey/saas-btp/blob/main/libraries/MicroKit.Persistence/README.md#";

    /// <summary>Returns the README anchor of <paramref name="diagnosticId"/>, e.g. <c>…/README.md#mkp001</c>.</summary>
    public static string For(string diagnosticId) => Readme + diagnosticId.ToLowerInvariant();
}
