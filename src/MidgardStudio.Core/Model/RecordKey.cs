namespace MidgardStudio.Core.Model;

/// <summary>
/// A database record key: either an integer (Id) or a string (AegisName / Group / Skill / Mob).
/// String comparison is case-insensitive, matching rAthena/GRF conventions.
/// </summary>
public readonly struct RecordKey : IEquatable<RecordKey>
{
    private readonly long _int;
    private readonly string? _str;

    private RecordKey(long value)
    {
        _int = value;
        _str = null;
    }

    private RecordKey(string value)
    {
        _int = 0;
        _str = value;
    }

    public bool IsString => _str is not null;

    public long AsInt => _int;

    public string AsString => _str ?? _int.ToString();

    public static RecordKey Of(long value) => new(value);

    public static RecordKey Of(string value) => new(value);

    public bool Equals(RecordKey other) =>
        IsString == other.IsString &&
        (IsString
            ? string.Equals(_str, other._str, StringComparison.OrdinalIgnoreCase)
            : _int == other._int);

    public override bool Equals(object? obj) => obj is RecordKey other && Equals(other);

    public override int GetHashCode() =>
        IsString ? StringComparer.OrdinalIgnoreCase.GetHashCode(_str!) : _int.GetHashCode();

    public override string ToString() => AsString;
}
