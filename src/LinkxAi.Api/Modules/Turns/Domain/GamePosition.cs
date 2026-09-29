using System.Text.RegularExpressions;

namespace LinkxAi.Api.Modules.Turns.Domain;

public enum GameEndReason { Connection, Stalemate, Draw }
public sealed record GameResult(PlayerColor? Winner, GameEndReason Reason, int? BlueLargestZone = null, int? WhiteLargestZone = null);

public sealed partial class GamePosition
{
    private readonly PlayerColor?[] board;
    private readonly int[,] inventories;

    private GamePosition(PlayerColor?[] board, int[,] inventories, PlayerColor activePlayer, PlayerColor firstPlayer, string record)
    {
        this.board = board;
        this.inventories = inventories;
        ActivePlayer = activePlayer;
        FirstPlayer = firstPlayer;
        Record = record;
    }

    public PlayerColor ActivePlayer { get; private set; }
    public PlayerColor FirstPlayer { get; }
    public string Record { get; private set; }
    public GameResult? Result { get; private set; }

    public static GamePosition Replay(string record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var tokens = TokenSeparator().Split(record).Where(token => token.Length > 0).ToArray();
        PlayerColor? declared = tokens.FirstOrDefault()?.ToLowerInvariant() switch
        {
            "b" or "blue" => PlayerColor.Blue,
            "w" or "white" => PlayerColor.White,
            _ => null
        };
        var position = Create(declared ?? PlayerColor.Blue);
        var offset = declared.HasValue ? 1 : 0;
        var pendingPass = false;
        for (var index = offset; index < tokens.Length; index++)
        {
            var token = tokens[index];
            try
            {
                if (token == "--")
                {
                    if (!pendingPass) throw new GameRuleException("unexpected-pass");
                    pendingPass = false;
                    continue;
                }
                if (position.Result is not null) throw new GameRuleException("game-over");
                position = position.Play(Move.Parse(token));
                pendingPass = position.Record.EndsWith(" --", StringComparison.Ordinal);
            }
            catch (GameRuleException error)
            {
                throw new GameRuleException(error.Reason, index - offset + 1, token);
            }
        }
        return position;
    }

    public static GamePosition Create(PlayerColor firstPlayer = PlayerColor.Blue)
    {
        if (!Enum.IsDefined(firstPlayer)) throw new ArgumentOutOfRangeException(nameof(firstPlayer));
        var inventories = new int[2, 7];
        for (var color = 0; color < 2; color++)
        for (var shape = 0; shape < 7; shape++) inventories[color, shape] = 2;
        return new GamePosition(new PlayerColor?[81], inventories, firstPlayer, firstPlayer,
            firstPlayer == PlayerColor.White ? "w" : "");
    }

    public GamePosition Play(Move move)
    {
        if (Result is not null) throw new GameRuleException("game-over");
        _ = Pieces.Get(move.Shape, move.Rotation, move.Flipped);
        if (Remaining(ActivePlayer, move.Shape) == 0) throw new GameRuleException("exhausted");
        var reason = Drop(move, out var cells);
        if (reason is not null) throw new GameRuleException(reason);

        var nextBoard = (PlayerColor?[])board.Clone();
        foreach (var cell in cells) nextBoard[cell.Y * 9 + cell.X] = ActivePlayer;
        var nextInventories = (int[,])inventories.Clone();
        nextInventories[(int)ActivePlayer, (int)move.Shape]--;
        var record = Record.Length == 0 ? move.ToString() : $"{Record} {move}";
        var next = new GamePosition(nextBoard, nextInventories, ActivePlayer, FirstPlayer, record);
        var ownZones = next.MeasureZones(ActivePlayer);
        if (ownZones.Winning)
            next.Result = new GameResult(ActivePlayer, GameEndReason.Connection);
        else if (next.EnumerateLegalMoves(Other(ActivePlayer)).Any())
            next.ActivePlayer = Other(ActivePlayer);
        else if (next.EnumerateLegalMoves(ActivePlayer).Any())
            next.Record += " --";
        else
        {
            var opponentZones = next.MeasureZones(Other(ActivePlayer));
            var blue = ActivePlayer == PlayerColor.Blue ? ownZones.Largest : opponentZones.Largest;
            var white = ActivePlayer == PlayerColor.White ? ownZones.Largest : opponentZones.Largest;
            next.Result = new GameResult(blue == white ? null : blue > white ? PlayerColor.Blue : PlayerColor.White,
                blue == white ? GameEndReason.Draw : GameEndReason.Stalemate, blue, white);
        }
        return next;
    }

