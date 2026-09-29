using System.Text.Json;
using LinkxAi.Api.Modules.Turns.Domain;
using Xunit;

namespace LinkxAi.Tests.Unit.ReferenceFixtures;

public sealed class RulesReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        using var stream = typeof(RulesReferenceTests).Assembly.GetManifestResourceStream(
            "LinkxAi.Tests.Unit.ReferenceFixtures.rules.json")!;
        using var document = JsonDocument.Parse(stream);
        foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray())
            yield return [item.GetProperty("record").GetString()!, item.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Replay_ShouldMatchReference_WhenRecordWasCaptured(string record, string snapshot)
    {
        using var document = JsonDocument.Parse(snapshot);
        var expected = document.RootElement;
        if (expected.TryGetProperty("error", out var error))
        {
            var actual = Assert.Throws<GameRuleException>(() => GamePosition.Replay(record));
            Assert.Equal(error.GetProperty("reason").GetString(), actual.Reason);
            Assert.Equal(error.GetProperty("moveNumber").GetInt32(), actual.MoveNumber);
            Assert.Equal(error.GetProperty("token").GetString(), actual.Token);
            return;
        }

        var position = GamePosition.Replay(record);
        Assert.Equal(expected.GetProperty("canonical").GetString(), position.Record);
        Assert.Equal(expected.GetProperty("firstPlayer").GetString(), Name(position.FirstPlayer));
        Assert.Equal(expected.GetProperty("activePlayer").GetString(), Name(position.ActivePlayer));
        var board = string.Concat(Enumerable.Range(0, 81).Select(index => position.GetCell(index % 9, index / 9) switch
        {
            PlayerColor.Blue => "B", PlayerColor.White => "W", _ => "."
        }));
        Assert.Equal(expected.GetProperty("board").GetString(), board);
        foreach (var player in Enum.GetValues<PlayerColor>())
        {
            var inventory = expected.GetProperty("inventories").GetProperty(Name(player));
            foreach (var shape in Enum.GetValues<Shape>())
                Assert.Equal(inventory[(int)shape].GetInt32(), position.Remaining(player, shape));
        }
        Assert.Equal(
            expected.GetProperty("legalMoves").EnumerateArray().Select(move => move.GetString()),
            position.GetLegalMoves().Select(move => move.ToString()).Order(StringComparer.Ordinal));

        var result = expected.GetProperty("result");
        if (result.ValueKind == JsonValueKind.Null)
            Assert.Null(position.Result);
        else
        {
            var winner = result.GetProperty("winner").GetString();
            var scores = result.TryGetProperty("largestZones", out var zones);
            var expectedResult = new GameResult(
                winner is null ? null : Enum.Parse<PlayerColor>(winner, true),
                Enum.Parse<GameEndReason>(result.GetProperty("reason").GetString()!, true),
                scores ? zones.GetProperty("blue").GetInt32() : null,
                scores ? zones.GetProperty("white").GetInt32() : null);
            Assert.Equal(expectedResult, position.Result);
        }
    }

    private static string Name(PlayerColor player) => player.ToString().ToLowerInvariant();
}
