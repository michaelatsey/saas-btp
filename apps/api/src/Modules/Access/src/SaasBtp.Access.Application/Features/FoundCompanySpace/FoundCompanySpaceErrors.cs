using MicroKit.Result;

namespace SaasBtp.Access.Application.Features.FoundCompanySpace;

/// <summary>
/// Expected business-flow failures of the founding capability, returned as <see cref="Result"/>
/// failures — never thrown. Infrastructure faults (database down, unique-violation on a replayed
/// intent) are NOT here: they are exceptions, because no business flow anticipates them.
/// </summary>
public static class FoundCompanySpaceErrors
{
    /// <summary>
    /// The identity could not be established at the auth boundary — BP-001's first rupture point:
    /// terminal failure, no business state exists (Design Package §1).
    /// </summary>
    /// <param name="Reason">What the identity boundary reported.</param>
    public sealed record IdentityNotEstablished(string Reason)
        : Error(ErrorCode.From("ACCESS.FOUNDING.IDENTITY_NOT_ESTABLISHED"),
            $"The founder's identity could not be established: {Reason}")
    {
        /// <inheritdoc/>
        public override ErrorCategory Category => ErrorCategory.External;
    }

    /// <summary>
    /// The orphan-adoption lookup matched MORE than one account for the founder's e-mail. Adopting
    /// either would attach business facts to the wrong person — an impersonation, not a functional
    /// bug — so the command aborts loudly and adopts nothing (ADR-ARCH-011, security-critical).
    /// </summary>
    public sealed record AmbiguousFounderIdentity()
        : Error(ErrorCode.From("ACCESS.FOUNDING.IDENTITY_AMBIGUOUS"),
            "More than one existing account matches the founder's e-mail; refusing to adopt any.")
    {
        /// <inheritdoc/>
        public override ErrorCategory Category => ErrorCategory.Conflict;

        /// <inheritdoc/>
        public override ErrorSeverity Severity => ErrorSeverity.Critical;
    }
}
