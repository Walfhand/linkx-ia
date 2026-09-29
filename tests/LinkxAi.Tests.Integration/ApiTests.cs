using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public async Task MoveEndpoint_ShouldReturnSingleMoveToken_WhenRequestIsValid()
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

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Single(json.RootElement.EnumerateObject());
        Assert.Equal("15", json.RootElement.GetProperty("move").GetString());
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

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MoveEndpoint_ShouldRejectUnsignedRequest_WhenSecretIsConfigured()
    {
        using var securedFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("Linkx:Secret", "test-secret"));
        using var client = securedFactory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/move", new
        {
            protocol = 1, game = "game-1", color = "blue", record = "", deadline_ms = 6000
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MoveEndpoint_ShouldReturnMove_WhenSignatureMatchesExactBody()
    {
        using var securedFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("Linkx:Secret", "test-secret"));
        using var client = securedFactory.CreateClient();
        const string body = """{"protocol":1,"game":"game-1","color":"white","record":"b 15 --","deadline_ms":6000}""";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var bytes = Encoding.UTF8.GetBytes($"{timestamp}.{body}");
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("test-secret"), bytes));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/move")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Linkx-Timestamp", timestamp);
        request.Headers.Add("X-Linkx-Signature", $"sha256={signature}");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("15", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("move").GetString());
    }
}
