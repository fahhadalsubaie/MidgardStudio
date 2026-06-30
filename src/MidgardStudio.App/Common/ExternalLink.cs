using System;
using System.Diagnostics;

namespace MidgardStudio.App.Common;

/// <summary>
/// Opens an external link in the user's default browser — but ONLY when it is an http/https URL. The update
/// feed hands us a server-supplied <c>html_url</c>; launching it with <c>UseShellExecute=true</c> would let a
/// non-web scheme (file://, a UNC path, an .exe) be executed by the shell, so the scheme is validated first and
/// a trusted fallback (the fixed releases page) is used when the primary link isn't a safe web URL.
/// </summary>
public static class ExternalLink
{
    public static void Open(string? url, string? fallback = null)
    {
        string? target = AsWebUrl(url) ?? AsWebUrl(fallback);
        if (target is null) { Serilog.Log.Warning("Refused to open a non-http(s) link: {Url}", url); return; }
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Could not open {Url}", target); }
    }

    private static string? AsWebUrl(string? u) =>
        Uri.TryCreate(u, UriKind.Absolute, out var x) && (x.Scheme == Uri.UriSchemeHttp || x.Scheme == Uri.UriSchemeHttps)
            ? x.AbsoluteUri
            : null;
}
