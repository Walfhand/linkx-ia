namespace LinkxAi.Api.Modules.Turns.Domain;

public sealed class GameRuleException(string reason, int? moveNumber = null, string? token = null)
    : ArgumentException(moveNumber is null ? reason : $"Move {moveNumber} ('{token}'): {reason}.", "record")
{
    public string Reason { get; } = reason;
    public int? MoveNumber { get; } = moveNumber;
    public string? Token { get; } = token;
}
