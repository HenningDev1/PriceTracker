using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PriceTrackerSetup;

/// Per-user install (no admin rights): %LOCALAPPDATA%\Programs\Price Tracker,
/// Start menu (+ optional desktop) shortcut, and an entry in Settings › Apps with a working uninstaller.
public static class SetupEngine
{
    public const string AppName = "Price Tracker";
    public const string Version = "1.0.0";
    public const string Publisher = "HenningDev";
    private const string ExeName = "PriceTracker.exe";
    private const string UninstallerName = "Uninstall Price Tracker.exe";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\HenningDev.PriceTracker";

    public static string InstallDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
    public static string ExePath => Path.Combine(InstallDir, ExeName);
    private static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");
    private static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");
    public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PriceTracker");
    private static string CacheDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PriceTracker");

    public static bool IsInstalled => File.Exists(ExePath);

    public static string? InstalledVersion
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            return key?.GetValue("DisplayVersion") as string;
        }
    }

    public static bool HasDesktopShortcut => File.Exists(DesktopLink);

    /// True when this exe is the copy inside the install folder (i.e. started from Settings › Apps).
    public static bool RunningFromInstallDir =>
        string.Equals(Path.GetDirectoryName(Environment.ProcessPath), InstallDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    public static void Install(bool desktopShortcut, IProgress<(double, string)> progress)
    {
        progress.Report((0.02, "Closing Price Tracker…"));
        StopRunningApp();

        progress.Report((0.06, "Removing the previous version…"));
        if (Directory.Exists(InstallDir))
        {
            foreach (var f in Directory.EnumerateFiles(InstallDir))
                if (!string.Equals(f, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) TryDelete(f);
            foreach (var d in Directory.EnumerateDirectories(InstallDir)) Directory.Delete(d, recursive: true);
        }
        Directory.CreateDirectory(InstallDir);

        using (var payload = typeof(SetupEngine).Assembly.GetManifestResourceStream("payload.zip")
                             ?? throw new InvalidOperationException("The installer payload is missing. Build it with scripts\\build-release.ps1."))
        using (var zip = new ZipArchive(payload))
        {
            var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var target = Path.GetFullPath(Path.Combine(InstallDir, e.FullName));
                if (!target.StartsWith(InstallDir, StringComparison.OrdinalIgnoreCase)) continue; // zip-slip guard
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                e.ExtractToFile(target, overwrite: true);
                if (i % 8 == 0) progress.Report((0.08 + 0.80 * i / entries.Count, $"Copying files… {i + 1}/{entries.Count}"));
            }
        }

        progress.Report((0.90, "Creating shortcuts…"));
        // The setup exe doubles as the uninstaller.
        var uninstaller = Path.Combine(InstallDir, UninstallerName);
        if (!string.Equals(Environment.ProcessPath, uninstaller, StringComparison.OrdinalIgnoreCase))
            File.Copy(Environment.ProcessPath!, uninstaller, overwrite: true);

        CreateShortcut(StartMenuLink, ExePath, "Track Steam, key and hardware prices");
        if (desktopShortcut) CreateShortcut(DesktopLink, ExePath, "Track Steam, key and hardware prices");
        else TryDelete(DesktopLink);

        progress.Report((0.96, "Registering with Windows…"));
        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", AppName);
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", Publisher);
            key.SetValue("DisplayIcon", ExePath + ",0");
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("UninstallString", $"\"{uninstaller}\" /uninstall");
            key.SetValue("QuietUninstallString", $"\"{uninstaller}\" /uninstall /quiet");
            key.SetValue("URLInfoAbout", "https://github.com/HenningDev1/PriceTracker");
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)(DirSize(InstallDir) / 1024), RegistryValueKind.DWord);
        }
        progress.Report((1.0, "Done"));
    }

    public static void Uninstall(bool removeData, IProgress<(double, string)> progress)
    {
        progress.Report((0.1, "Closing Price Tracker…"));
        StopRunningApp();
        progress.Report((0.3, "Removing shortcuts…"));
        TryDelete(StartMenuLink);
        TryDelete(DesktopLink);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);

        progress.Report((0.5, "Removing program files…"));
        if (Directory.Exists(InstallDir))
        {
            foreach (var f in Directory.EnumerateFiles(InstallDir, "*", SearchOption.AllDirectories))
                if (!string.Equals(f, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) TryDelete(f);
            foreach (var d in Directory.EnumerateDirectories(InstallDir))
                try { Directory.Delete(d, recursive: true); } catch { }
        }
        if (removeData)
        {
            progress.Report((0.8, "Removing your data…"));
            try { if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true); } catch { }
            try { if (Directory.Exists(CacheDir)) Directory.Delete(CacheDir, true); } catch { }
        }

        // This exe may live inside the install folder; a short-lived cmd removes the rest after we exit.
        if (RunningFromInstallDir || Directory.Exists(InstallDir))
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 3 /nobreak >nul & rmdir /s /q \"{InstallDir}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetTempPath(),
            });
        }
        progress.Report((1.0, "Done"));
    }

    public static void Launch()
    {
        if (File.Exists(ExePath))
            Process.Start(new ProcessStartInfo(ExePath) { UseShellExecute = true, WorkingDirectory = InstallDir });
    }

    private static void StopRunningApp()
    {
        foreach (var p in Process.GetProcessesByName("PriceTracker"))
        {
            try
            {
                var file = p.MainModule?.FileName;
                if (file == null || FileVersionInfo.GetVersionInfo(file).ProductName != AppName) continue;
                p.CloseMainWindow();
                if (!p.WaitForExit(3000)) { p.Kill(); p.WaitForExit(5000); }
            }
            catch { /* not ours, or already gone */ }
        }
    }

    private static void CreateShortcut(string lnk, string target, string description)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic sc = shell.CreateShortcut(lnk);
        sc.TargetPath = target;
        sc.WorkingDirectory = Path.GetDirectoryName(target);
        sc.IconLocation = target + ",0";
        sc.Description = description;
        sc.Save();
        Marshal.FinalReleaseComObject(sc);
        Marshal.FinalReleaseComObject(shell);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static long DirSize(string dir) =>
        new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
}
