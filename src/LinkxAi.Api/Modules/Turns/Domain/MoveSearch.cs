namespace LinkxAi.Api.Modules.Turns.Domain;

public sealed record SearchDecision(Move Move, int Score, int CompletedDepth, int Nodes, bool Exact);

public static class MoveSearch
{
    private const int Win = 1_000_000;

    public static SearchDecision Find(GamePosition position, int maxDepth = 28, int maxNodes = int.MaxValue, Func<bool>? shouldStop = null,
        Func<GamePosition, PlayerColor, int>? evaluate = null, Action<GamePosition>? push = null, Action? pop = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        ArgumentOutOfRangeException.ThrowIfNegative(maxNodes);
        if ((push is null) != (pop is null)) throw new ArgumentException("Evaluation push and pop must be supplied together.");
        if (position.Result is not null) throw new GameRuleException("game-over");
        var moves = position.GetLegalMoves();
        var remaining = Enum.GetValues<PlayerColor>().Sum(player => Enum.GetValues<Shape>().Sum(shape => position.Remaining(player, shape)));
        var context = new SearchContext(position.ActivePlayer, maxNodes, shouldStop, evaluate ?? PositionEvaluation.Score, push, pop);
        var decision = new SearchDecision(moves[0], 0, 0, 0, false);
        for (var depth = 1; depth <= Math.Min(maxDepth, remaining); depth++)
        {
            try
            {
                var score = context.Visit(position, depth, -Win - 1, Win + 1, out var best);
                var exact = depth >= remaining || Math.Abs(score) == Win;
                decision = new SearchDecision(best!.Value, score, depth, context.Nodes, exact);
                if (exact) break;
            }
            catch (SearchInterruptedException) { break; }
        }
        return decision with { Nodes = context.Nodes };
    }

    private sealed class SearchInterruptedException : Exception;
    private enum Bound { Exact, Lower, Upper }
    private sealed record Entry(int Depth, int Score, Bound Bound, Move? Best);

    private sealed class SearchContext(PlayerColor rootPlayer, int maxNodes, Func<bool>? shouldStop,
        Func<GamePosition, PlayerColor, int> evaluate, Action<GamePosition>? push, Action? pop)
    {
        private readonly Dictionary<string, Entry> table = new(StringComparer.Ordinal);
        public int Nodes { get; private set; }

        public int Visit(GamePosition position, int depth, int alpha, int beta, out Move? best)
        {
            if (Nodes >= maxNodes || shouldStop?.Invoke() == true) throw new SearchInterruptedException();
            Nodes++;
            best = null;
            if (position.Result is { } result)
                return result.Winner is null ? 0 : result.Winner == rootPlayer ? Win : -Win;
            if (depth == 0) return Math.Clamp(evaluate(position, rootPlayer), -Win + 1, Win - 1);

            var key = Key(position);
            var originalAlpha = alpha;
            var originalBeta = beta;
            if (table.TryGetValue(key, out var cached))
            {
                best = cached.Best;
                if (cached.Depth >= depth)
                {
                    if (cached.Bound == Bound.Exact) return cached.Score;
                    if (cached.Bound == Bound.Lower) alpha = Math.Max(alpha, cached.Score);
                    else beta = Math.Min(beta, cached.Score);
                    if (alpha >= beta) return cached.Score;
                }
            }
            var maximizing = position.ActivePlayer == rootPlayer;
            var score = maximizing ? -Win - 1 : Win + 1;
            var preferred = best;
            best = null;
            foreach (var move in position.GetLegalMoves().OrderByDescending(move => preferred == move))
            {
                var child = position.Play(move);
                push?.Invoke(child);
                int value;
                try { value = Visit(child, depth - 1, alpha, beta, out _); }
                finally { pop?.Invoke(); }
                if (best is null || (maximizing ? value > score : value < score))
                {
                    score = value;
                    best = move;
                }
                if (maximizing) alpha = Math.Max(alpha, score);
                else beta = Math.Min(beta, score);
                if (alpha >= beta || (maximizing ? score == Win : score == -Win)) break;
            }
            var bound = score <= originalAlpha ? Bound.Upper : score >= originalBeta ? Bound.Lower : Bound.Exact;
            // ponytail: cap per-search memory; use a fixed-size replacement table if profiling warrants it.
            if (table.Count < 100_000 || table.ContainsKey(key)) table[key] = new Entry(depth, score, bound, best);
            return score;
        }

        private static string Key(GamePosition position)
        {
            Span<char> key = stackalloc char[96];
            for (var index = 0; index < 81; index++)
                key[index] = position.GetCell(index % 9, index / 9) switch { PlayerColor.Blue => 'B', PlayerColor.White => 'W', _ => '.' };
            var offset = 81;
            foreach (var player in Enum.GetValues<PlayerColor>())
            foreach (var shape in Enum.GetValues<Shape>()) key[offset++] = (char)('0' + position.Remaining(player, shape));
            key[95] = position.ActivePlayer == PlayerColor.Blue ? 'B' : 'W';
            return new string(key);
        }
    }
}
