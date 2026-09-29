using LinkxAi.Api.Modules.Turns.Domain;
using Xunit;

namespace LinkxAi.Tests.Unit;

public sealed class GameRecordTests
{
    // Replay examples from Marmelab's MIT-licensed reference, pinned in THIRD_PARTY_NOTICES.md.
    private const string BlueWin = "4Lr32 4Ss3 4Lr32 3Ir12 3Ir13 3Ir14 2r13";
    private const string WhiteWin = "4Tr21 4Lr34 3Ir12 3Ir12 4Lsr33 4Lr33 3Ir15 4Ss4 4Lsr26 4Ss8 4Tr16 3Lr17 27 3Ir16 12 2r13";
    private const string ForcedPass = "w 2r16 3Ir17 24 4Ssr12 4Ss4 16 3I5 3Ir16 3Ir19 3L2 4Ssr12 14 18 4Tr32 18 3L8 3Lr12 2r16 4Lsr27 2r18 4Lr18 4Lsr37 -- 4Lr14 3Lr24";
    private const string Ongoing = "4Lr32 3Ir12 3Ir12 3Ir13 4Tr24 4Lr38 3Ir15 15 2r13 2r15 15 2r13";
    private const string Draw = "w 3I4 3Lr37 3L6 3Ir14 2r19 12 3Ir17 4S1 13 4S1 3L8 18 11 3Ir13 2r14 4Lsr11 4Tr18 2r16 4Tr36 3Lr16 4Lsr33 4Tr38 4Sr12 2r15";
    private const string RecoverablePass = "w 4Lr31 3Lr37 24 4T2 3Ir15 3Lr17 4Lsr12 13 3Ir18 3Ir12 16 2r14 2r19 15 12 3Ir15 3Lr12 2r19 --";

    [Theory]
    [InlineData("", PlayerColor.Blue, "")]
    [InlineData("b", PlayerColor.Blue, "")]
    [InlineData("BLUE", PlayerColor.Blue, "")]
    [InlineData("w", PlayerColor.White, "w")]
    [InlineData("WhItE", PlayerColor.White, "w")]
    [InlineData(" b 15,3ir13+2R27 ", PlayerColor.White, "15 3Ir13 27")]
    public void Replay_ReadsFirstPlayerCaseAndSeparators(string source, PlayerColor active, string canonical)
    {
        var position = GamePosition.Replay(source);

        Assert.Equal(active, position.ActivePlayer);
        Assert.Equal(canonical, position.Record);
    }

    [Fact]
    public void Replay_RestoresEveryCellAndBothInventories()
    {
        var position = GamePosition.Replay(Ongoing);
        var rows = new[] { ".B..B....", ".BW.W....", ".BW.W....", ".WB.W....", ".WB.B....", ".WW.B....", ".BW.B..W.", ".BW.B..W.", ".BBBBB.WW" };

        for (var y = 0; y < 9; y++)
        for (var x = 0; x < 9; x++)
            Assert.Equal(rows[y][x] switch { 'B' => PlayerColor.Blue, 'W' => PlayerColor.White, _ => (PlayerColor?)null }, position.GetCell(x, y));
        Assert.Equal(new[] { 1, 1, 0, 2, 2, 1, 1 }, Enum.GetValues<Shape>().Select(shape => position.Remaining(PlayerColor.Blue, shape)));
        Assert.Equal(new[] { 1, 0, 0, 2, 2, 2, 1 }, Enum.GetValues<Shape>().Select(shape => position.Remaining(PlayerColor.White, shape)));
        Assert.Equal(PlayerColor.Blue, position.ActivePlayer);
        Assert.Equal(PlayerColor.Blue, position.FirstPlayer);
        Assert.Null(position.Result);
    }

