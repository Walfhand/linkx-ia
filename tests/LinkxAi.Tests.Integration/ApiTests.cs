using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LinkxAi.Api.Modules.Turns.Domain;
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

    [Theory]
    [InlineData("", "blue")]
    [InlineData("w", "white")]
    [InlineData("b 15", "white")]
    [InlineData("11 12 13 14", "blue")]
    [InlineData("w 11 12 13 14", "white")]
    [InlineData("4Lr32 3Ir12 3Ir12 3Ir13 4Tr24 4Lr38 3Ir15 15 2r13 2r15 15 2r13", "blue")]
    [InlineData("w 4Lr31 3Lr37 24 4T2 3Ir15 3Lr17 4Lsr12 13 3Ir18 3Ir12 16 2r14 2r19 15 12 3Ir15 3Lr12 2r19 --", "blue")]
    public async Task MoveEndpoint_ShouldReturnSingleLegalMoveToken_WhenRequestIsValid(string record, string color)
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/move", new
        {
            protocol = 1,
            game = "game-1",
            color,
            record,
            deadline_ms = 6000
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Single(json.RootElement.EnumerateObject());
        var token = json.RootElement.GetProperty("move").GetString()!;
        var move = Move.Parse(token);
        var position = GamePosition.Replay(record);
        Assert.Contains(move, position.GetLegalMoves());
        Assert.Equal(token, move.ToString());
        Assert.NotEqual("--", token);
        Assert.NotEqual(position.Record, position.Play(move).Record);
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
            record = "w",
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
        const string body = """{"protocol":1,"game":"game-1","color":"white","record":"b 15","deadline_ms":6000}""";
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
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("move").GetString()!;
        Assert.Contains(Move.Parse(token), GamePosition.Replay("b 15").GetLegalMoves());
    }

    [Theory]
    [InlineData("11 12 --", "blue", "record")]
    [InlineData("4Z3", "blue", "record")]
    [InlineData("4T1", "blue", "record")]
    [InlineData("11 12 13 14 15", "white", "record")]
    [InlineData("", "white", "color")]
    [InlineData("w", "blue", "color")]
    [InlineData("4Lr32 4Ss3 4Lr32 3Ir12 3Ir13 3Ir14 2r13", "blue", "record")]
    public async Task MoveEndpoint_ShouldRejectImpossibleRequestWithoutInventingAMove(string record, string color, string field)
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/move", new
        {
            protocol = 1, game = "game-1", color, record, deadline_ms = 6000
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("errors").TryGetProperty(field, out _));
        Assert.False(json.TryGetProperty("move", out _));
    }

    [Fact]
    public async Task MoveEndpoint_ShouldIsolateSimultaneousGamesAndReturnDeterministicMoves()
    {
        using var client = factory.CreateClient();
        var records = new[] { "11 12 13 14", "w 11 12 13 14" };
        var requests = Enumerable.Range(0, 2).Select(async index =>
        {
            var payload = new { protocol = 1, game = $"game-{index}", color = index == 0 ? "blue" : "white", record = records[index], deadline_ms = 6000 };
            var response = await client.PostAsJsonAsync("/api/v1/move", payload);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("move").GetString()!;
            Assert.Contains(Move.Parse(token), GamePosition.Replay(records[index]).GetLegalMoves());
            var repeated = await client.PostAsJsonAsync("/api/v1/move", payload);
            Assert.Equal(token, (await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("move").GetString());
        });

        await Task.WhenAll(requests);
    }
}
