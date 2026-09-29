using System.Diagnostics;
using System.Text.Json.Serialization;
using LinkxAi.Api.Modules.Turns.Domain;
using QuickApi.Engine.Web.Endpoints;
using QuickApi.Engine.Web.Endpoints.Enums;

namespace LinkxAi.Api.Modules.Turns.Features.MakeMove;

public sealed class MakeMoveEndpoint() : MinimalEndpoint<IResult>(EndpointType.Post, "move")
{
    protected override Delegate Handler => Handle;

    private static IResult Handle(Request request, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var turn = Turn.Create(request.Game, request.Color, request.Record, request.DeadlineMs);
            var budgetMs = Math.Max(0, Math.Min(4500, turn.DeadlineMs - 500));
            var move = turn.SelectMove(() => cancellationToken.IsCancellationRequested || clock.Elapsed.TotalMilliseconds >= budgetMs);
            return Results.Ok(new { move = move.ToString() });
        }
        catch (ArgumentException error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [error.ParamName ?? "request"] = [error.Message]
            });
        }
    }

    public sealed record Request(
        int Protocol,
        string? Game,
        string? Color,
        string? Record,
        [property: JsonPropertyName("deadline_ms")] int DeadlineMs);
}
