using System.Buffers.Binary;
using System.Buffers.Text;

namespace MicroKit.Persistence.Tests.Query;

/// <summary>
/// #197 phase 5: the keyset cursor is an opaque Base64Url token of provider values and a fingerprint of the
/// ordering. The token is untrusted input: a malformed one, an oversized one, or one that declares more bytes
/// than it carries, is refused before anything is allocated for it.
/// </summary>
public sealed class KeysetCursorTests
{
    private static readonly (string Name, bool Descending)[] Ordering = [("PlacedAt", true), ("Id", false)];

    public static TheoryData<object> EveryProviderType() =>
    [
        true,
        (byte)200,
        (sbyte)-5,
        (short)-1234,
        (ushort)65000,
        -123456789,
        4000000000u,
        long.MinValue,
        ulong.MaxValue,
        1.5f,
        -2.25d,
        123456.789m,
        Guid.Parse("0a1b2c3d-4e5f-6071-8293-a4b5c6d7e8f9"),
        "Zoë — 東京",
        string.Empty,
        new DateTime(2026, 11, 9, 12, 30, 0, DateTimeKind.Utc),
        new DateTime(2026, 11, 9, 12, 30, 0, DateTimeKind.Unspecified),
        new DateTimeOffset(2026, 11, 9, 12, 30, 0, TimeSpan.FromHours(-5)),
        new DateOnly(2026, 11, 9),
        new TimeOnly(23, 59, 59, 999),
        new byte[] { 0, 1, 254, 255 },
    ];

    [Theory]
    [MemberData(nameof(EveryProviderType))]
    public void AValueOfEveryProviderType_RoundTripsThroughTheToken(object value)
    {
        var cursor = KeysetCursor.Create("Shop.Order", [("Key", false)], [value]);

        Assert.True(KeysetCursor.TryParse(cursor.ToString(), out var parsed));

        var read = Assert.Single(parsed.Values);
        Assert.Equal(value.GetType(), read.GetType());
        if (value is byte[] bytes)
            Assert.Equal(bytes, ((byte[])read));
        else
            Assert.Equal(value, read);
        if (value is DateTime instant)
            Assert.Equal(instant.Kind, ((DateTime)read).Kind);
        if (value is DateTimeOffset withOffset)
            Assert.Equal(withOffset.Offset, ((DateTimeOffset)read).Offset);
    }

    [Fact]
    public void TheToken_IsBase64Url_AndCarriesSeveralValuesInOrder()
    {
        var at = new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);
        var id = Guid.NewGuid();

        var token = KeysetCursor.Create("Shop.Order", Ordering, [at, id]).ToString();

        Assert.DoesNotContain("+", token, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/", token, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("=", token, StringComparison.OrdinalIgnoreCase);
        Assert.True(KeysetCursor.TryParse(token, out var parsed));
        Assert.Equal([at, id], parsed.Values);
    }

    [Fact]
    public void TheFingerprint_CoversTheRootTypeTheKeyNamesAndTheirDirections()
    {
        var cursor = KeysetCursor.Create("Shop.Order", Ordering, [DateTimeOffset.UnixEpoch, Guid.Empty]);

        Assert.True(cursor.IsFor("Shop.Order", Ordering));
        Assert.False(cursor.IsFor("Shop.Invoice", Ordering), "same keys and types, another aggregate");
        Assert.False(cursor.IsFor("Shop.Order", [("PlacedAt", false), ("Id", false)]), "another direction");
        Assert.False(cursor.IsFor("Shop.Order", [("Number", true), ("Id", false)]), "another key");
        Assert.False(cursor.IsFor("Shop.Order", [("Id", false), ("PlacedAt", true)]), "another order of the keys");
        Assert.False(cursor.IsFor("Shop.Order", [("PlacedAt", true)]), "fewer keys");
        Assert.False(cursor.IsFor("Shop.OrderPlacedAt", [("", true), ("Id", false)]), "names are length-prefixed");
    }

    public enum Malformed
    {
        Null,
        Empty,
        NotBase64Url,
        Padded,
        WrongVersion,
        Truncated,
        TrailingByte,
        NoValue,
        UnknownTag,
        BooleanOutOfRange,
        InvalidUtf8,
        OffsetOutOfRange,
        DayOutOfRange,
    }

