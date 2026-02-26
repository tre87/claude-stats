using System.Diagnostics;

namespace ClaudeStats.Console.Browser;

public static class ScreenHelper
{
    public static (int x, int y) GetCenteredWindowPosition(int windowWidth, int windowHeight)
    {
        try
        {
            var (screenW, screenH) = GetScreenResolution();
            var x = Math.Max(0, (screenW - windowWidth) / 2);
            var y = Math.Max(0, (screenH - windowHeight) / 2);
            return (x, y);
        }
        catch
        {
            return (10, 10);
        }
    }

    private static (int width, int height) GetScreenResolution()
    {
        var process = new Process();
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        if (OperatingSystem.IsMacOS())
        {
            process.StartInfo.FileName = "osascript";
            process.StartInfo.Arguments = """
                                          -l JavaScript -e "ObjC.import('AppKit'); var f = $.NSScreen.mainScreen.frame; f.size.width + ' ' + f.size.height;"
                                          """;
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            var parts = output.Split(' ');
            if (parts.Length == 2)
            {
                return ((int)double.Parse(parts[0]), (int)double.Parse(parts[1]));
            }
        }
        else if (OperatingSystem.IsWindows())
        {
            process.StartInfo.FileName = "cmd";
            process.StartInfo.Arguments = "/c wmic path Win32_VideoController get CurrentHorizontalResolution,CurrentVerticalResolution /format:value";
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            int w = 0, h = 0;
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("CurrentHorizontalResolution="))
                {
                    w = int.Parse(trimmed.Split('=')[1]);
                }
                else if (trimmed.StartsWith("CurrentVerticalResolution="))
                {
                    h = int.Parse(trimmed.Split('=')[1]);
                }
            }

            if (w > 0 && h > 0)
            {
                return (w, h);
            }
        }

        return (1920, 1080);
    }
}