using System.Text.RegularExpressions;

namespace LinkxAi.Api.Modules.Turns.Domain;

public enum Shape { Mono, Domino, Bar3, SmallL, S, T, LargeL }

public readonly partial record struct Move(Shape Shape, int Rotation, bool Flipped, int Column)
{
    public static Move Parse(string token)
    {
        var match = MovePattern().Match(token);
        if (!match.Success) throw new GameRuleException("syntax");

        var shape = (Shape)Array.IndexOf(Pieces.Names, match.Groups[1].Value.ToUpperInvariant());
        var rotation = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 0;
        if (match.Groups[3].Value.Equals("l", StringComparison.OrdinalIgnoreCase))
            rotation = (4 - rotation) % 4;
        var orientation = Pieces.Get(shape, rotation, match.Groups[2].Length > 0);
        return new Move(shape, orientation.Rotation, orientation.Flipped, int.Parse(match.Groups[5].Value) - 1);
    }

    public override string ToString()
    {
        var orientation = Pieces.Get(Shape, Rotation, Flipped);
        if (Column is < 0 or > 8) throw new GameRuleException("horizontal-bounds");
        return $"{Pieces.Names[(int)Shape]}{(orientation.Flipped ? "s" : "")}{(orientation.Rotation == 0 ? "" : $"r{orientation.Rotation}")}{Column + 1}";
    }

    [GeneratedRegex(@"\A(1|2|3I|3L|4S|4T|4L)(S?)(?:([RL])([123]))?([1-9])\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MovePattern();
}

internal readonly record struct Cell(int X, int Y);
internal sealed record Orientation(Cell[] Cells, int Rotation, bool Flipped)
{
    public int Width { get; } = Cells.Max(cell => cell.X) + 1;
    public int Height { get; } = Cells.Max(cell => cell.Y) + 1;
}

internal static class Pieces
{
    internal static readonly string[] Names = ["1", "2", "3I", "3L", "4S", "4T", "4L"];
    private static readonly Cell[][] Shapes =
    [
        [new(0, 0)],
        [new(0, 0), new(1, 0)],
        [new(0, 0), new(1, 0), new(2, 0)],
        [new(0, 0), new(1, 0), new(0, 1)],
        [new(1, 0), new(0, 1), new(1, 1), new(0, 2)],
        [new(0, 0), new(1, 0), new(2, 0), new(1, 1)],
        [new(0, 0), new(1, 0), new(2, 0), new(0, 1)]
    ];

    private static readonly Orientation[][] Orientations = Shapes.Select(BuildOrientations).ToArray();
    internal static IEnumerable<Orientation> Unique(Shape shape) => Orientations[(int)shape].Distinct();

    internal static Orientation Get(Shape shape, int rotation, bool flipped)
    {
        if (!Enum.IsDefined(shape) || rotation is < 0 or > 3) throw new GameRuleException("syntax");
        return Orientations[(int)shape][(flipped ? 4 : 0) + rotation];
    }

    private static Orientation[] BuildOrientations(Cell[] shape)
    {
        var all = new List<Orientation>();
        foreach (var flipped in new[] { false, true })
        {
            var cells = shape.Select(cell => new Cell(flipped ? -cell.X : cell.X, cell.Y)).ToArray();
            for (var rotation = 0; rotation < 4; rotation++)
            {
                var minX = cells.Min(cell => cell.X);
                var minY = cells.Min(cell => cell.Y);
                cells = cells.Select(cell => new Cell(cell.X - minX, cell.Y - minY))
                    .OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray();
                var canonical = all.FirstOrDefault(item => item.Cells.SequenceEqual(cells));
                all.Add(canonical ?? new Orientation(cells, rotation, flipped));
                cells = cells.Select(cell => new Cell(-cell.Y, cell.X)).ToArray();
            }
        }
        return all.ToArray();
    }
}
