namespace Sanduhr.Core;

/// <summary>One package the Windows app ships with: its name, its license as its NuGet package
/// declares it, and the project's page.</summary>
public sealed record ThirdPartyPackage(string Name, string License, string Url);

/// <summary>One font family that ships with the app: its family name, its files, and the copyright
/// and license records in the fonts' own name tables.</summary>
public sealed record ThirdPartyFont(string Name, IReadOnlyList<string> Files, string Copyright, string License);

/// <summary>
/// Settings, About, Third-party notices: every package the App references directly, with the
/// license its NuGet package declares, and the bundled fonts with their own copyright and license
/// records. Build-only packages (PrivateAssets all, such as CsWin32) ship
/// nothing and are not listed; the packages these depend on are not listed either. <c>ThirdPartyNoticesTests</c> checks this list against the App's PackageReferences
/// and the fonts against the font files.
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

    private const string FontCopyright = "Copyright (c) 2009-2026 Estevan Hernandez / 626Labs LLC. All rights reserved.";
    private const string FontLicense = "Proprietary. Licensed only for use by 626Labs LLC and Estevan Hernandez. No redistribution, resale or modification without written permission from 626Labs LLC.";

    public static IReadOnlyList<ThirdPartyFont> Fonts { get; } =
    [
        new("EsteFont Pro", ["EsteFontPro-Regular.ttf", "EsteFontPro-Bold.ttf"], FontCopyright, FontLicense),
        new("EsteFont 26", ["EsteFont26-Regular.ttf", "EsteFont26-Bold.ttf"], FontCopyright, FontLicense),
    ];
}