    public PlayerColor? GetCell(int x, int y)
    {
        if (x is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(x));
        if (y is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(y));
        return board[y * 9 + x];
    }

    public int Remaining(PlayerColor color, Shape shape)
    {
        if (!Enum.IsDefined(color)) throw new ArgumentOutOfRangeException(nameof(color));
        if (!Enum.IsDefined(shape)) throw new ArgumentOutOfRangeException(nameof(shape));
        return inventories[(int)color, (int)shape];
    }

    public IReadOnlyList<Move> GetLegalMoves() => GetLegalMoves(ActivePlayer);
    public IReadOnlyList<Move> GetLegalMoves(PlayerColor color)
    {
        if (!Enum.IsDefined(color)) throw new ArgumentOutOfRangeException(nameof(color));
        return Result is null ? EnumerateLegalMoves(color).ToArray() : [];
    }

    private IEnumerable<Move> EnumerateLegalMoves(PlayerColor color)
    {
        foreach (var shape in Enum.GetValues<Shape>())
        {
            if (Remaining(color, shape) == 0) continue;
            foreach (var orientation in Pieces.Unique(shape))
            for (var column = 0; column <= 9 - orientation.Width; column++)
            {
                var move = new Move(shape, orientation.Rotation, orientation.Flipped, column);
                if (Drop(move, out _) is null) yield return move;
            }
        }
    }

    private string? Drop(Move move, out Cell[] cells)
    {
        cells = [];
        var orientation = Pieces.Get(move.Shape, move.Rotation, move.Flipped);
        if (move.Column < 0 || move.Column > 9 - orientation.Width) return "horizontal-bounds";
        var row = -orientation.Height;
        while (!orientation.Cells.Any(cell =>
            cell.Y + row + 1 >= 9 ||
            (cell.Y + row + 1 >= 0 && board[(cell.Y + row + 1) * 9 + cell.X + move.Column] is not null)))
            row++;

        cells = orientation.Cells.Select(cell => new Cell(cell.X + move.Column, cell.Y + row)).ToArray();
        if (cells.Any(cell => cell.Y < 0)) return "overflow";
        foreach (var cell in cells)
            if (cell.Y != 8 && !cells.Contains(new Cell(cell.X, cell.Y + 1)) && board[(cell.Y + 1) * 9 + cell.X] is null)
                return "unsupported";
        return null;
    }

    private static PlayerColor Other(PlayerColor color) => color == PlayerColor.Blue ? PlayerColor.White : PlayerColor.Blue;

    internal (int Largest, bool Winning) MeasureZones(PlayerColor color)
    {
        var visited = new bool[81];
        var largest = 0;
        var winning = false;
        for (var start = 0; start < board.Length; start++)
        {
            if (visited[start] || board[start] != color) continue;
            var queue = new Queue<int>();
            queue.Enqueue(start);
            visited[start] = true;
            var count = 0;
            var edges = 0;
            while (queue.TryDequeue(out var index))
            {
                count++;
                var x = index % 9;
                var y = index / 9;
                if (x == 0) edges |= 1;
                if (x == 8) edges |= 2;
                if (y == 0) edges |= 4;
                if (y == 8) edges |= 8;
                for (var ny = Math.Max(0, y - 1); ny <= Math.Min(8, y + 1); ny++)
                for (var nx = Math.Max(0, x - 1); nx <= Math.Min(8, x + 1); nx++)
                {
                    var neighbor = ny * 9 + nx;
                    if (visited[neighbor] || board[neighbor] != color) continue;
                    visited[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }
            largest = Math.Max(largest, count);
            winning |= (edges & 3) == 3 || (edges & 12) == 12;
        }
        return (largest, winning);
    }

    [GeneratedRegex(@"[\s,+]+")]
    private static partial Regex TokenSeparator();
}
