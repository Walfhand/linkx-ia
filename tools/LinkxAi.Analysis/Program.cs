using System.Diagnostics;
using System.Text.Json;
using LinkxAi.Api.Modules.Turns.Domain;

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
using var model = args.Length > 0 ? new OnnxEvaluation(args[0]) : null;
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
            if (model is null) throw new ArgumentException("Provide an ONNX model path for evaluation-only requests.");
            Console.WriteLine(JsonSerializer.Serialize(new { value = model.Value(position) }, json));
            continue;
        }
        var clock = Stopwatch.StartNew();
        var decision = MoveSearch.Find(position, request.MaxDepth, request.MaxNodes,
            () => clock.Elapsed.TotalMilliseconds >= request.BudgetMs, model is null ? null : model.Score);
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
