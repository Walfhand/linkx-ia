using LinkxAi.Api.Modules.Turns.Domain;
using Xunit;

namespace LinkxAi.Tests.Unit;

public sealed class SearchSamplesTests
{
    [Fact]
    public void Sampling_ShouldPreserveTheDecision_AndReturnReproducibleLegalNonterminalStates()
    {
        var root = GamePosition.Replay("15 3Lr15");
        var baseline = MoveSearch.Find(root, maxNodes: 1000);
        var first = new SearchSamples(4, 719);
        var sampled = MoveSearch.Find(root, maxNodes: 1000, push: first.Consider, pop: () => { });
        var second = new SearchSamples(4, 719);
        MoveSearch.Find(root, maxNodes: 1000, push: second.Consider, pop: () => { });

        Assert.Equal(baseline, sampled);
        Assert.Equal(first.Records, second.Records);
        Assert.Equal(4, first.Records.Count);
        Assert.Equal(4, first.Records.Distinct().Count());
        foreach (var record in first.Records)
        {
            Assert.StartsWith(root.Record + " ", record);
            Assert.Null(GamePosition.Replay(record).Result);
        }
        Assert.True(first.Seen > first.Records.Count);
    }

    [Fact]
    public void Sampling_ShouldRejectUnboundedRequests()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchSamples(-1, 42));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchSamples(33, 42));
    }
}
