namespace SaasBtp.Access.Domain;

/// <summary>What is wrong with a declared name, as rule BP001-R7 measures it.</summary>
public enum NameDefect
{
    /// <summary>The name is valid.</summary>
    None = 0,

    /// <summary>The name is empty once the white space around it is removed.</summary>
    Missing = 1,

    /// <summary>The name holds more Unicode scalar values than the limit, once trimmed.</summary>
    TooLong = 2,
}
