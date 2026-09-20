using MicroKit.Domain.Rules;

namespace SaasBtp.Access.Domain.Workspaces.Rules;

/// <summary>
/// A workspace is the operational space a team navigates to, so it always carries a name. Its
/// name need not be unique — the FORM of workspace resolution is deliberately left open by the
/// business model — but it must exist.
/// </summary>
/// <param name="displayName">The declared name under test.</param>
public sealed class WorkspaceNameMustBeProvidedRule(string? displayName) : BusinessRule
{
    /// <inheritdoc/>
    public override bool IsBroken() => string.IsNullOrWhiteSpace(displayName);

    /// <inheritdoc/>
    public override string Message => "A workspace must have a name.";
}
