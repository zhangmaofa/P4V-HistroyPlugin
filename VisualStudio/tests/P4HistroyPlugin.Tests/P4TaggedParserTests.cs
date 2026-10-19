using P4HistroyPlugin.Core;
using Xunit;

namespace P4HistroyPlugin.Tests;

public sealed class P4TaggedParserTests
{
    [Fact]
    public void ParsesTaggedAnnotateIncludingEmptySourceLine()
    {
        const string output = """
... depotFile //depot/file.cs
... rev 2

... upper 20
... lower 10
... user alice
... time 2026/10/01 12:34:56
... client alice-main
... data first line

... upper 20
... lower 20
... user bob
... time 2026/10/02 01:02:03
... client bob-main
... data 

""";

        var result = P4TaggedParser.ParseAnnotations(output);

        Assert.Equal(2, result.Count);
        Assert.Equal(10, result[0].Change);
        Assert.Equal("alice", result[0].User);
        Assert.Equal("first line", result[0].Text);
        Assert.Equal(20, result[1].Change);
        Assert.Equal(string.Empty, result[1].Text);
    }

    [Theory]
    [InlineData("P4PORT=ssl:perforce.example.com:1666", "ssl:perforce.example.com:1666")]
    [InlineData("P4PORT=perforce.example.com:1666 (set)", "perforce.example.com:1666")]
    public void ParsesEffectiveP4Setting(string output, string expected)
    {
        Assert.Equal(expected, P4TaggedParser.ParseSettingValue(output, "P4PORT"));
    }
}
