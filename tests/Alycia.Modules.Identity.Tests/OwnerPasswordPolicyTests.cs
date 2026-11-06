using Alycia.Modules.Identity.Application;

namespace Alycia.Modules.Identity.Tests;

public sealed class OwnerPasswordPolicyTests
{
    private static OwnerPasswordPolicy CreatePolicy() =>
        new(["password1234567", "CAFE\u0301CAFE\u0301CAFE\u0301"]);

    [Theory]
    [InlineData(14)]
    [InlineData(129)]
    public void NormalizeAndValidate_RejectsOutOfRangeCodePointCount(int count)
    {
        Assert.Throws<ArgumentException>(() =>
            CreatePolicy().NormalizeAndValidate(new string('x', count)));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(128)]
    public void NormalizeAndValidate_AcceptsLengthBoundariesWithoutComposition(int count)
    {
        var password = new string('x', count);
        Assert.Equal(password, CreatePolicy().NormalizeAndValidate(password));
    }

    [Fact]
    public void NormalizeAndValidate_CountsUnicodeScalarsAfterNfc()
    {
        var input = new string('a', 14) + "e\u0301";
        Assert.Equal(new string('a', 14) + "é", CreatePolicy().NormalizeAndValidate(input));
    }

    [Fact]
    public void NormalizeAndValidate_PreservesSpacesAndPrintableUnicode()
    {
        var input = " 🔒 " + new string('x', 15) + " ";
        Assert.Equal(input, CreatePolicy().NormalizeAndValidate(input));
    }

    [Fact]
    public void NormalizeAndValidate_RejectsBlockedFullPasswordWithoutTrimming()
    {
        Assert.Throws<ArgumentException>(() =>
            CreatePolicy().NormalizeAndValidate("PASSWORD1234567"));
        Assert.Equal(" password1234567 ",
            CreatePolicy().NormalizeAndValidate(" password1234567 "));
    }

    [Fact]
    public void NormalizeAndValidate_RejectsControlAndMalformedUtf16()
    {
        Assert.Throws<ArgumentException>(() =>
            CreatePolicy().NormalizeAndValidate(new string('x', 15) + "\n"));
        Assert.Throws<ArgumentException>(() =>
            CreatePolicy().NormalizeAndValidate(new string('x', 15) + "\uD800"));
    }
}
