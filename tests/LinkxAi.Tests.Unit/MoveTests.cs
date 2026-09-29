using LinkxAi.Api.Modules.Turns.Domain;
using Xunit;

namespace LinkxAi.Tests.Unit;

public sealed class MoveTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    [InlineData("3I", 2)]
    [InlineData("3L", 4)]
    [InlineData("4S", 4)]
    [InlineData("4T", 4)]
    [InlineData("4L", 8)]
    public void Transformations_HaveTheSpecifiedNumberOfDistinctOrientations(string shape, int expected)
    {
        var tokens = new List<string>();
        foreach (var mirror in new[] { "", "s" })
        foreach (var rotation in new[] { "", "r1", "r2", "r3" })
            tokens.Add(Move.Parse($"{shape}{mirror}{rotation}1").ToString());

        Assert.Equal(expected, tokens.Distinct().Count());
    }

    [Theory]
    [InlineData("2r23", "23")]
    [InlineData("1r27", "17")]
    [InlineData("2l13", "2r13")]
    [InlineData("4lsl15", "4Lsr35")]
    [InlineData("3Ls4", "3Lr14")]
    [InlineData("4Ls5", "4Ls5")]
    [InlineData("4Ssr21", "4Ss1")]
    [InlineData("4lsr23", "4Lsr23")]
    public void Parse_NormalizesRotationsMirrorsAndCase(string token, string canonical)
    {
        Assert.Equal(canonical, Move.Parse(token).ToString());
    }

    [Theory]
    [InlineData("1")]
    [InlineData("3I0")]
    [InlineData("5x")]
    [InlineData("4Z3")]
    [InlineData("2r43")]
    [InlineData("3I")]
    [InlineData("")]
    [InlineData("--")]
    [InlineData("15 16")]
    [InlineData("15\n")]
    public void Parse_RejectsMalformedMove(string token)
    {
        Assert.Equal("syntax", Assert.Throws<GameRuleException>(() => Move.Parse(token)).Reason);
    }

    [Fact]
    public void Parse_UsesTheFinalDigitAsOneBasedAnchorColumn()
    {
        Assert.Equal(new Move(Shape.Bar3, 0, false, 6), Move.Parse("3I7"));
    }
}
