using System.Linq.Expressions;
using MicroKit.Domain.Specifications;
using Xunit;

namespace MicroKit.Domain.Tests.Specifications;

public sealed class SpecificationTests
{
    private static readonly Candidate[] Candidates =
    [
        new(Age: 17, Active: false),
        new(Age: 17, Active: true),
        new(Age: 30, Active: false),
        new(Age: 30, Active: true),
    ];

    [Theory]
    [InlineData(17, false)]
    [InlineData(18, true)]
    [InlineData(30, true)]
    public void IsSatisfiedBy_SpecDefiningOnlyToExpression_EvaluatesTheExpression(int age, bool expected)
    {
        Assert.Equal(expected, new AdultSpec().IsSatisfiedBy(new Candidate(age, Active: false)));
    }

    public static TheoryData<string> Composites => ["and", "or", "not", "nested"];

    [Theory]
    [MemberData(nameof(Composites))]
    public void IsSatisfiedBy_Composite_AgreesWithItsExpression(string composite)
    {
        var spec = Build(composite);
        var expression = spec.ToExpression().Compile();

        var results = Candidates.Select(c => spec.IsSatisfiedBy(c)).ToArray();

        Assert.Equal(Candidates.Select(expression).ToArray(), results);
        Assert.Contains(true, results);
        Assert.Contains(false, results);
    }

    [Fact]
    public void IsSatisfiedBy_Composites_MatchTheirBooleanDefinition()
    {
        var adult = new AdultSpec();
        var active = new ActiveSpec();

        foreach (var c in Candidates)
        {
            var isAdult = c.Age >= 18;
            Assert.Equal(isAdult && c.Active, adult.And(active).IsSatisfiedBy(c));
            Assert.Equal(isAdult || c.Active, adult.Or(active).IsSatisfiedBy(c));
            Assert.Equal(!isAdult, adult.Not().IsSatisfiedBy(c));
        }
    }

    [Fact]
    public void IsSatisfiedBy_CompilesTheExpressionOncePerInstance()
    {
        var spec = new CountingSpec();

        Assert.True(spec.IsSatisfiedBy(new Candidate(30, Active: true)));
        Assert.False(spec.IsSatisfiedBy(new Candidate(17, Active: true)));
        Assert.True(spec.IsSatisfiedBy(new Candidate(40, Active: true)));

        Assert.Equal(1, spec.ToExpressionCalls);
    }

    [Fact]
    public void IsSatisfiedBy_CacheIsPerInstance()
    {
        var first = new CountingSpec();
        var second = new CountingSpec();

        first.IsSatisfiedBy(new Candidate(30, Active: true));
        second.IsSatisfiedBy(new Candidate(30, Active: true));

        Assert.Equal(1, first.ToExpressionCalls);
        Assert.Equal(1, second.ToExpressionCalls);
    }

    [Fact]
    public void IsSatisfiedBy_ConcurrentFirstUse_NeverYieldsAWrongResult()
    {
        var spec = new AdultSpec();

        var results = Enumerable.Range(0, 64)
            .AsParallel()
            .Select(i => (Expected: i % 2 == 0, Actual: spec.IsSatisfiedBy(new Candidate(i % 2 == 0 ? 30 : 10, false))))
            .ToArray();

        Assert.All(results, r => Assert.True(r.Expected == r.Actual));
    }

    [Fact]
    public void IsSatisfiedBy_IsNotOverridable()
    {
        var method = typeof(Specification<Candidate>).GetMethod(nameof(Specification<Candidate>.IsSatisfiedBy))!;

        Assert.False((method.IsVirtual && !method.IsFinal));
        Assert.True(typeof(Specification<Candidate>).GetMethod(nameof(Specification<Candidate>.ToExpression))!
            .IsAbstract);
    }

    private static ISpecification<Candidate> Build(string composite) => composite switch
    {
        "and" => new AdultSpec().And(new ActiveSpec()),
        "or" => new AdultSpec().Or(new ActiveSpec()),
        "not" => new AdultSpec().Not(),
        "nested" => ((Specification<Candidate>)new AdultSpec().Not()).Or(new AdultSpec().And(new ActiveSpec())),
        _ => throw new ArgumentOutOfRangeException(nameof(composite)),
    };

    public sealed record Candidate(int Age, bool Active);

    private sealed class AdultSpec : Specification<Candidate>
    {
        public override Expression<Func<Candidate, bool>> ToExpression() => c => c.Age >= 18;
    }

    private sealed class ActiveSpec : Specification<Candidate>
    {
        public override Expression<Func<Candidate, bool>> ToExpression() => c => c.Active;
    }

    private sealed class CountingSpec : Specification<Candidate>
    {
        public int ToExpressionCalls { get; private set; }

        public override Expression<Func<Candidate, bool>> ToExpression()
        {
            ToExpressionCalls++;
            return c => c.Age >= 18;
        }
    }
}
