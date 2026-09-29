using System.Text.Json.Serialization;
using LinkxAi.Api.Modules.Turns.Domain;
using QuickApi.Engine.Web.Endpoints;
using QuickApi.Engine.Web.Endpoints.Enums;

namespace LinkxAi.Api.Modules.Turns.Features.MakeMove;

public sealed class MakeMoveEndpoint() : MinimalEndpoint<IResult>(EndpointType.Post, "move")
{
    protected override Delegate Handler => Handle;

    private static IResult Handle(Request request)
    {
        try
        {
            _ = Turn.Create(request.Game, request.Color, request.Record, request.DeadlineMs);
        }
        catch (ArgumentException error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [error.ParamName ?? "request"] = [error.Message]
            });
        }

        return Results.StatusCode(StatusCodes.Status501NotImplemented);
    }

    public sealed record Request(
        int Protocol,
        string? Game,
        string? Color,
        string? Record,
        [property: JsonPropertyName("deadline_ms")] int DeadlineMs);
}
