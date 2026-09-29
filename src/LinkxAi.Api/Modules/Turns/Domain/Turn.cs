namespace LinkxAi.Api.Modules.Turns.Domain;

public enum PlayerColor { Blue, White }

public sealed record Turn
{
    private Turn(string game, PlayerColor color, string record, int deadlineMs)
    {
        Game = game;
        Color = color;
        Record = record;
        DeadlineMs = deadlineMs;
    }

    public string Game { get; }
    public PlayerColor Color { get; }
    public string Record { get; }
    public int DeadlineMs { get; }

    public static Turn Create(string? game, string? color, string? record, int deadlineMs)
    {
        if (string.IsNullOrWhiteSpace(game))
            throw new ArgumentException("A game identifier is required.", nameof(game));
        if (color is not ("blue" or "white"))
            throw new ArgumentException("Color must be blue or white.", nameof(color));
        if (record is null)
            throw new ArgumentNullException(nameof(record));
        if (deadlineMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(deadlineMs));

        return new Turn(game, color == "blue" ? PlayerColor.Blue : PlayerColor.White, record, deadlineMs);
    }
}