    [Theory]
    [InlineData(Malformed.Null)]
    [InlineData(Malformed.Empty)]
    [InlineData(Malformed.NotBase64Url)]
    [InlineData(Malformed.Padded)]
    [InlineData(Malformed.WrongVersion)]
    [InlineData(Malformed.Truncated)]
    [InlineData(Malformed.TrailingByte)]
    [InlineData(Malformed.NoValue)]
    [InlineData(Malformed.UnknownTag)]
    [InlineData(Malformed.BooleanOutOfRange)]
    [InlineData(Malformed.InvalidUtf8)]
    [InlineData(Malformed.OffsetOutOfRange)]
    [InlineData(Malformed.DayOutOfRange)]
    public void AMalformedToken_IsRefused(Malformed malformed)
    {
        var valid = Decode(KeysetCursor.Create("Shop.Order", [("Key", false)], [42]).ToString());
        var token = malformed switch
        {
            Malformed.Null => null,
            Malformed.Empty => string.Empty,
            Malformed.NotBase64Url => "not a token!",
            Malformed.Padded => KeysetCursor.Create("Shop.Order", [("Key", false)], [42]).ToString() + "==",
            Malformed.WrongVersion => Encode([2, .. valid[1..]]),
            Malformed.Truncated => Encode(valid[..^1]),
            Malformed.TrailingByte => Encode([.. valid, 0]),
            Malformed.NoValue => Encode([.. Header(count: 0)]),
            Malformed.UnknownTag => Encode([.. Header(count: 1), 99, 0]),
            Malformed.BooleanOutOfRange => Encode([.. Header(count: 1), 1, 2]),
            Malformed.InvalidUtf8 => Encode([.. Header(count: 1), 14, .. Int32(2), 0xC3, 0x28]),
            Malformed.OffsetOutOfRange => Encode([.. Header(count: 1), 16, .. Int64(0), .. Int16(15 * 60)]),
            _ => Encode([.. Header(count: 1), 17, .. Int32(int.MaxValue)]),
        };

        Assert.False(KeysetCursor.TryParse(token, out var cursor));
        Assert.Null(cursor);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void ATokenThatDecodesButIsNotSpelledCanonically_IsRefused(string whitespace)
    {
        // The Base64Url decoder skips whitespace: this spelling decodes to the same bytes.
        var token = KeysetCursor.Create("Shop.Order", [("Key", false)], [42]).ToString();
        var alias = token.Insert(5, whitespace);
        Assert.True(Base64Url.IsValid(alias));
        Assert.Equal(Decode(token), Decode(alias));

        Assert.False(KeysetCursor.TryParse(alias, out _), "one spelling per cursor");
    }

    [Theory]
    [InlineData(14)] // string
    [InlineData(19)] // byte[]
    public void AForgedTokenDeclaringAHugeLength_IsRefused_WithoutAllocatingIt(byte tag)
    {
        var token = Encode([.. Header(count: 1), tag, .. Int32(int.MaxValue), 1, 2, 3]);
        Assert.True(KeysetCursor.TryParse(KeysetCursor.Create("Shop.Order", [("Key", false)], ["warm"]).ToString(), out _));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var parsed = KeysetCursor.TryParse(token, out _);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.False(parsed);
        Assert.True(allocated < (64 * 1024), "the declared length is checked against the bytes that remain before any allocation");
    }

    [Fact]
    public void AForgedTokenDeclaringMoreValuesThanItCarries_IsRefused()
    {
        var token = Encode([.. Header(count: 255), 6, .. Int32(1)]);

        Assert.False(KeysetCursor.TryParse(token, out _));
    }

    [Fact]
    public void ATokenAboveTheMaximumLength_IsRefused()
    {
        var longest = KeysetCursor.Create("Shop.Order", [("Key", false)], [new string('x', 700)]).ToString();
        Assert.True(longest.Length <= KeysetCursor.MaxTokenLength);
        Assert.True(KeysetCursor.TryParse(longest, out _));

        // Well formed in every other respect: only its length refuses it.
        var oversized = Encode([.. Header(count: 1), 14, .. Int32(800), .. Enumerable.Repeat((byte)'x', 800)]);
        Assert.True(oversized.Length > KeysetCursor.MaxTokenLength);
        Assert.False(KeysetCursor.TryParse(oversized, out _));
    }

    [Fact]
    public void Create_RefusesAValueOfAnotherType_AndValuesTooLongForAToken()
    {
        Assert.Contains("TimeSpan", Assert.Throws<ArgumentException>(() => KeysetCursor.Create("Shop.Order", [("Key", false)], [TimeSpan.Zero]))
            .Message);
        Assert.Contains(nameof(KeysetCursor.MaxTokenLength), Assert.Throws<InvalidOperationException>(() => KeysetCursor.Create("Shop.Order", [("Key", false)], [new string('x', 2000)]))
            .Message);
    }

    [Theory]
    [InlineData(typeof(int), true)]
    [InlineData(typeof(Guid), true)]
    [InlineData(typeof(byte[]), true)]
    [InlineData(typeof(TimeSpan), false)]
    [InlineData(typeof(char), false)]
    [InlineData(typeof(int?), false)]
    public void IsSupported_IsTheClosedSetOfProviderTypes(Type type, bool supported) =>
        Assert.Equal(supported, KeysetCursor.IsSupported(type));

    // ---- Helpers ------------------------------------------------------------------------------

    private static byte[] Header(byte count) => [1, 0, 0, 0, 0, 0, 0, 0, 0, count];

    private static byte[] Int16(short value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] Int32(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] Int64(long value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        return bytes;
    }

    private static string Encode(byte[] bytes) => Base64Url.EncodeToString(bytes);

    private static byte[] Decode(string token) => Base64Url.DecodeFromChars(token);

    // ---- Worst-case size (PR #200 review, #197 Spec phase 5) -----------------------------------------

    public static TheoryData<object> FixedSizeValues() => new()
    {
        true, byte.MaxValue, sbyte.MinValue, short.MinValue, ushort.MaxValue, int.MinValue, uint.MaxValue, long.MinValue,
        ulong.MaxValue, float.MinValue, double.MaxValue, decimal.MinValue, Guid.NewGuid(), DateTime.MaxValue,
        DateTimeOffset.MaxValue, DateOnly.MaxValue, TimeOnly.MaxValue,
    };

    [Theory]
    [MemberData(nameof(FixedSizeValues))]
    public void MaxEncodedLength_OfAFixedSizeType_IsExactlyWhatItsCursorTakes(object value)
    {
        var size = KeysetCursor.MaxEncodedLength(value.GetType(), maxLength: null);

        Assert.NotNull(size);
        Assert.Equal(KeysetCursor.Create("Shop.Order", [("Key", false)], [value]).ToString().Length, KeysetCursor.TokenLengthFor(size.Value));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("é")]
    [InlineData("€")]
    [InlineData("😀")]
    public void MaxEncodedLength_OfABoundedString_CoversItsWorstUtf8Encoding(string unit)
    {
        const int maxLength = 60;
        var longest = string.Concat(Enumerable.Repeat(unit, maxLength / unit.Length));
        var size = KeysetCursor.MaxEncodedLength(typeof(string), maxLength);

        Assert.NotNull(size);
        Assert.True(((long)KeysetCursor.Create("Shop.Order", [("Key", false)], [longest]).ToString().Length) <= KeysetCursor.TokenLengthFor(size.Value));
    }

    [Fact]
    public void MaxEncodedLength_OfBoundedBytes_IsExactlyWhatItsCursorTakes()
    {
        var size = KeysetCursor.MaxEncodedLength(typeof(byte[]), 100);

        Assert.NotNull(size);
        Assert.Equal(KeysetCursor.Create("Shop.Order", [("Key", false)], [new byte[100]]).ToString().Length, KeysetCursor.TokenLengthFor(size.Value));
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(byte[]))]
    public void MaxEncodedLength_WithoutAMaximumLength_HasNoBound(Type type) =>
        Assert.Null(KeysetCursor.MaxEncodedLength(type, maxLength: null));

    [Fact]
    public void TokenLengthFor_SeveralValues_IsTheirSum_UnderOneHeader()
    {
        var sizes = new[] { typeof(int), typeof(Guid) }.Sum(t => KeysetCursor.MaxEncodedLength(t, null)!.Value);

        Assert.Equal(KeysetCursor.Create("Shop.Order", Ordering, [42, Guid.NewGuid()]).ToString().Length, KeysetCursor.TokenLengthFor(sizes));
    }
}
