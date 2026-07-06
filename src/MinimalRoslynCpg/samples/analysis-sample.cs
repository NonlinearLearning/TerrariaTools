namespace Demo.App;

/// <summary>
/// 样例图使用的最小归一化接口。
/// </summary>
public interface INormalizer
{
    /// <summary>
    /// 归一化一个整数输入。
    /// </summary>
    int Normalize(int value);
}

/// <summary>
/// 用于覆盖扩展方法调用场景的样例扩展集合。
/// </summary>
public static class NormalizerExtensions
{
    /// <summary>
    /// 在归一化结果上叠加外部种子值。
    /// </summary>
    public static int AddSeed(this INormalizer normalizer, int value, int seed)
    {
        return normalizer.Normalize(value) + seed;
    }

    /// <summary>
    /// 生成一个简单字符串投影，覆盖泛型扩展调用。
    /// </summary>
    public static string Describe<T>(this T value)
    {
        return value?.ToString() ?? string.Empty;
    }
}

/// <summary>
/// 提供虚方法、属性和泛型方法，供派发与属性流样例复用。
/// </summary>
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

/// <summary>
/// 具体实现体，覆盖循环、switch、override 和泛型调用样例。
/// </summary>
public sealed class StepNormalizer : BaseNormalizer
{
    public override string Label => nameof(StepNormalizer);
    public override LabelInfo? LabelChain => new LabelInfo(Label + ".step");

    public override int Normalize(int value)
    {
        var current = value;
        // 故意保留 continue 和 break，方便 CFG 构图覆盖循环跳转边。
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

    /// <summary>
    /// 生成一个小型 switch 控制流样例。
    /// </summary>
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

    /// <summary>
    /// 泛型回声方法，用于调用图与返回流样例。
    /// </summary>
    public T Echo<T>(T value)
    {
        return value;
    }

    public override T EchoBase<T>(T value)
    {
        return value;
    }
}

/// <summary>
/// 轻量标签类型，用于属性、成员访问和可空链样例。
/// </summary>
public sealed class LabelInfo
{
    /// <summary>
    /// 创建一个标签包装对象。
    /// </summary>
    public LabelInfo(string text)
    {
        Text = text;
    }

    public string Text { get; }
}

/// <summary>
/// 聚合 CLI 默认样例里的控制流、调用图和数据流场景。
/// </summary>
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

    /// <summary>
    /// 在一个方法体内串联算术、派发、属性、异常和数据流样例。
    /// </summary>
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
