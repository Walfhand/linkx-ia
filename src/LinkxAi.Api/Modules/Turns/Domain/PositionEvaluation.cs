namespace LinkxAi.Api.Modules.Turns.Domain;

public static class PositionEvaluation
{
    private static readonly int[] CellCounts = [1, 2, 3, 3, 4, 4, 4];
    private static readonly int[][] Neighbors = Enumerable.Range(0, 81).Select(index =>
        Enumerable.Range(Math.Max(0, index / 9 - 1), Math.Min(8, index / 9 + 1) - Math.Max(0, index / 9 - 1) + 1)
            .SelectMany(y => Enumerable.Range(Math.Max(0, index % 9 - 1), Math.Min(8, index % 9 + 1) - Math.Max(0, index % 9 - 1) + 1)
                .Select(x => y * 9 + x)).Where(next => next != index).ToArray()).ToArray();

    public static int Score(GamePosition position, PlayerColor player)
    {
        // ponytail: geometric paths approximate future support; add gravity-aware terms only after paired benchmarks.
        var opponent = player == PlayerColor.Blue ? PlayerColor.White : PlayerColor.Blue;
        var mine = Potential(position, player);
        var theirs = Potential(position, opponent);
        var filled = Enumerable.Range(0, 81).Count(index => position.GetCell(index % 9, index / 9) is not null);
        var zones = position.MeasureZones(player).Largest - position.MeasureZones(opponent).Largest;
        return 100 * (theirs.Primary - mine.Primary) + 10 * (theirs.Secondary - mine.Secondary) + zones * (1 + filled / 9);
    }

    private static (int Primary, int Secondary) Potential(GamePosition position, PlayerColor player)
    {
        var horizontal = Distance(position, player, false);
        var vertical = Distance(position, player, true);
        var remaining = Enum.GetValues<Shape>().Sum(shape => position.Remaining(player, shape) * CellCounts[(int)shape]);
        horizontal = horizontal > remaining ? 20 : Math.Min(horizontal, 20);
        vertical = vertical > remaining ? 20 : Math.Min(vertical, 20);
        return (Math.Min(horizontal, vertical), Math.Max(horizontal, vertical));
    }

    private static int Distance(GamePosition position, PlayerColor player, bool vertical)
    {
        var distance = new int[81];
        Array.Fill(distance, int.MaxValue);
        var queue = new PriorityQueue<int, int>();
        for (var offset = 0; offset < 9; offset++)
        {
            var index = vertical ? offset : offset * 9;
            var cell = position.GetCell(index % 9, index / 9);
            if (cell is not null && cell != player) continue;
            distance[index] = cell == player ? 0 : 1;
            queue.Enqueue(index, distance[index]);
        }
        while (queue.TryDequeue(out var current, out var score))
        {
            if (score != distance[current]) continue;
            if (vertical ? current / 9 == 8 : current % 9 == 8) return score;
            foreach (var next in Neighbors[current])
            {
                var cell = position.GetCell(next % 9, next / 9);
                if (cell is not null && cell != player) continue;
                var candidate = score + (cell == player ? 0 : 1);
                if (candidate >= distance[next]) continue;
                distance[next] = candidate;
                queue.Enqueue(next, candidate);
            }
        }
        return 81;
    }
}
