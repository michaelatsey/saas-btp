using System.Data.Common;

namespace SaasBtp.Access.Features;

/// <summary>
/// How the module's endpoints name a failure in their log: by class, never by a value of the request
/// or an exception message (ADR-ARCH-032 §5). It depends on no ASP.NET Core type: the answers stay
/// in the <c>*Endpoint</c> types (ADR-ARCH-034 decision 7).
/// </summary>
internal static class EndpointFailure
{
    /// <summary>The class of a failure: the type names of the exception and its inner ones, and a database's SQLSTATE.</summary>
    public static string ClassOf(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        var parts = new List<string>();
        for (var current = failure; current is not null; current = current.InnerException)
        {
            parts.Add(current is DbException { SqlState: { } state } ? $"{current.GetType().Name} (SQLSTATE {state})" : current.GetType().Name);
        }

        return string.Join(" <- ", parts);
    }
}
