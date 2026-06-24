using MidgardStudio.Core.Model;
using MidgardStudio.Core.Overlay;

namespace MidgardStudio.Core.Validation;

public enum ValidationSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>A single advisory finding. Never blocks editing or saving.</summary>
public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string DbId,
    string Key,
    string? Field,
    string Message);

/// <summary>Validates one database's overlay, producing advisory issues.</summary>
public interface IDbValidator
{
    string DbId { get; }

    IEnumerable<ValidationIssue> Validate(OverlayTable table);
}

/// <summary>Aggregates per-database validators and runs the matching one against an overlay.</summary>
public sealed class ValidationService
{
    private readonly Dictionary<string, IDbValidator> _validators = new(StringComparer.Ordinal);

    public ValidationService Register(IDbValidator validator)
    {
        _validators[validator.DbId] = validator;
        return this;
    }

    public IReadOnlyList<ValidationIssue> Validate(OverlayTable table)
    {
        if (_validators.TryGetValue(table.Schema.Id, out var validator))
            return validator.Validate(table).ToList();
        return Array.Empty<ValidationIssue>();
    }
}

/// <summary>Baseline item checks: required identity fields on authored (custom/overridden) entries.
/// Richer cross-file rules are added in the cross-file validator (Phase 11).</summary>
public sealed class ItemValidator : IDbValidator
{
    public string DbId => "item_db";

    public IEnumerable<ValidationIssue> Validate(OverlayTable table)
    {
        foreach (var record in table.Effective())
        {
            if (record.Origin == RecordOrigin.Base)
                continue; // core data is not the user's concern

            string key = record.Key.ToString();

            if (string.IsNullOrWhiteSpace(record.GetString("AegisName")))
                yield return new ValidationIssue(ValidationSeverity.Error, DbId, key, "AegisName", "Aegis Name is required.");

            if (string.IsNullOrWhiteSpace(record.GetString("Name")))
                yield return new ValidationIssue(ValidationSeverity.Warning, DbId, key, "Name", "Display Name is empty.");

            if (string.IsNullOrWhiteSpace(record.GetString("Type")))
                yield return new ValidationIssue(ValidationSeverity.Warning, DbId, key, "Type", "Type is not set (defaults to Etc).");
        }
    }
}
