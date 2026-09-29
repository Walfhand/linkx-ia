using QuickApi.Engine.Web.Endpoints;
using QuickApi.Engine.Web.Endpoints.Enums;

namespace LinkxAi.Api.Modules.Operations.Features.Health;

public sealed class HealthEndpoint() : MinimalEndpoint<string>(EndpointType.Get, "health")
{
    protected override Delegate Handler => Handle;

    private static IResult Handle() => Results.Text("ok");
}
