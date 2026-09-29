using LinkxAi.Api.Modules.Turns.Domain;
using Xunit;

namespace LinkxAi.Tests.Unit;

public sealed class GamePositionTests
{
    [Fact]
    public void Create_HasTwoCopiesPerShapeAnd95LegalMoves()
    {
        var position = GamePosition.Create();

        Assert.Equal(PlayerColor.Blue, position.ActivePlayer);
        foreach (var color in Enum.GetValues<PlayerColor>())
        {
            Assert.Equal(95, position.GetLegalMoves(color).Count);
            foreach (var shape in Enum.GetValues<Shape>()) Assert.Equal(2, position.Remaining(color, shape));
        }
        Assert.Equal(95, position.GetLegalMoves().Distinct().Count());
        Assert.All(Enumerable.Range(0, 81), index => Assert.Null(position.GetCell(index % 9, index / 9)));
    }

    [Fact]
    public void Play_DropsOntoTheFirstObstacleAndConsumesOnlyTheMovingPlayersPiece()
    {
        var initial = GamePosition.Create(PlayerColor.White);
        var first = initial.Play(Move.Parse("15"));
        var second = first.Play(Move.Parse("2r15"));

        Assert.Equal(PlayerColor.White, second.GetCell(4, 8));
        Assert.Equal(PlayerColor.Blue, second.GetCell(4, 7));
        Assert.Equal(PlayerColor.Blue, second.GetCell(4, 6));
        Assert.Null(second.GetCell(4, 5));
        Assert.Equal(1, second.Remaining(PlayerColor.White, Shape.Mono));
        Assert.Equal(1, second.Remaining(PlayerColor.Blue, Shape.Domino));
        Assert.Equal(2, second.Remaining(PlayerColor.Blue, Shape.Mono));
        Assert.Equal(PlayerColor.White, second.ActivePlayer);
        Assert.Null(initial.GetCell(4, 8));
        Assert.Null(first.GetCell(4, 7));
    }

    [Fact]
    public void Play_RequiresSupportUnderBothArmsOfTheT()
    {
        Assert.Equal("unsupported", Assert.Throws<GameRuleException>(() => GamePosition.Create().Play(Move.Parse("4T1"))).Reason);
        var upsideDown = GamePosition.Create().Play(Move.Parse("4Tr21"));
        Assert.Equal(PlayerColor.Blue, upsideDown.GetCell(0, 8));
        Assert.Equal(PlayerColor.Blue, upsideDown.GetCell(2, 8));
        Assert.Equal(PlayerColor.Blue, upsideDown.GetCell(1, 7));

        var supported = Play("11", "13").Play(Move.Parse("4T1"));
        Assert.Equal(PlayerColor.Blue, supported.GetCell(1, 8));
        Assert.Equal(PlayerColor.Blue, supported.GetCell(0, 7));
        Assert.Equal(PlayerColor.Blue, supported.GetCell(2, 7));
    }

    [Fact]
    public void Play_MirrorsBeforeRotating()
    {
        var position = GamePosition.Create().Play(Move.Parse("4Lsr11"));

        Assert.Equal(PlayerColor.Blue, position.GetCell(1, 6));
        Assert.Equal(PlayerColor.Blue, position.GetCell(1, 7));
        Assert.Equal(PlayerColor.Blue, position.GetCell(0, 8));
        Assert.Equal(PlayerColor.Blue, position.GetCell(1, 8));
        Assert.Null(position.GetCell(0, 6));
    }

    [Theory]
    [InlineData("", "3I9", "horizontal-bounds")]
    [InlineData("3Ir11 3Ir11 3Ir11", "2r11", "overflow")]
    [InlineData("11 13", "3I1", "unsupported")]
    [InlineData("11 12 13 14", "15", "exhausted")]
    public void Play_RejectsIllegalMovesWithoutChangingThePosition(string prefix, string token, string reason)
    {
        var before = Play(prefix.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var board = Enumerable.Range(0, 81).Select(index => before.GetCell(index % 9, index / 9)).ToArray();
        var inventory = Enum.GetValues<Shape>().Select(shape => before.Remaining(before.ActivePlayer, shape)).ToArray();
        var active = before.ActivePlayer;

        Assert.Equal(reason, Assert.Throws<GameRuleException>(() => before.Play(Move.Parse(token))).Reason);

        Assert.Equal(board, Enumerable.Range(0, 81).Select(index => before.GetCell(index % 9, index / 9)));
        Assert.Equal(inventory, Enum.GetValues<Shape>().Select(shape => before.Remaining(active, shape)));
        Assert.Equal(active, before.ActivePlayer);
    }

    [Fact]
    public void GetLegalMoves_ExcludesExhaustedShapes()
    {
        var position = Play("11", "12", "13", "14");

        Assert.Equal(0, position.Remaining(PlayerColor.Blue, Shape.Mono));
        Assert.DoesNotContain(position.GetLegalMoves(), move => move.Shape == Shape.Mono);
    }

    private static GamePosition Play(params string[] tokens)
    {
        var position = GamePosition.Create();
        foreach (var token in tokens) position = position.Play(Move.Parse(token));
        return position;
    }
}
