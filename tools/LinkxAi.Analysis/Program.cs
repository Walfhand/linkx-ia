using System.Diagnostics;
using System.Text.Json;
using LinkxAi.Api.Modules.Turns.Domain;

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var nnue = args.Length > 0 && Path.GetExtension(args[0]) == ".nnue" ? NnueModel.Load(args[0]) : null;
using var model = args.Length > 0 && nnue is null ? new OnnxEvaluation(args[0]) : null;
while (Console.ReadLine() is { } line)
{
    try
    {
        var request = JsonSerializer.Deserialize<AnalysisRequest>(line, json)
            ?? throw new ArgumentException("An analysis request is required.");
        ArgumentOutOfRangeException.ThrowIfNegative(request.BudgetMs);
        var position = GamePosition.Replay(request.Record);
        if (request.EvaluateOnly)
        {
            if (model is null && nnue is null) throw new ArgumentException("Provide an ONNX or NNUE model path for evaluation-only requests.");
            Console.WriteLine(JsonSerializer.Serialize(new { value = nnue?.Value(position) ?? model!.Value(position) }, json));
            continue;
        }
        var clock = Stopwatch.StartNew();
        var accumulator = nnue?.CreateAccumulator(position);
        Func<GamePosition, PlayerColor, int>? evaluate = accumulator is not null ? accumulator.Score : model is not null ? model.Score : null;
        var decision = MoveSearch.Find(position, request.MaxDepth, request.MaxNodes,
            () => clock.Elapsed.TotalMilliseconds >= request.BudgetMs, evaluate,
            accumulator is null ? null : accumulator.Push, accumulator is null ? null : accumulator.Pop);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            move = decision.Move.ToString(), decision.Score, depth = decision.CompletedDepth,
            decision.Nodes, decision.Exact, elapsedMs = clock.Elapsed.TotalMilliseconds
        }, json));
    }
    catch (Exception error) when (error is ArgumentException or JsonException)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { error = error.Message }, json));
    }
}

internal sealed record AnalysisRequest(string Record, int MaxDepth = 28, int MaxNodes = int.MaxValue, int BudgetMs = 1000, bool EvaluateOnly = false);
