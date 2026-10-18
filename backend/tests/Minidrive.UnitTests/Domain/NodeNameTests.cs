using Minidrive.Domain.Exceptions;
using Minidrive.Domain.ValueObjects;
using Xunit;

namespace Minidrive.UnitTests.Domain;

public sealed class NodeNameTests
{
    [Theory]
    [InlineData("report.pdf")]
    [InlineData("2026 budget.xlsx")]
    [InlineData("a")]
    public void Validate_AcceptsReasonableNames(string name)
    {
        Assert.Equal(name, NodeName.Validate(name));
    }

    [Fact]
    public void Validate_TrimsSurroundingWhitespace()
    {
        Assert.Equal("holiday", NodeName.Validate("  holiday  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsMissingNames(string? name)
    {
        Assert.Throws<InvalidNodeNameException>(() => NodeName.Validate(name));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void Validate_RejectsForbiddenValues(string name)
    {
        Assert.Throws<InvalidNodeNameException>(() => NodeName.Validate(name));
    }

    [Fact]
    public void Validate_RejectsControlCharacters()
    {
        Assert.Throws<InvalidNodeNameException>(() => NodeName.Validate("bad\u0007name"));
    }

    [Fact]
    public void Validate_RejectsOverlongNames()
    {
        var name = new string('x', NodeName.MaxLength + 1);

        Assert.Throws<InvalidNodeNameException>(() => NodeName.Validate(name));
    }

    [Fact]
    public void Validate_AcceptsNameAtMaximumLength()
    {
        var name = new string('x', NodeName.MaxLength);

        Assert.Equal(name, NodeName.Validate(name));
    }
}
