namespace Demo.App;

public interface INormalizer
{
    int Normalize(int value);
}

public static class NormalizerExtensions
{
    public static int AddSeed(this INormalizer normalizer, int value, int seed)
    {
        return normalizer.Normalize(value) + seed;
    }

    public static string Describe<T>(this T value)
    {
        return value?.ToString() ?? string.Empty;
    }
}

public class BaseNormalizer : INormalizer
{
    public virtual string Label => nameof(BaseNormalizer);
    public virtual LabelInfo? LabelChain => new LabelInfo(Label);

    public virtual int Normalize(int value)
    {
        return value;
    }

    public virtual int Normalize(string value)
    {
        return value.Length;
    }

    public virtual T EchoBase<T>(T value)
    {
        return value;
    }
}

public sealed class StepNormalizer : BaseNormalizer
{
    public override string Label => nameof(StepNormalizer);
    public override LabelInfo? LabelChain => new LabelInfo(Label + ".step");

    public override int Normalize(int value)
    {
        var current = value;
        while (current > 0)
        {
            if (current == 2)
            {
                current = current - 1;
                continue;
            }

            if (current > 6)
            {
                break;
            }

            current = current - 2;
        }

        return current;
    }

    public int NormalizeWithSwitch(int value)
    {
        switch (value)
        {
            case 0:
                return 0;
            case 1:
                value = value + 10;
                break;
            default:
                value = value + 1;
                break;
        }

        return value;
    }

    public T Echo<T>(T value)
    {
        return value;
    }

    public override T EchoBase<T>(T value)
    {
        return value;
    }
}

public sealed class LabelInfo
{
    public LabelInfo(string text)
    {
        Text = text;
    }

    public string Text { get; }
}

public sealed class Calculator
{
    private int _seed = 1;
    private readonly INormalizer _normalizer = new StepNormalizer();
    private readonly Calculator? _peer = null;
    private readonly Dictionary<int, string> _labels = new()
    {
        [0] = "zero",
        [1] = "one",
    };

    private string this[int index]
    {
        get => _labels[index];
        set => _labels[index] = value;
    }

    private int Seed
    {
        get => _seed;
        set => _seed = value;
    }

    private int ComputedSeed
    {
        get
        {
            return _seed + Seed;
        }
    }

    private int TrackedSeed
    {
        set
        {
            _seed = value + 1;
        }
    }

    public int Compute(int left, int right)
    {
        var sum = left + right;
        Seed = sum;
        var scaled = Seed * _seed;
        if (scaled > 10)
        {
            scaled = scaled - 1;
        }

        try
        {
            var normalized = _normalizer.Normalize(scaled);
            var fallback = ((BaseNormalizer)_normalizer).Normalize("seed");
            var labelName = ((BaseNormalizer)_normalizer).Label;
            var labelChainLength = ((BaseNormalizer)_normalizer).LabelChain?.Text.Length ?? 0;
            var computedSeed = ComputedSeed;
            var localBox = ((BaseNormalizer)_normalizer).LabelChain;
            localBox = new LabelInfo(labelName + string.Empty);
            var reboundLabelLength = localBox.Text.Length;
            TrackedSeed = normalized;
            Seed = normalized;
            var peerSeed = _peer?.Seed ?? 0;
            _labels[Seed] = normalized.Describe();
            this[Seed] = this[Seed] + labelName;
            var extended = _normalizer.AddSeed(normalized, fallback);
            var labelLength = extended.Describe().Length;
            var inheritedLabelLength = labelName.Length;
            var currentLabelLength = _labels[Seed].Length;
            var indexedLabelLength = this[Seed].Length;
            var externalText = labelLength.ToString();
            var inheritedEcho = ((BaseNormalizer)_normalizer).EchoBase(labelLength);
            var echoed = ((StepNormalizer)_normalizer).Echo(extended);
            return ((StepNormalizer)_normalizer).NormalizeWithSwitch(echoed + inheritedEcho + externalText.Length + currentLabelLength + indexedLabelLength + inheritedLabelLength + labelChainLength + reboundLabelLength + computedSeed + peerSeed);
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
        finally
        {
            _seed = _seed + 1;
        }
    }
}
