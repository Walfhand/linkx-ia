using System.Text;
using System.Text.Json;
using LinkxAi.Api.Modules.Turns.Domain;
using LinkxAi.Tests.Unit.ReferenceFixtures;
using Xunit;

namespace LinkxAi.Tests.Unit;

public sealed class NnueTests
{
    [Fact]
    public void Search_ShouldProveTheWinningContinuation_WithinTheApiNodeBudget()
    {
        // A recorded loss: 15 allows a forced defeat; the independent teacher proves a win beginning with 17.
        const string record = "25 2r11 3Lr32 4Lsr11 4Lsr15 4Ss3 3Ir16 2r16 4Tr13 3Ir12 12 4S3 4Tr33 4Lr38";
        var model = NnueModel.Load(Path.Combine(AppContext.BaseDirectory, "ReferenceFixtures/nnue-h512.nnue"));
        var position = GamePosition.Replay(record);
        var accumulator = model.CreateAccumulator(position);
        var decision = MoveSearch.Find(position, maxNodes: 100_000,
            evaluate: accumulator.Score, push: accumulator.Push, pop: accumulator.Pop);
        Assert.True(decision.Exact);
        Assert.Equal(1_000_000, decision.Score);
        Assert.NotEqual("15", decision.Move.ToString());
    }

    [Fact]
    public void IncrementalEvaluation_ShouldEqualRecomputation_AcrossMovesPassesAndUndo()
    {
        using var data = ModelBytes();
        var model = NnueModel.Load(data);
        var sawPass = false;
        var count = 0;
        foreach (var item in RulesReferenceTests.Cases())
        {
            using var snapshot = JsonDocument.Parse((string)item[1]);
            if (snapshot.RootElement.TryGetProperty("error", out _)) continue;
            var target = GamePosition.Replay((string)item[0]);
            var position = GamePosition.Create(target.FirstPlayer);
            var accumulator = model.CreateAccumulator(position);
            var tokens = target.Record.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens.Where(token => token is not ("w" or "--")))
            {
                var next = position.Play(Move.Parse(token));
                sawPass |= next.ActivePlayer == position.ActivePlayer && next.Result is null;
                accumulator.Push(next);
                Assert.Equal(model.CreateAccumulator(next).Snapshot(), accumulator.Snapshot());
                Assert.Equal(model.Value(next), accumulator.Value());
                accumulator.Pop();
                Assert.Equal(model.CreateAccumulator(position).Snapshot(), accumulator.Snapshot());
                accumulator.Push(next);
                position = next;
                count++;
            }
            foreach (var move in position.GetLegalMoves())
            {
                var child = position.Play(move);
                accumulator.Push(child);
                Assert.Equal(model.CreateAccumulator(child).Snapshot(), accumulator.Snapshot());
                Assert.Equal(model.Value(child), accumulator.Value());
                accumulator.Pop();
            }
        }
        Assert.True(sawPass);
        Assert.True(count > 100);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(256)]
    [InlineData(1024)]
    public void Search_ShouldMatchFullEvaluation_AndRestoreRootAfterCancellation(int width)
    {
        using var data = ModelBytes(width);
        var model = NnueModel.Load(data);
        var root = GamePosition.Replay("15 3Lr15");
        var accumulator = model.CreateAccumulator(root);
        var expected = MoveSearch.Find(root, maxNodes: 1000, evaluate: (position, player) =>
            (int)(model.Value(position) * 10_000) * (position.ActivePlayer == player ? 1 : -1));
        var actual = MoveSearch.Find(root, maxNodes: 1000, evaluate: accumulator.Score,
            push: accumulator.Push, pop: accumulator.Pop);
        Assert.Equal(expected, actual);
        Assert.Equal(model.CreateAccumulator(root).Snapshot(), accumulator.Snapshot());
        Assert.Equal(model.Value(root), accumulator.Value());
        Assert.Throws<InvalidOperationException>(accumulator.Pop);
        Assert.Equal(model.Value(GamePosition.Replay("15")), model.Value(GamePosition.Replay("w 15")));
    }

    [Fact]
    public void ModelLoader_ShouldRejectWrongSchemaTruncationTrailingDataAndUnsafeRanges()
    {
        using var good = ModelBytes();
        var bytes = good.ToArray();
        var badMagic = (byte[])bytes.Clone(); badMagic[0] = 0;
        var badWidth = (byte[])bytes.Clone(); BitConverter.GetBytes(int.MaxValue).CopyTo(badWidth, 8);
        var badWeight = (byte[])bytes.Clone(); BitConverter.GetBytes(short.MaxValue).CopyTo(badWeight, 20);
        foreach (var bad in new[] { badMagic, badWidth, badWeight, bytes[..^1], bytes.Concat(new byte[] { 0 }).ToArray() })
            Assert.ThrowsAny<ArgumentException>(() => NnueModel.Load(new MemoryStream(bad)));
    }

    private static MemoryStream ModelBytes(int width = 8)
    {
        var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("LXNNU001"));
        writer.Write(width); writer.Write(294); writer.Write(32);
        var random = new Random(902);
        void Layer(int weights, int biases, int bound)
        {
            for (var i = 0; i < weights; i++) writer.Write((short)random.Next(-bound, bound + 1));
            for (var i = 0; i < biases; i++) writer.Write(random.Next(0, 100));
        }
        Layer(294 * width, width, 24);
        Layer(width * 2 * 32, 32, 12);
        Layer(32, 1, 16);
        stream.Position = 0;
        return stream;
    }
}
