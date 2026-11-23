namespace Candied.Primitives;

public readonly record struct GameId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct SymbolId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct PluginGuid(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ProcessId(int Value)
{
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct UnityInstanceId(string Value)
{
    public override string ToString() => Value;
}

public enum AssetFormat
{
    Png,
    Jpeg,
    Json
}
