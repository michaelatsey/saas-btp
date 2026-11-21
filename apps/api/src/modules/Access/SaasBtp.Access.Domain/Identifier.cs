namespace SaasBtp.Access.Domain;

/// <summary>The guard every aggregate of the domain applies to the identifiers it is given.</summary>
internal static class Identifier
{
    /// <summary>Refuses the default value of an identifier: it identifies nothing.</summary>
    internal static void Require(Guid value, string paramName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("An identifier cannot be the default value.", paramName);
    }
}
