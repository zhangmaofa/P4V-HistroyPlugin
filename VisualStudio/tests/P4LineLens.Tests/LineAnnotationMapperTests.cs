using System;
using P4LineLens.Core;
using Xunit;

namespace P4LineLens.Tests;

public sealed class LineAnnotationMapperTests
{
    [Fact]
    public void MarksInsertedLineLocalWithoutShiftingFollowingOwners()
    {
        var baseline = new[]
        {
            Line(1, "alice", "alpha"),
            Line(2, "bob", "beta"),
            Line(3, "carol", "gamma"),
        };

        var mapped = LineAnnotationMapper.Map(baseline, new[] { "alpha", "inserted", "beta", "gamma" });

        Assert.Equal("alice", mapped[0].User);
        Assert.True(mapped[1].IsLocal);
        Assert.Equal("bob", mapped[2].User);
        Assert.Equal("carol", mapped[3].User);
    }

    [Fact]
    public void MarksChangedLineLocalAndPreservesSurroundingOwners()
    {
        var baseline = new[]
        {
            Line(1, "alice", "alpha"),
            Line(2, "bob", "beta"),
            Line(3, "carol", "gamma"),
        };

        var mapped = LineAnnotationMapper.Map(baseline, new[] { "alpha", "changed", "gamma" });

        Assert.Equal("alice", mapped[0].User);
        Assert.True(mapped[1].IsLocal);
        Assert.Equal("carol", mapped[2].User);
    }

    [Fact]
    public void AlignsUniqueAnchorsAcrossDeletion()
    {
        var baseline = new[]
        {
            Line(1, "a", "{"),
            Line(2, "b", "first"),
            Line(3, "c", "deleted"),
            Line(4, "d", "second"),
            Line(5, "e", "}"),
        };

        var mapped = LineAnnotationMapper.Map(baseline, new[] { "{", "first", "second", "}" });

        Assert.Equal(new[] { "a", "b", "d", "e" }, Array.ConvertAll(mapped, item => item.User));
        Assert.DoesNotContain(mapped, item => item.IsLocal);
    }

    private static AnnotationLine Line(int change, string user, string text)
    {
        return new AnnotationLine(change, user, null, "workspace", text);
    }
}
