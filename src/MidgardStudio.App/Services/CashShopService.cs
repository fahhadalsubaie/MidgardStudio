using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MidgardStudio.Core.CashShop;
using MidgardStudio.Core.Commands;
using MidgardStudio.Core.IO;
using MidgardStudio.Core.Validation;

namespace MidgardStudio.App.Services;

/// <summary>
/// Loads and saves the server cash shop (<c>item_cash_db</c>). Reads the base <c>item_cash.yml</c> plus the
/// editable <c>db/import/item_cash.yml</c>; the editor mutates the import (Custom) layer through the global
/// undo stack, and <see cref="Save"/> writes only import (atomic, via <see cref="FileTransaction"/>). Dirty is
/// a content comparison so an undo back to the loaded state reports "nothing to save" — no sticky flag.
/// </summary>
public sealed class CashShopService : IDirtySource
{
    private readonly WorkspaceSession _session;
    private readonly SchemaRegistry _schemas;
    private CashShopData? _data;
    private string? _savedSignature;
    private string? _signatureCache;
    private CashShopItemIndex? _index;
    private HashSet<string>? _knownItems;

    public CashShopService(WorkspaceSession session, SchemaRegistry schemas)
    {
        _session = session;
        _schemas = schemas;
        // A profile switch points at a different server -> drop the cached model + item index.
        _session.WorkspaceReloaded += () => { _data = null; _savedSignature = null; _signatureCache = null; _index = null; _knownItems = null; };
        // Every cash-shop edit (and any item_db edit that changes the known items) runs through the command
        // stack — invalidate the memoized signature + item index on any stack change instead of recomputing on poll.
        _session.Commands.Changed += () => { _signatureCache = null; _index = null; _knownItems = null; };
    }

    private string BasePath => Path.Combine(_session.Paths.ServerDbRoot, "item_cash.yml");
    private string ImportPath => Path.Combine(_session.Paths.ServerDbRoot, "import", "item_cash.yml");

    /// <summary>The file the next <see cref="Save"/> writes to (for the save summary).</summary>
    public string SaveTargetPath => ImportPath;

    /// <summary>The live model (base ∪ import), loaded on first access. Edits mutate <see cref="CashShopData.Custom"/>.</summary>
    public CashShopData Data
    {
        get
        {
            if (_data is null)
            {
                string? baseYaml = File.Exists(BasePath) ? File.ReadAllText(BasePath) : null;     // server YAML is UTF-8
                string? importYaml = File.Exists(ImportPath) ? File.ReadAllText(ImportPath) : null;
                _data = CashShopYaml.Load(baseYaml, importYaml);
                _savedSignature = _data.Signature(); // baseline = the just-loaded content
            }
            return _data;
        }
    }

    /// <summary>True when the in-memory import layer differs from what's on disk (content comparison, so an
    /// undo back to the loaded/saved state correctly reports nothing to save).</summary>
    public bool IsDirty => _data is not null && Signature() != _savedSignature;

    /// <summary>Forwarded from the command stack — every cash-shop edit runs through it, so
    /// <see cref="CompositeDirtyState"/> can treat this as a source.</summary>
    public event Action? DirtyChanged
    {
        add => _session.Commands.Changed += value;
        remove => _session.Commands.Changed -= value;
    }

    private string Signature() => _signatureCache ??= (_data?.Signature() ?? string.Empty);

    public void Save()
    {
        if (!IsDirty || _data is null) return;
        string dir = Path.GetDirectoryName(ImportPath)!;
        string canonical = CashShopYaml.Write(_data);

        // Preserve any banner/comments/Footer the user hand-added to import/item_cash.yml — the same
        // comment-preserving merge the other import writers use (audit sweep). Abort on an unreadable
        // existing file rather than overwrite it with a comment-less regenerate.
        string? existing = null;
        if (File.Exists(ImportPath))
        {
            try { existing = File.ReadAllText(ImportPath, new UTF8Encoding(false)); }
            catch (Exception ex)
            {
                throw new IOException(
                    "Couldn't read the existing item_cash.yml to merge your change safely, so nothing was written " +
                    "and the file is untouched. It may be open in another program — close it and save again.", ex);
            }
        }
        string text = MidgardStudio.Core.Serialization.YamlBodyMerge.Merge(existing, canonical);

        var tx = new FileTransaction(Path.Combine(dir, ".midgard-backup"));
        tx.Stage(ImportPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text));
        tx.Commit();
        _signatureCache = null;
        _savedSignature = _data.Signature(); // re-baseline: on-disk now matches memory
    }

    /// <summary>item_db index (id / Aegis name / display name) for the current mode. Powers reference
    /// resolution, autocomplete, icon + display-name lookup. Rebuilt lazily; invalidated on any edit or
    /// profile switch.</summary>
    private CashShopItemIndex Items()
    {
        if (_index is null)
        {
            var refs = new List<ItemRef>();
            if (_schemas.Get("item_db") is { } schema)
            {
                try
                {
                    foreach (var rec in _session.GetActiveOverlay(schema).Effective())
                    {
                        var aegis = rec.GetString("AegisName");
                        if (string.IsNullOrWhiteSpace(aegis)) continue;
                        refs.Add(new ItemRef(rec.GetInt("Id"), aegis, rec.GetString("Name") ?? string.Empty));
                    }
                }
                catch { /* item_db not loadable in this workspace — empty index (entries flag as unknown) */ }
            }
            _index = new CashShopItemIndex(refs);
        }
        return _index;
    }

    /// <summary>Aegis names known to item_db — the validator membership set.</summary>
    public IReadOnlySet<string> KnownItems() =>
        _knownItems ??= new HashSet<string>(Items().AegisNames, StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves a token (Aegis name, display Name, or numeric Id) to the canonical Aegis name.</summary>
    public (CashShopItemIndex.ResolveStatus Status, string? Aegis) Resolve(string? token) => Items().Resolve(token);

    /// <summary>Autocomplete matches across Aegis name / display Name / Id.</summary>
    public IReadOnlyList<ItemSuggestion> Suggest(string? query) => Items().Suggest(query);

    /// <summary>The display Name for an Aegis name (card subtitle), or null.</summary>
    public string? DisplayName(string aegisName) => Items().DisplayName(aegisName);

    /// <summary>The item id for an Aegis name, or null if item_db has no such item (icon resolution).</summary>
    public int? ItemId(string aegisName) => Items().IdOf(aegisName);

    /// <summary>Cash-shop findings (unknown item / dup-in-tab / price ≤ 0) for the global panel + save gate.</summary>
    public IReadOnlyList<ValidationIssue> Validate() => CashShopValidator.Validate(Data, KnownItems());
}
