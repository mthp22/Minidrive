using Minidrive.Domain.Exceptions;
using Minidrive.Domain.ValueObjects;
using Xunit;

namespace Minidrive.UnitTests.Domain;

public sealed class MaterializedPathTests
{
    [Fact]
    public void ForChild_WhenParentIsRoot_UsesRootLevelPath()
    {
        var path = MaterializedPath.ForChild(parentPath: null, parentId: null);

        Assert.Equal(",", path);
    }

    [Fact]
    public void ForChild_WhenParentExists_AppendsParentId()
    {
        var path = MaterializedPath.ForChild(",", "abc");

        Assert.Equal(",abc,", path);
    }

    [Fact]
    public void ForChild_WhenParentPathMissing_Throws()
    {
        Assert.Throws<DomainException>(() => MaterializedPath.ForChild(parentPath: null, parentId: "abc"));
    }

    [Fact]
    public void DescendantPrefix_CombinesOwnPathAndId()
    {
        var prefix = MaterializedPath.DescendantPrefix(",parent,", "self");

        Assert.Equal(",parent,self,", prefix);
    }

    [Theory]
    [InlineData(",a,", ",a,", true)]           // direct child
    [InlineData(",a,b,", ",a,", true)]         // deeper descendant
    [InlineData(",ab,", ",a,", false)]         // must not match a partial identifier
    [InlineData(",x,", ",a,", false)]          // sibling
    public void IsWithin_DoesNotMatchPartialIdentifiers(string candidate, string prefix, bool expected)
    {
        Assert.Equal(expected, MaterializedPath.IsWithin(candidate, prefix));
    }

    [Fact]
    public void Rebase_ReplacesPrefixAndKeepsSuffix()
    {
        var rebased = MaterializedPath.Rebase(",a,b,c,", ",a,", ",x,");

        Assert.Equal(",x,b,c,", rebased);
    }

    [Fact]
    public void Rebase_WhenPrefixDoesNotMatch_Throws()
    {
        Assert.Throws<DomainException>(() => MaterializedPath.Rebase(",a,b,", ",z,", ",x,"));
    }

    [Fact]
    public void ToAnchoredRegexPattern_EscapesAndAnchors()
    {
        var pattern = MaterializedPath.ToAnchoredRegexPattern(",a,");

        Assert.Equal(@"^\,a\,", pattern);
    }
}
