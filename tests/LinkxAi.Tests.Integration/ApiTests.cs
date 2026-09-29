using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LinkxAi.Tests.Integration;

public sealed class ApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task HealthEndpoint_ShouldReturnOk_WhenServiceIsRunning()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MoveEndpoint_ShouldReturnNotImplemented_WhenRulesArePending()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/move", new
        {
            protocol = 1,
            game = "game-1",
            color = "blue",
            record = "",
            deadline_ms = 6000
        });

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact]
    public async Task MoveEndpoint_ShouldReturnBadRequest_WhenColorIsInvalid()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/move", new
        {
            protocol = 1,
            game = "game-1",
            color = "red",
            record = "",
            deadline_ms = 6000
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MoveEndpoint_ShouldAcceptUnknownProtocol_WhenOtherFieldsAreValid()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/move", new
        {
            protocol = 99,
            game = "game-1",
            color = "white",
            record = "",
            deadline_ms = 6000
        });

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }
}
