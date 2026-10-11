namespace Sanduhr.Core;

/// <summary>One package the Windows app ships with: its name, its license as its NuGet package
/// declares it, and the project's page.</summary>
public sealed record ThirdPartyPackage(string Name, string License, string Url);

/// <summary>
/// Settings, About, Third-party notices: every package the app ships, with the license its NuGet
/// package declares. Build-only packages (PrivateAssets all, such as CsWin32) ship nothing and are not
/// listed. <c>ThirdPartyNoticesTests</c> checks this list against the App's PackageReferences.
/// </summary>
public static class ThirdPartyNotices
{
    public static IReadOnlyList<ThirdPartyPackage> Packages { get; } =
    [
        new("CommunityToolkit.Mvvm", "MIT", "https://github.com/CommunityToolkit/dotnet"),
        new("Hardcodet.NotifyIcon.Wpf", "MIT", "https://github.com/hardcodet/wpf-notifyicon"),
        new("Microsoft.Toolkit.Uwp.Notifications", "MIT", "https://github.com/CommunityToolkit/WindowsCommunityToolkit"),
        new("Microsoft.Web.WebView2", "BSD-style (Microsoft)", "https://aka.ms/webview"),
        new("Velopack", "MIT", "https://github.com/velopack/velopack"),
        new("WPF-UI", "MIT", "https://github.com/lepoco/wpfui"),
    ];
}
