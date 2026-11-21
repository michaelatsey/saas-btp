using System.Collections;
using MicroKit.Result.Tests.TestSupport;

namespace MicroKit.Result.Tests.Errors;

public sealed class ErrorCollectionTests
{
    // ── From ──────────────────────────────────────────────────────────────

    [Fact]
    public void From_WhenParamsArrayIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IError[]? errors = null;

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => ErrorCollection.From(errors!));

        // Assert
        Assert.Equal("errors", exception.ParamName);
    }

    [Fact]
    public void From_WhenParamsArrayIsEmpty_ThrowsArgumentException()
    {
        // Arrange + Act
        var exception = Assert.Throws<ArgumentException>(() => ErrorCollection.From());

        // Assert
        Assert.IsType<ArgumentException>(exception);
        Assert.Equal("errors", exception.ParamName);
    }

    [Fact]
    public void From_WhenEnumerableIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        IEnumerable<IError>? errors = null;

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => ErrorCollection.From(errors!));

        // Assert
        Assert.Equal("errors", exception.ParamName);
    }

    [Fact]
    public void From_WhenEnumerableIsEmpty_ThrowsArgumentException()
    {
        // Arrange
        var errors = Enumerable.Empty<IError>();

        // Act
        var exception = Assert.Throws<ArgumentException>(() => ErrorCollection.From(errors));

        // Assert
        Assert.IsType<ArgumentException>(exception);
        Assert.Equal("errors", exception.ParamName);
    }

    [Fact]
    public void From_WhenParamsArrayHasErrors_KeepsThemInOrder()
    {
        // Arrange
        var first = new TestError("first");
        var second = new OtherError();

        // Act
        var collection = ErrorCollection.From(first, second);

        // Assert
        Assert.Equal(2, collection.Count);
        Assert.Same(first, collection[0]);
        Assert.Same(second, collection[1]);
    }

    [Fact]
    public void From_WhenEnumerableHasErrors_KeepsThemInOrder()
    {
        // Arrange
        var first = new TestError("first");
        var second = new OtherError();
        var errors = new List<IError> { first, second };

        // Act
        var collection = ErrorCollection.From(errors);

        // Assert
        Assert.Equal(2, collection.Count);
        Assert.Same(first, collection[0]);
        Assert.Same(second, collection[1]);
    }

    [Fact]
    public void From_WhenSourceArrayMutatedAfterwards_KeepsOriginalErrors()
    {
        // Arrange
        var original = new TestError("original");
        IError[] errors = [original];
        var collection = ErrorCollection.From(errors);

        // Act
        errors[0] = new OtherError();

        // Assert
        Assert.Same(original, collection[0]);
    }

    // ── IError members ────────────────────────────────────────────────────

    [Fact]
    public void Code_WhenRead_IsMultiError()
    {
        // Arrange
        var collection = ErrorCollection.From(new TestError());

        // Act
        var code = collection.Code;

        // Assert
        Assert.Equal("MULTI.ERROR", code.Value);
    }

    [Fact]
    public void Message_WhenSeveralErrors_JoinsMessagesWithSemicolon()
    {
        // Arrange
        var collection = ErrorCollection.From(new TestError("a"), new OtherError(), new NotFoundTestError(3));

        // Act
        var message = collection.Message;

        // Assert
        Assert.Equal("Test error: a; Other error; Item 3 not found", message);
    }

    [Fact]
    public void Message_WhenSingleError_IsThatErrorMessage()
    {
        // Arrange
        var collection = ErrorCollection.From(new OtherError());

        // Act
        var message = collection.Message;

        // Assert
        Assert.Equal("Other error", message);
    }

    [Fact]
    public void Severity_WhenOneErrorIsCritical_IsCritical()
    {
        // Arrange
        var collection = ErrorCollection.From(ValidationError.Required("Name"), new TestError(), new CriticalTestError());

        // Act
        var severity = collection.Severity;

        // Assert
        Assert.Equal(ErrorSeverity.Critical, severity);
    }

    [Fact]
    public void Severity_WhenErrorAndWarning_IsError()
    {
        // Arrange
        var collection = ErrorCollection.From(ValidationError.Required("Name"), new TestError());

        // Act
        var severity = collection.Severity;

        // Assert
        Assert.Equal(ErrorSeverity.Error, severity);
    }

    [Fact]
    public void Severity_WhenAllErrorsAreWarnings_IsWarning()
    {
        // Arrange
        // The aggregate severity is the highest severity of its errors.
        var collection = ErrorCollection.From(ValidationError.Required("Name"), ValidationError.Required("Email"));

        // Act
        var severity = collection.Severity;

        // Assert
        Assert.Equal(ErrorSeverity.Warning, severity);
    }

    [Fact]
    public void Severity_WhenAllErrorsAreInformation_IsInformation()
    {
        // Arrange
        var collection = ErrorCollection.From(new InformationTestError("first"), new InformationTestError("second"));

        // Act
        var severity = collection.Severity;

        // Assert
        Assert.Equal(ErrorSeverity.Information, severity);
    }

    [Fact]
    public void Severity_WhenInformationAndWarning_IsWarning()
    {
        // Arrange
        var collection = ErrorCollection.From(new InformationTestError(), ValidationError.Required("Name"));

        // Act
        var severity = collection.Severity;

        // Assert
        Assert.Equal(ErrorSeverity.Warning, severity);
    }

    [Fact]
    public void Severity_WhenNestedCollectionOfWarnings_IsWarning()
    {
        // Arrange
        var inner = ErrorCollection.From(ValidationError.Required("Name"), ValidationError.Required("Email"));
        var collection = ErrorCollection.From(inner, ValidationError.Required("Phone"));

        // Act
        var severity = collection.Severity;

        // Assert
        Assert.Equal(ErrorSeverity.Warning, severity);
    }

    [Fact]
    public void Metadata_WhenErrorsHaveMetadata_IsEmpty()
    {
        // Arrange
        var collection = ErrorCollection.From(new CriticalTestError());

        // Act
        var metadata = collection.Metadata;

        // Assert
        Assert.Empty(metadata);
    }

    [Fact]
    public void FirstOrDefault_WhenNotEmpty_ReturnsFirstError()
    {
        // Arrange
        var first = new NotFoundTestError(1);
        var collection = ErrorCollection.From(first, new OtherError());

        // Act
        var head = collection.FirstOrDefault;

        // Assert
        Assert.Same(first, head);
    }

    // ── Category ──────────────────────────────────────────────────────────

    [Fact]
    public void Category_WhenAllErrorsShareCategory_IsThatCategory()
    {
        // Arrange
        var collection = ErrorCollection.From(ValidationError.Required("Name"), ValidationError.Required("Email"));

        // Act
        var category = collection.Category;

        // Assert
        Assert.Equal(ErrorCategory.Validation, category);
    }

    [Fact]
    public void Category_WhenOnlyClientFaults_IsFirstErrorCategory()
    {
        // Arrange
        var collection = ErrorCollection.From(new NotFoundTestError(1), new OtherError());

        // Act
        var category = collection.Category;

        // Assert
        Assert.Equal(ErrorCategory.NotFound, category);
    }

    [Fact]
    public void Category_WhenValidationThenTechnical_IsTechnical()
    {
        // Arrange
        var collection = ErrorCollection.From(ValidationError.Required("Name"), new TestError());

        // Act
        var category = collection.Category;

        // Assert
        Assert.Equal(ErrorCategory.Technical, category);
    }

    [Fact]
    public void Category_WhenSeveralServerFaults_IsFirstServerFault()
    {
        // Arrange
        var collection = ErrorCollection.From(new NotFoundTestError(1), new CriticalTestError(), new ExternalTestError());

        // Act
        var category = collection.Category;

        // Assert
        Assert.Equal(ErrorCategory.Unavailable, category);
    }

    // ── Filters ───────────────────────────────────────────────────────────

    [Fact]
    public void WithCategory_WhenSomeErrorsMatch_ReturnsMatchingErrorsInOrder()
    {
        // Arrange
        var first = new NotFoundTestError(1);
        var second = new NotFoundTestError(2);
        var collection = ErrorCollection.From(first, new OtherError(), second);

        // Act
        var filtered = collection.WithCategory(ErrorCategory.NotFound);

        // Assert
        Assert.Equal(2, filtered.Count);
        Assert.Same(first, filtered[0]);
        Assert.Same(second, filtered[1]);
    }

    [Fact]
    public void WithCategory_WhenNoErrorMatches_ReturnsEmptyNonCollection()
    {
        // Arrange
        var collection = ErrorCollection.From(new TestError(), new NotFoundTestError(1));

        // Act
        var filtered = collection.WithCategory(ErrorCategory.Conflict);

        // Assert
        Assert.Empty(filtered);
        Assert.IsNotType<ErrorCollection>(filtered);
    }

    [Fact]
    public void WithSeverity_WhenSomeErrorsMatch_ReturnsMatchingErrorsInOrder()
    {
        // Arrange
        var name = ValidationError.Required("Name");
        var email = ValidationError.Required("Email");
        var collection = ErrorCollection.From(name, new CriticalTestError(), email);

        // Act
        var filtered = collection.WithSeverity(ErrorSeverity.Warning);

        // Assert
        Assert.Equal(2, filtered.Count);
        Assert.Same(name, filtered[0]);
        Assert.Same(email, filtered[1]);
    }

    [Fact]
    public void WithSeverity_WhenNoErrorMatches_ReturnsEmptyNonCollection()
    {
        // Arrange
        var collection = ErrorCollection.From(new TestError(), new NotFoundTestError(1));

        // Act
        var filtered = collection.WithSeverity(ErrorSeverity.Information);

        // Assert
        Assert.Empty(filtered);
        Assert.IsNotType<ErrorCollection>(filtered);
    }

    [Fact]
    public void WithCode_WhenSomeErrorsMatch_ReturnsMatchingErrorsInOrder()
    {
        // Arrange
        var first = new TestError("first");
        var second = new TestError("second");
        var collection = ErrorCollection.From(first, new OtherError(), second);

        // Act
        var filtered = collection.WithCode(ErrorCode.From("TEST.ERROR"));

        // Assert
        Assert.Equal([first, second], filtered);
    }

    [Fact]
    public void WithCode_WhenNoErrorMatches_ReturnsEmptyNonCollection()
    {
        // Arrange
        var collection = ErrorCollection.From(new TestError(), new NotFoundTestError(1));

        // Act
        var filtered = collection.WithCode(ErrorCode.From("NO.SUCH.CODE"));

        // Assert
        Assert.Empty(filtered);
        Assert.IsNotType<ErrorCollection>(filtered);
    }

    [Fact]
    public void OfType_WhenSomeErrorsMatchConcreteType_ReturnsMatchingErrorsInOrder()
    {
        // Arrange
        var first = new NotFoundTestError(1);
        var second = new NotFoundTestError(2);
        var collection = ErrorCollection.From(new TestError(), first, new OtherError(), second);

        // Act
        var filtered = collection.OfType<NotFoundTestError>();

        // Assert
        Assert.Equal(2, filtered.Count);
        Assert.Same(first, filtered[0]);
        Assert.Same(second, filtered[1]);
    }

    [Fact]
    public void OfType_WhenFilteringByInterface_ReturnsImplementingErrors()
    {
        // Arrange
        var validation = ValidationError.Required("Name");
        var collection = ErrorCollection.From(new TestError(), validation);

        // Act
        var filtered = collection.OfType<IValidationError>();

        // Assert
        Assert.Single(filtered);
        Assert.Same(validation, filtered[0]);
    }

    [Fact]
    public void OfType_WhenNoErrorMatches_ReturnsEmptyNonCollection()
    {
        // Arrange
        var collection = ErrorCollection.From(new TestError(), new NotFoundTestError(1));

        // Act
        var filtered = collection.OfType<ValidationError>();

        // Assert
        Assert.Empty(filtered);
        Assert.IsNotType<ErrorCollection>(filtered);
    }

    [Theory]
    [InlineData(ErrorCategory.NotFound, true)]
    [InlineData(ErrorCategory.Conflict, true)]
    [InlineData(ErrorCategory.Validation, false)]
    public void HasCategory_WhenCalled_ReportsWhetherAnyErrorHasCategory(ErrorCategory category, bool expected)
    {
        // Arrange
        var collection = ErrorCollection.From(new NotFoundTestError(1), new OtherError());

        // Act
        var has = collection.HasCategory(category);

        // Assert
        Assert.Equal(expected, has);
    }

    // ── Flatten ───────────────────────────────────────────────────────────

    [Fact]
    public void Flatten_WhenCollectionsAreNested_ExpandsThemInOrder()
    {
        // Arrange
        var a = new TestError("a");
        var b = new TestError("b");
        var c = new TestError("c");
        var d = new TestError("d");
        var e = new TestError("e");
        var collection = ErrorCollection.From(
            a,
            ErrorCollection.From(b, ErrorCollection.From(c, d)),
            e);

        // Act
        var flat = collection.Flatten();

        // Assert
        Assert.Equal([a, b, c, d, e], flat);
        Assert.DoesNotContain(flat, error => error is ErrorCollection);
    }

    [Fact]
    public void Flatten_WhenNoNestedCollection_KeepsErrorsInOrder()
    {
        // Arrange
        var first = new TestError("first");
        var second = new OtherError();
        var collection = ErrorCollection.From(first, second);

        // Act
        var flat = collection.Flatten();

        // Assert
        Assert.Equal([first, second], flat);
    }

    // ── Grouping ──────────────────────────────────────────────────────────

    [Fact]
    public void GroupByCode_WhenErrorsShareCodes_GroupsThemInOrder()
    {
        // Arrange
        var first = new TestError("first");
        var other = new OtherError();
        var second = new TestError("second");
        var collection = ErrorCollection.From(first, other, second);

        // Act
        var groups = collection.GroupByCode();

        // Assert
        Assert.Equal(2, groups.Count);
        Assert.Equal([first, second], groups[ErrorCode.From("TEST.ERROR")]);
        Assert.Equal([other], groups[ErrorCode.From("TEST.OTHER")]);
    }

    [Fact]
    public void GroupByCategory_WhenErrorsShareCategories_GroupsThemInOrder()
    {
        // Arrange
        var first = new NotFoundTestError(1);
        var technical = new TestError();
        var second = new NotFoundTestError(2);
        var collection = ErrorCollection.From(first, technical, second);

        // Act
        var groups = collection.GroupByCategory();

        // Assert
        Assert.Equal(2, groups.Count);
        Assert.Equal([first, second], groups[ErrorCategory.NotFound]);
        Assert.Equal([technical], groups[ErrorCategory.Technical]);
    }

    // ── Enumeration ───────────────────────────────────────────────────────

    [Fact]
    public void GetEnumerator_WhenIteratedWithForeach_YieldsErrorsInOrder()
    {
        // Arrange
        var first = new TestError("first");
        var second = new OtherError();
        var collection = ErrorCollection.From(first, second);
        var seen = new List<IError>();

        // Act
        foreach (var error in collection)
            seen.Add(error);

        // Assert
        Assert.Equal([first, second], seen);
    }

    [Fact]
    public void GetEnumerator_WhenIteratedAsGenericEnumerable_YieldsErrorsInOrder()
    {
        // Arrange
        var first = new TestError("first");
        var second = new OtherError();
        IEnumerable<IError> collection = ErrorCollection.From(first, second);

        // Act
        var seen = collection.ToList();

        // Assert
        Assert.Equal([first, second], seen);
    }

    [Fact]
    public void GetEnumerator_WhenIteratedAsNonGenericEnumerable_YieldsErrorsInOrder()
    {
        // Arrange
        var first = new TestError("first");
        var second = new OtherError();
        IEnumerable collection = ErrorCollection.From(first, second);
        var seen = new List<object?>();

        // Act
        foreach (var item in collection)
            seen.Add(item);

        // Assert
        Assert.Equal(2, seen.Count);
        Assert.Same(first, seen[0]);
        Assert.Same(second, seen[1]);
    }

    // ── Equality ──────────────────────────────────────────────────────────

    [Fact]
    public void Equals_WhenTwoCollectionsHoldSameErrors_ReturnsFalse()
    {
        // Arrange
        var error = new TestError();
        var first = ErrorCollection.From(error);
        var second = ErrorCollection.From(error);

        // Act
        var equal = first.Equals(second);

        // Assert
        Assert.False(equal);
        Assert.False(ReferenceEquals(first, second));
    }

    [Fact]
    public void Equals_WhenSameInstance_ReturnsTrue()
    {
        // Arrange
        var collection = ErrorCollection.From(new TestError());
        var same = collection;

        // Act
        var equal = collection.Equals(same);

        // Assert
        Assert.True(equal);
    }

    private sealed record ExternalTestError()
        : Error(ErrorCode.From("TEST.EXTERNAL"), "External error")
    {
        public override ErrorCategory Category => ErrorCategory.External;
    }

    [Fact]
    public void Message_WhenAnErrorHasANullMessage_JoinsItAsEmpty()
    {
        // Arrange
        var collection = ErrorCollection.From(new NullMessageError(), new TestSupport.OtherError());

        // Act
        var message = collection.Message;

        // Assert: as string.Join did before Phase 7
        Assert.Equal("; Other error", message);
    }

    private sealed class NullMessageError : IError
    {
        public ErrorCode Code => ErrorCode.From("TEST.NULL");

        public string Message => null!;

        public ErrorCategory Category => ErrorCategory.Technical;

        public ErrorSeverity Severity => ErrorSeverity.Error;

        public IReadOnlyDictionary<string, object?> Metadata => ErrorMetadata.Empty;
    }

    [Fact]
    public void From_WhenMessageIsComputed_ReadsItOnce()
    {
        // Arrange
        var error = new CountingMessageError();

        // Act
        var collection = ErrorCollection.From(error, new TestSupport.OtherError());

        // Assert: a getter whose value changes between reads cannot break the message
        Assert.Equal(1, error.Reads);
        Assert.Equal("read 1; Other error", collection.Message);
    }

    private sealed class CountingMessageError : IError
    {
        public int Reads { get; private set; }

        public ErrorCode Code => ErrorCode.From("TEST.COUNTING");

        public string Message => $"read {++Reads}";

        public ErrorCategory Category => ErrorCategory.Technical;

        public ErrorSeverity Severity => ErrorSeverity.Error;

        public IReadOnlyDictionary<string, object?> Metadata => ErrorMetadata.Empty;
    }

    [Theory]
    [InlineData(ErrorCategory.Validation, ErrorCategory.Unauthorized, ErrorCategory.Unauthorized)]
    [InlineData(ErrorCategory.Validation, ErrorCategory.NotFound, ErrorCategory.NotFound)]
    [InlineData(ErrorCategory.Cancelled, ErrorCategory.NotFound, ErrorCategory.NotFound)]
    [InlineData(ErrorCategory.BusinessRule, ErrorCategory.Validation, ErrorCategory.Validation)]
    [InlineData(ErrorCategory.Forbidden, ErrorCategory.Unauthorized, ErrorCategory.Unauthorized)]
    [InlineData(ErrorCategory.Timeout, ErrorCategory.Technical, ErrorCategory.Technical)]
    [InlineData((ErrorCategory)998, (ErrorCategory)999, (ErrorCategory)998)]
    [InlineData((ErrorCategory)998, ErrorCategory.NotSupported, ErrorCategory.NotSupported)]
    public void Category_WhenErrorsDisagree_IsTheHighestPrecedenceInEitherOrder(
        ErrorCategory first, ErrorCategory second, ErrorCategory expected)
    {
        // Act
        var forward = ErrorCollection.From(new PrecedenceError(first), new PrecedenceError(second));
        var backward = ErrorCollection.From(new PrecedenceError(second), new PrecedenceError(first));

        // Assert
        Assert.Equal(expected, forward.Category);
        Assert.Equal(expected, backward.Category);
    }

    private sealed record PrecedenceError(ErrorCategory Kind) : Error(ErrorCode.From("TEST.PRECEDENCE"), "Precedence test")
    {
        public override ErrorCategory Category => Kind;
    }

    [Fact]
    public void From_WhenAnElementIsNull_ThrowsArgumentExceptionNamingErrors()
    {
        // Arrange
        IError valid = new TestSupport.OtherError();

        // Act
        var fromArray = Assert.Throws<ArgumentException>(() => ErrorCollection.From(valid, null!));
        var fromSequence = Assert.Throws<ArgumentException>(() => ErrorCollection.From(new List<IError> { valid, null! }));

        // Assert: not a NullReferenceException from the constructor
        Assert.IsNotType<ArgumentNullException>(fromArray);
        Assert.Equal("errors", fromArray.ParamName);
        Assert.Equal("errors", fromSequence.ParamName);
    }
}
