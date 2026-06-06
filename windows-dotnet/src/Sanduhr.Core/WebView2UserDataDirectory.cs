namespace Sanduhr.Core;

/// <summary>
/// Per-capture user-data folder allocator for the embedded WebView2 "Sign in to
/// Claude" window. Ported 1:1 from ROROROblox's <c>WebView2UserDataDirectory</c>;
/// the only deviation is dropping the <c>ILogger</c> dependency so
/// <see cref="Sanduhr.Core"/> stays dep-free (spec: "Core packages: none heavy").
/// Sweep failures are swallowed instead of logged — a leftover dir is harmless on
/// its own; what matters is that we never *reuse* one for the next capture.
///
/// Each <see cref="AllocateNew"/> returns a fresh GUID-named subdirectory under
/// <see cref="Root"/> (the app-owned <c>%APPDATA%\Sanduhr\webview2\</c> profile,
/// isolated from the user's Chrome/Edge). A fresh dir per sign-in means a second
/// "Add account" never boots WebView2 against the previous capture's session
/// state — the user always lands on a logged-out claude.ai/login, so account #2's
/// <c>sessionKey</c> is captured rather than account #1's being re-read.
///
/// Why per-capture and not a single persistent profile: msedgewebview2.exe child
/// processes outlive the WPF window that owned them. Wiping one shared dir on each
/// capture races those still-draining children (IOException on pinned files) and
/// silently leaves stale auth cookies behind. Per-capture dirs sidestep the
/// lifecycle race entirely — exactly the trap ROROROblox hit and fixed.
/// </summary>
public sealed class WebView2UserDataDirectory
{
    private readonly string _root;

    public WebView2UserDataDirectory(string root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

    /// <summary>Root path that holds per-capture subdirectories
    /// (<c>%APPDATA%\Sanduhr\webview2\</c> in production).</summary>
    public string Root => _root;

    /// <summary>
    /// Allocate a fresh per-capture user-data folder under <see cref="Root"/>.
    /// Creates the directory eagerly so the caller can hand the absolute path to
    /// <c>CoreWebView2Environment.CreateAsync</c>.
    /// </summary>
    public string AllocateNew()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Best-effort delete of every sibling subdirectory under <see cref="Root"/>,
    /// optionally excluding one (typically the dir just allocated for the current
    /// capture). Failures are swallowed — a leftover dir gets swept on the next
    /// capture or app start once the stale msedgewebview2.exe handles release.
    /// </summary>
    public void SweepStale(string? exclude = null)
    {
        if (!Directory.Exists(_root))
            return;

        string? excludeFull = exclude is null ? null : Path.GetFullPath(exclude);

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(_root);
        }
        catch (Exception)
        {
            return;
        }

        foreach (var dir in children)
        {
            if (excludeFull is not null
                && string.Equals(Path.GetFullPath(dir), excludeFull, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // Stale handles from a still-draining msedgewebview2.exe child —
                // exactly the case this class exists to make harmless.
            }
            catch (UnauthorizedAccessException)
            {
                // Access denied; leftover dir is harmless, retry next sweep.
            }
        }
    }
}
