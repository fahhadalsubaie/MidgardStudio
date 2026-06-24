namespace MidgardStudio.Core.Model;

/// <summary>
/// Wraps a rAthena script body so the YAML serializer knows to emit it as a literal block
/// (<c>Field: |</c>) preserving newlines and indentation.
/// </summary>
public sealed record ScriptValue(string Text)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

    public override string ToString() => Text;
}
