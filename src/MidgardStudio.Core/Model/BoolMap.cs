using System;
using System.Collections.Generic;

namespace MidgardStudio.Core.Model;

/// <summary>
/// A bool-map field value (item_db <c>Jobs</c>/<c>Classes</c>, mob <c>Modes</c>, …). The set itself holds the
/// tokens set to <c>true</c> (included); <see cref="Excluded"/> holds the tokens explicitly set to
/// <c>false</c>. Excluded tokens express rAthena's "all-except" pattern (<c>All: true</c> + <c>Token: false</c>
/// = all jobs/classes except <c>Token</c>) and are preserved for round-trip.
/// <para>Because it derives from <see cref="HashSet{T}"/>, every existing consumer that reads the value as an
/// <c>ISet&lt;string&gt;</c> (via <c>DbRecord.GetSet</c>) transparently sees the included tokens — unchanged.
/// Only the YAML reader/writer, the clone path and the chip editor are <c>BoolMap</c>-aware.</para>
/// </summary>
public sealed class BoolMap : HashSet<string>
{
    public BoolMap() : base(StringComparer.Ordinal) { }
    public BoolMap(IEnumerable<string> included) : base(included, StringComparer.Ordinal) { }

    /// <summary>Tokens explicitly disabled — written as <c>token: false</c>.</summary>
    public HashSet<string> Excluded { get; } = new(StringComparer.Ordinal);

    public bool HasContent => Count > 0 || Excluded.Count > 0;
}
