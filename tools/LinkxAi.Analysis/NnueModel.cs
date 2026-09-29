using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using LinkxAi.Api.Modules.Turns.Domain;

[assembly: InternalsVisibleTo("LinkxAi.Tests.Unit")]

// Experimental offline evaluator. Immutable weights may be shared; each search owns its accumulators.
internal sealed class NnueModel
{
    private static readonly Shape[] Shapes = Enum.GetValues<Shape>();
    private readonly int width;
    private readonly int[] transform, bias, hidden, hiddenBias, output, outputBias;

    private NnueModel(BinaryReader reader)
    {
        if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "LXNNU001") throw new ArgumentException("Unsupported NNUE format.");
        width = reader.ReadInt32();
        if (width is < 8 or > 1024 || width % 8 != 0 || reader.ReadInt32() != 294 || reader.ReadInt32() != 32)
            throw new ArgumentException("Unsupported NNUE dimensions.");
        int[] Read(int count, int bound, bool wide = false)
        {
            var values = new int[count];
            for (var i = 0; i < count; i++)
            {
                var value = wide ? reader.ReadInt32() : reader.ReadInt16();
                if (value < -bound || value > bound) throw new ArgumentException("NNUE parameter exceeds its safe integer range.");
                values[i] = value;
            }
            return values;
        }
        transform = Read(294 * width, 1024);
        bias = Read(width, 2048, true);
        hidden = Read(width * 2 * 32, 128);
        hiddenBias = Read(32, 131072, true);
        output = Read(32, 128);
        outputBias = Read(1, 131072, true);
        if (reader.BaseStream.ReadByte() != -1) throw new ArgumentException("Unexpected trailing NNUE data.");
    }

    public static NnueModel Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    public static NnueModel Load(Stream stream)
    {
        try
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            return new NnueModel(reader);
        }
        catch (EndOfStreamException error) { throw new ArgumentException("Truncated NNUE model.", error); }
    }

    public Accumulator CreateAccumulator(GamePosition root) => new(this, root);
    public float Value(GamePosition position) => CreateAccumulator(position).Value();

    // Fixed blue perspective. The other perspective swaps colors, preserving gravity and column order.
    private static void Encode(GamePosition position, Span<byte> features)
    {
        features.Clear();
        for (var x = 0; x < 9; x++)
        {
            var height = 0;
            for (var y = 0; y < 9; y++)
            {
                var cell = position.GetCell(x, y);
                if (cell is null) continue;
                if (height == 0) height = 9 - y;
                features[(cell == PlayerColor.Blue ? 0 : 81) + y * 9 + x] = 1;
            }
            features[204 + x * 10 + height] = 1;
        }
        foreach (var shape in Shapes)
        {
            features[162 + (int)shape * 3 + position.Remaining(PlayerColor.Blue, shape)] = 1;
            features[183 + (int)shape * 3 + position.Remaining(PlayerColor.White, shape)] = 1;
        }
    }

    private static int OtherPerspective(int feature) => feature < 162 ? (feature + 81) % 162
        : feature < 204 ? 162 + (feature - 162 + 21) % 42 : feature;

    private void Update(Span<int> accumulator, int feature, int sign)
    {
        var row = feature * width;
        var i = 0;
        for (; i <= width - Vector<int>.Count; i += Vector<int>.Count)
        {
            var current = new Vector<int>(accumulator[i..]);
            var weights = new Vector<int>(transform, row + i);
            (sign > 0 ? current + weights : current - weights).CopyTo(accumulator[i..]);
        }
        for (; i < width; i++) accumulator[i] += sign * transform[row + i];
    }

    private static int Dot(ReadOnlySpan<int> inputs, ReadOnlySpan<int> weights)
    {
        var sum = Vector<int>.Zero;
        var i = 0;
        for (; i <= inputs.Length - Vector<int>.Count; i += Vector<int>.Count)
            sum += new Vector<int>(inputs[i..]) * new Vector<int>(weights[i..]);
        var value = Vector.Sum(sum);
        for (; i < inputs.Length; i++) value += inputs[i] * weights[i];
        return value;
    }

    internal sealed class Accumulator
    {
        private readonly NnueModel model;
        private readonly int[][] stack = new int[29][];
        private readonly GamePosition?[] positions = new GamePosition[29];
        private readonly int[] inputs;
        private readonly int[] activations = new int[32];
        private int depth;

        public Accumulator(NnueModel model, GamePosition root)
        {
            this.model = model;
            positions[0] = root;
            stack[0] = new int[model.width * 2];
            inputs = new int[model.width * 2];
            model.bias.CopyTo(stack[0], 0);
            model.bias.CopyTo(stack[0], model.width);
            Span<byte> features = stackalloc byte[294];
            Encode(root, features);
            for (var feature = 0; feature < features.Length; feature++)
                if (features[feature] != 0) UpdateBoth(stack[0], feature, 1);
        }

        private void UpdateBoth(int[] state, int feature, int sign)
        {
            model.Update(state.AsSpan(0, model.width), feature, sign);
            model.Update(state.AsSpan(model.width), OtherPerspective(feature), sign);
        }

        public void Push(GamePosition child)
        {
            if (depth == 28) throw new InvalidOperationException("NNUE search exceeds the 28-placement game limit.");
            Span<byte> before = stackalloc byte[294];
            Span<byte> after = stackalloc byte[294];
            // ponytail: compare 81 cells per move; use explicit move deltas if this becomes a measured bottleneck.
            Encode(positions[depth]!, before);
            Encode(child, after);
            var next = stack[depth + 1] ??= new int[model.width * 2];
            stack[depth].CopyTo(next, 0);
            for (var feature = 0; feature < before.Length; feature++)
                if (before[feature] != after[feature]) UpdateBoth(next, feature, after[feature] - before[feature]);
            positions[++depth] = child;
        }

        public void Pop()
        {
            if (depth == 0) throw new InvalidOperationException("Cannot undo the NNUE root.");
            positions[depth--] = null;
        }

        internal int[] Snapshot() => (int[])stack[depth].Clone();

        public float Value()
        {
            var width = model.width;
            var own = positions[depth]!.ActivePlayer == PlayerColor.Blue ? 0 : width;
            var other = width - own;
            for (var i = 0; i < width; i++)
            {
                inputs[i] = Math.Clamp(stack[depth][own + i], 0, 256);
                inputs[width + i] = Math.Clamp(stack[depth][other + i], 0, 256);
            }
            for (var neuron = 0; neuron < 32; neuron++)
            {
                var sum = model.hiddenBias[neuron] + Dot(inputs, model.hidden.AsSpan(neuron * inputs.Length, inputs.Length));
                activations[neuron] = Math.Clamp((int)Math.Round(sum / 64.0, MidpointRounding.ToEven), 0, 256);
            }
            var result = model.outputBias[0] + Dot(activations, model.output);
            return MathF.Tanh(result / 16384f);
        }

        public int Score(GamePosition position, PlayerColor player)
        {
            if (!ReferenceEquals(position, positions[depth])) throw new InvalidOperationException("NNUE state does not match the search position.");
            var score = (int)(Value() * 10_000);
            return position.ActivePlayer == player ? score : -score;
        }
    }
}
