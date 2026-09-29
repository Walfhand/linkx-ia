using LinkxAi.Api.Modules.Turns.Domain;
using Xunit;

namespace LinkxAi.Tests.Unit;

public sealed class MoveSearchTests
{
    [Fact]
    public void Find_ShouldUseInjectedEvaluation_WithoutAllowingItToOverrideTerminalResults()
    {
        var position = GamePosition.Create();
        var decision = MoveSearch.Find(position, maxDepth: 1,
            evaluate: (state, player) => state.GetCell(8, 8) == player ? 100 : -100);
        Assert.Equal(PlayerColor.Blue, position.Play(decision.Move).GetCell(8, 8));

        var winning = GamePosition.Replay(WinningPosition);
        var win = MoveSearch.Find(winning, maxDepth: 1, evaluate: (_, _) => -10_000);
        Assert.True(win.Exact);
        Assert.Equal(PlayerColor.Blue, winning.Play(win.Move).Result?.Winner);
        var overconfident = MoveSearch.Find(position, maxDepth: 1, evaluate: (_, _) => int.MaxValue);
        Assert.False(overconfident.Exact);
        Assert.True(overconfident.Score < 1_000_000);
    }

    private const string WinningPosition = "4Lr32 4Ss3 4Lr32 3Ir12 3Ir13 3Ir14";

    [Fact]
    public void Find_ShouldTakeImmediateWin_WhenConnectionCanBeCompleted()
    {
        var position = GamePosition.Replay(WinningPosition);
        var decision = MoveSearch.Find(position, maxDepth: 1);

        Assert.Equal(PlayerColor.Blue, position.Play(decision.Move).Result?.Winner);
        Assert.True(decision.Exact);
        Assert.True(decision.Score > 100_000);
    }

    [Fact]
    public void Find_ShouldKeepALegalFallback_WhenDeadlineHasAlreadyExpired()
    {
        var position = GamePosition.Create();
        var decision = MoveSearch.Find(position, shouldStop: () => true);

        Assert.Contains(decision.Move, position.GetLegalMoves());
        Assert.Equal(0, decision.CompletedDepth);
        Assert.False(decision.Exact);
    }

    [Fact]
    public void Find_ShouldKeepLastCompletedIteration_WhenNextIterationExhaustsNodeBudget()
    {
        var position = GamePosition.Create();
        var completed = MoveSearch.Find(position, maxDepth: 1);
        var interrupted = MoveSearch.Find(position, maxNodes: completed.Nodes + 10);

        Assert.Equal(1, interrupted.CompletedDepth);
        Assert.Equal(completed.Move, interrupted.Move);
        Assert.Equal(completed.Score, interrupted.Score);
        Assert.False(interrupted.Exact);
        Assert.InRange(interrupted.Nodes, completed.Nodes, completed.Nodes + 10);
    }

    [Fact]
    public void Find_ShouldBeDeterministicAndPreserveInput_WhenBoundedByNodes()
    {
        var position = GamePosition.Replay("15 3Lr15");
        var first = MoveSearch.Find(position, maxNodes: 500);
        var second = MoveSearch.Find(position, maxNodes: 500);

        Assert.Equal(first, second);
        Assert.Equal("15 3Lr15", position.Record);
        Assert.Contains(first.Move, position.GetLegalMoves());
    }

    [Fact]
    public void Find_ShouldRejectFinishedGame_WhenNoTurnRemains()
    {
        var position = GamePosition.Replay(WinningPosition + " 2r13");
        Assert.Equal("game-over", Assert.Throws<GameRuleException>(() => MoveSearch.Find(position)).Reason);
    }

    [Fact]
    public void Evaluate_ShouldRespectColorSymmetry_WhenColorsAreExchanged()
    {
        Assert.Equal(0, PositionEvaluation.Score(GamePosition.Create(), PlayerColor.Blue));
        var blue = PositionEvaluation.Score(GamePosition.Replay("15"), PlayerColor.Blue);
        var white = PositionEvaluation.Score(GamePosition.Replay("w 15"), PlayerColor.Blue);
        Assert.True(blue > 0);
        Assert.Equal(-blue, white);
    }
}
