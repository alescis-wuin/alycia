using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Alicia.Desktop.Accessibility;

internal static partial class ReducedMotionPreference
{
    private const uint SpiGetClientAreaAnimation = 0x1042;

    public static bool IsEnabled()
    {
        if (TryReadOverride(out bool overridden))
        {
            return overridden;
        }

        if (OperatingSystem.IsWindows())
        {
            return ReadWindowsPreference();
        }

        if (OperatingSystem.IsMacOS())
        {
            return ReadBooleanCommand(
                "/usr/bin/defaults",
                ["read", "com.apple.universalaccess", "reduceMotion"])
                ?? false;
        }

        if (OperatingSystem.IsLinux())
        {
            bool? animationsEnabled = ReadBooleanCommand(
                "gsettings",
                ["get", "org.gnome.desktop.interface", "enable-animations"]);
            return animationsEnabled is bool enabled && !enabled;
        }

        return false;
    }

    private static bool TryReadOverride(out bool value)
    {
        string? raw = Environment.GetEnvironmentVariable("ALICIA_REDUCED_MOTION");

        if (string.Equals(raw, "1", StringComparison.Ordinal)
            || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (string.Equals(raw, "0", StringComparison.Ordinal)
            || string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "no", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    private static bool ReadWindowsPreference()
    {
        int animationsEnabled = 1;
        int result = SystemParametersInfo(
            SpiGetClientAreaAnimation,
            uiParam: 0,
            ref animationsEnabled,
            fWinIni: 0);
        return result != 0 && animationsEnabled == 0;
    }

    private static bool? ReadBooleanCommand(
        string command,
        IReadOnlyList<string> arguments)
    {
        try
        {
            ProcessStartInfo startInfo = new(command)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process? process = Process.Start(startInfo);

            if (process is null)
            {
                return null;
            }

            if (!process.WaitForExit(milliseconds: 600))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            if (process.ExitCode != 0)
            {
                return null;
            }

            string value = process.StandardOutput.ReadToEnd().Trim();

            if (bool.TryParse(value, out bool parsed))
            {
                return parsed;
            }

            return value switch
            {
                "1" => true,
                "0" => false,
                _ => null,
            };
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static partial int SystemParametersInfo(
        uint uiAction,
        uint uiParam,
        ref int pvParam,
        uint fWinIni);
}
