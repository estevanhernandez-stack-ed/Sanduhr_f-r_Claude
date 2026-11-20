using System.Globalization;
using System.Runtime.InteropServices;

namespace Sanduhr.Core;

/// <summary>
/// The Arm build notice (5.0 spec 7). An Arm PC that installed Sanduhr from GitHub before 5.0 has the x64 build,
/// which Windows runs through emulation, and that install keeps updating from the x64 feed. This says once that a
/// native build exists. The Store needs no notice: it hands an Arm PC the arm64 package at its next update.
/// Pure: the app reads the machine and shows the notification.
/// </summary>
public static class ArmBuildNotice
{
    /// <summary>settings.json: true once the notification has been handed to Windows.</summary>
    public const string SettingsKey = "arm_build_notice_shown";

    /// <summary>The notification button's argument.</summary>
    public const string GetAction = "getArmBuild";

    public const string Title = "A native Arm build is available";
    public const string Body = "This PC has an Arm processor and this copy of Sanduhr is the x64 build. The Arm build runs without emulation.";
    public const string GetText = "Get the Arm build";
    public const string LaterText = "Not now";

    /// <summary>Settings, About, Updates: the standing line, shown for as long as the notice applies.</summary>
    public const string AboutLine = "This PC has an Arm processor and this copy of Sanduhr is the x64 build, which Windows runs through emulation.";

    /// <summary>The log line written when the notification is due. The Arm check workflow reads for it.</summary>
    public const string LogDue = "arm build notice: due";

    private const string Releases = "https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude/releases";

    /// <summary>True for an x64 build on an Arm PC that is not the Store package. A machine that could not be read
    /// (<paramref name="machine"/> null) gets no notice.</summary>
    public static bool Applies(Architecture process, Architecture? machine, bool storePackage)
        => process == Architecture.X64 && machine == Architecture.Arm64 && !storePackage;

    /// <summary>The notification shows once.</summary>
    public static bool ToastDue(bool applies, bool alreadyShown) => applies && !alreadyShown;

    /// <summary>The release list for the running major version. Windows releases are never the repository's Latest
    /// (that slot is the Mac app's), so the link filters by major. A build with no major gets the whole list.</summary>
    public static string ReleasesUrl(int major)
        => major > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{Releases}?q=v{major}&expanded=true")
            : Releases;
}