    [Theory]
    [InlineData(BlueWin, PlayerColor.Blue)]
    [InlineData(WhiteWin, PlayerColor.White)]
    [InlineData("3I1 3Ir11 3I4 3Ir12 27 2r13 19", PlayerColor.Blue)]
    [InlineData("w 3I1 3Ir11 3I4 3Ir12 27 2r13 19", PlayerColor.White)]
    [InlineData("15 3Lr15 3Lr23 2r14 3Ir18 11 2r19 3L2 3Ir13 4Lr16 4L5 17 4S1", PlayerColor.Blue)]
    public void Play_EndsImmediatelyForEitherPlayerAndEitherAxis(string record, PlayerColor winner)
    {
        var lastSpace = record.LastIndexOf(' ');
        var before = GamePosition.Replay(record[..lastSpace]);
        Assert.Null(before.Result);

        var finished = before.Play(Move.Parse(record[(lastSpace + 1)..]));

        Assert.Equal(new GameResult(winner, GameEndReason.Connection), finished.Result);
        Assert.Empty(finished.GetLegalMoves());
        Assert.Equal("game-over", Assert.Throws<GameRuleException>(() => finished.Play(Move.Parse("11"))).Reason);
        Assert.Null(before.Result);
    }

    [Fact]
    public void Replay_BreaksAStalemateByLargestEightNeighborZone()
    {
        var finished = GamePosition.Replay(ForcedPass);

        Assert.Equal(new GameResult(PlayerColor.White, GameEndReason.Stalemate, 20, 23), finished.Result);
        Assert.Empty(finished.GetLegalMoves(PlayerColor.Blue));
        Assert.Empty(finished.GetLegalMoves(PlayerColor.White));
        Assert.Equal(ForcedPass, finished.Record);
    }

    [Fact]
    public void Replay_DeclaresDrawWhenLargestZonesAreEqual()
    {
        Assert.Equal(new GameResult(null, GameEndReason.Draw, 23, 23), GamePosition.Replay(Draw).Result);
    }

    [Fact]
    public void Replay_ReinstatesOmittedForcedPasses()
    {
        Assert.Equal(ForcedPass, GamePosition.Replay(ForcedPass.Replace(" --", "")).Record);
        Assert.Equal(RecoverablePass, GamePosition.Replay(RecoverablePass.Replace(" --", "")).Record);
    }

    [Fact]
    public void Play_RecomputesBlockedPlayersMovesAfterNewSupportAppears()
    {
        var blocked = GamePosition.Replay(RecoverablePass);
        Assert.Equal(PlayerColor.Blue, blocked.ActivePlayer);
        Assert.Empty(blocked.GetLegalMoves(PlayerColor.White));

        var resumed = blocked.Play(Move.Parse("4Lsr36"));

        Assert.Equal(PlayerColor.White, resumed.ActivePlayer);
        Assert.NotEmpty(resumed.GetLegalMoves());
        Assert.Null(resumed.Result);
    }

    [Theory]
    [InlineData(BlueWin)]
    [InlineData(WhiteWin)]
    [InlineData(ForcedPass)]
    [InlineData(Ongoing)]
    [InlineData(Draw)]
    public void Replay_PreservesCanonicalRecord(string record)
    {
        Assert.Equal(record, GamePosition.Replay(record).Record);
    }

    [Theory]
    [InlineData("11 12 13 14 15", 5, "15", "exhausted")]
    [InlineData("11 13 3I1", 3, "3I1", "unsupported")]
    [InlineData("3Ir11 3Ir11 3Ir11 2r11", 4, "2r11", "overflow")]
    [InlineData("w 3I9", 1, "3I9", "horizontal-bounds")]
    [InlineData("11 12 4Z3", 3, "4Z3", "syntax")]
    [InlineData(BlueWin + " 11", 8, "11", "game-over")]
    [InlineData("11 12 --", 3, "--", "unexpected-pass")]
    [InlineData(RecoverablePass + " --", 20, "--", "unexpected-pass")]
    public void Replay_RejectsWholeRecordWithOffendingTokenAndOneBasedRank(string record, int rank, string token, string reason)
    {
        GamePosition? result = null;

        var error = Assert.Throws<GameRuleException>(() => result = GamePosition.Replay(record));

        Assert.Null(result);
        Assert.Equal(rank, error.MoveNumber);
        Assert.Equal(token, error.Token);
        Assert.Equal(reason, error.Reason);
    }
}
