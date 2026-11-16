using MicroKit.Domain.Aggregates;
using MicroKit.Domain.Exceptions;
using MicroKit.Domain.Identifiers;
using MicroKit.Domain.Rules;
using Xunit;

namespace MicroKit.Domain.Tests.Rules;

public sealed class BusinessRuleTests
{
    [Fact]
    public void Exception_CarriesTheRuleCode()
    {
        var rule = new OrderMustHaveItemsRule(itemCount: 0);

        var ex = new BusinessRuleViolationException(rule);

        Assert.Equal("order.must-have-items", ex.Code);
        Assert.Same(rule, ex.ViolatedRule);
        Assert.Equal("Business rule 'order.must-have-items' was violated: An order must contain at least one item.", ex.Message);
    }

    [Fact]
    public void CheckRule_BrokenRule_ThrowsWithTheRuleCode()
    {
        var order = new TestOrder(GuidId.New());

        var ex = Assert.Throws<BusinessRuleViolationException>(() => order.Place(itemCount: 0));

        Assert.Equal("order.must-have-items", ex.Code);
    }

    [Fact]
    public void CheckRule_SatisfiedRule_DoesNotThrow()
    {
        var order = new TestOrder(GuidId.New());

        Assert.Null(Record.Exception(() => order.Place(itemCount: 1)));
    }

    [Fact]
    public void Exception_NullRule_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new BusinessRuleViolationException(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Exception_RuleWithoutCode_ThrowsArgumentException(string? code)
    {
        // ArgumentNullException for null, ArgumentException otherwise: Shouldly accepted a derived type.
        Assert.ThrowsAny<ArgumentException>(() => new BusinessRuleViolationException(new CodelessRule(code!)));
    }

    private sealed class CodelessRule(string code) : BusinessRule
    {
        public override bool IsBroken() => true;

        public override string Message => "Always broken.";

        public override string Code => code;
    }

    private sealed class OrderMustHaveItemsRule(int itemCount) : BusinessRule
    {
        public override bool IsBroken() => itemCount == 0;

        public override string Message => "An order must contain at least one item.";

        public override string Code => "order.must-have-items";
    }

    private sealed class TestOrder(GuidId id) : AggregateRoot<GuidId>(id)
    {
        public void Place(int itemCount) => CheckRule(new OrderMustHaveItemsRule(itemCount));
    }
}
