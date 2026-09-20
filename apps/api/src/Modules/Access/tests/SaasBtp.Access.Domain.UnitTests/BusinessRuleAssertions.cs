using MicroKit.Domain.Exceptions;
using MicroKit.Domain.Rules;
using Shouldly;

namespace SaasBtp.Access.Domain.UnitTests;

/// <summary>
/// Asserts that an aggregate refused construction because a SPECIFIC invariant was broken — not
/// merely that "something threw". Naming the rule is what makes the test a statement about the
/// model rather than about the code.
/// </summary>
internal static class BusinessRuleAssertions
{
    /// <summary>Asserts that <paramref name="act"/> breaks the rule <typeparamref name="TRule"/>.</summary>
    /// <typeparam name="TRule">The invariant expected to be violated.</typeparam>
    /// <param name="act">The construction expected to be refused.</param>
    public static void ShouldBreakRule<TRule>(Action act)
        where TRule : IBusinessRule
    {
        var violation = Should.Throw<BusinessRuleViolationException>(act);

        violation.ViolatedRule.ShouldBeOfType<TRule>();
    }
}
