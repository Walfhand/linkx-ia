using LinkxAi.Api.Modules.Turns.Domain;

// Bounded reservoir of generated positions. The teacher reanalyses them independently; search bounds are not exported as labels.
internal sealed class SearchSamples
{
    private readonly int capacity;
    private readonly Random random;
    private readonly List<string> records = [];
    public IReadOnlyList<string> Records => records;
    public int Seen { get; private set; }

    public SearchSamples(int capacity, int seed)
    {
        if (capacity is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
        random = new Random(seed);
    }

    public void Consider(GamePosition position)
    {
        if (position.Result is not null) return;
        Seen++;
        if (capacity == 0) return;
        var slot = records.Count < capacity ? records.Count : random.Next(Seen);
        if (slot >= capacity || records.Contains(position.Record, StringComparer.Ordinal)) return;
        if (slot == records.Count) records.Add(position.Record);
        else records[slot] = position.Record;
    }
}
