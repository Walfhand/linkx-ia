using LinkxAi.Api.Modules.Turns.Domain;
using Xunit;

namespace LinkxAi.Tests.Unit;

public sealed class TurnTests
{
    [Fact]
    public void Create_ShouldKeepEmptyRecord_WhenGameHasNoMoves()
    {
        var turn = Turn.Create("game-1", "blue", "", 6000);

        Assert.Equal("game-1", turn.Game);
        Assert.Equal(PlayerColor.Blue, turn.Color);
        Assert.Equal("", turn.Record);
        Assert.Equal(6000, turn.DeadlineMs);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("")]
    public void Create_ShouldRejectUnknownColor_WhenColorIsInvalid(string color)
    {
        Assert.Throws<ArgumentException>(() => Turn.Create("game-1", color, "", 6000));
    }

    [Fact]
    public void Create_ShouldRejectNonPositiveDeadline_WhenDeadlineIsInvalid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Turn.Create("game-1", "white", "", 0));
    }

    [Fact]
    public void Create_ShouldRejectBlankGame_WhenGameIsMissing()
    {
        Assert.Throws<ArgumentException>(() => Turn.Create(" ", "white", "", 6000));
    }

    [Fact]
    public void Create_ShouldRejectMissingRecord_WhenRecordIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => Turn.Create("game-1", "white", null!, 6000));
    }
}
