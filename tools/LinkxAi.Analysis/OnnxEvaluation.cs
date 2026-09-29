using LinkxAi.Api.Modules.Turns.Domain;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

// One instance per sequential analysis worker; tensor buffers are reused between evaluations.
internal sealed class OnnxEvaluation : IDisposable
{
    private static readonly Shape[] Shapes = Enum.GetValues<Shape>();
    private readonly InferenceSession session;
    private readonly float[] features = new float[176];
    private readonly NamedOnnxValue[] inputs;

    public OnnxEvaluation(string path)
    {
        using var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 };
        session = new InferenceSession(path, options);
        inputs = [NamedOnnxValue.CreateFromTensor("position", new DenseTensor<float>(features, [1, 176]))];
    }

    public float Value(GamePosition position)
    {
        Array.Clear(features);
        var own = position.ActivePlayer;
        for (var index = 0; index < 81; index++)
        {
            var cell = position.GetCell(index % 9, index / 9);
            if (cell is not null) features[(cell == own ? 0 : 81) + index] = 1;
        }
        var other = own == PlayerColor.Blue ? PlayerColor.White : PlayerColor.Blue;
        foreach (var shape in Shapes)
        {
            features[162 + (int)shape] = position.Remaining(own, shape) / 2f;
            features[169 + (int)shape] = position.Remaining(other, shape) / 2f;
        }
        using var outputs = session.Run(inputs);
        var value = outputs.First().AsEnumerable<float>().First();
        if (!float.IsFinite(value) || Math.Abs(value) > 1.001f)
            throw new InvalidOperationException("The model returned an invalid value.");
        return value;
    }

    public int Score(GamePosition position, PlayerColor player)
    {
        var value = (int)(Value(position) * 10_000);
        return position.ActivePlayer == player ? value : -value;
    }

    public void Dispose() => session.Dispose();
}
