using System.Text.Json;
using LinkxAi.Api.Modules.Turns.Domain;
using Xunit;

namespace LinkxAi.Tests.Unit.ReferenceFixtures;

public sealed class SearchReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        using var stream = typeof(SearchReferenceTests).Assembly.GetManifestResourceStream(
            "LinkxAi.Tests.Unit.ReferenceFixtures.endgames.json")!;
        using var document = JsonDocument.Parse(stream);
        foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray())
            yield return [item.GetProperty("record").GetString()!, item.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Find_ShouldRecoverExactOutcome_WhenEndgameWasSolvedIndependently(string record, string snapshot)
    {
        using var document = JsonDocument.Parse(snapshot);
        var expected = document.RootElement;
        var decision = MoveSearch.Find(GamePosition.Replay(record), maxNodes: 100_000);

        Assert.True(decision.Exact);
        Assert.Equal(expected.GetProperty("value").GetInt32(), Math.Sign(decision.Score));
        Assert.Contains(decision.Move.ToString(), expected.GetProperty("optimalMoves").EnumerateArray().Select(move => move.GetString()));
    }
}
