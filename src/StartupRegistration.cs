using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace LumiTasks.VisualPrototype;

public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovalKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "LumiTasksWPF";

    private static string GetExecutablePath()
    {
        // Environment.ProcessPath is the correct path for a self-contained
        // single-file publish. MainModule is retained as a compatibility fallback.
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
            path = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("无法确定 LumiTasks.exe 的实际路径。");
        return Path.GetFullPath(path);
    }

    private static string? ReadRegisteredCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName)?.ToString();
    }

    public static bool IsRegistered()
    {
        try
        {
            var current = GetExecutablePath();
            var registered = ReadRegisteredCommand();
            if (string.IsNullOrWhiteSpace(registered)) return false;
            var candidate = registered.Trim().Trim('"');
            return string.Equals(Path.GetFullPath(candidate), current, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void Set(bool enabled)
    {
        if (enabled)
        {
            var exe = GetExecutablePath();
            using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                   ?? throw new InvalidOperationException("无法打开当前用户的 Windows 启动注册表项。"))
            {
                key.SetValue(ValueName, $"\"{exe}\"", RegistryValueKind.String);
                key.Flush();
            }

            // Windows 10/11 may remember that a Run entry was disabled in
            // Task Manager. Remove only this app's approval record so enabling
            // the in-app switch actually re-enables it for the current user.
            using (var approval = Registry.CurrentUser.OpenSubKey(ApprovalKeyPath, writable: true))
            {
                approval?.DeleteValue(ValueName, throwOnMissingValue: false);
                approval?.Flush();
            }

            if (!IsRegistered())
                throw new InvalidOperationException("启动项写入后校验失败，请检查当前用户注册表权限。");
        }
        else
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
            {
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
                key?.Flush();
            }
            using (var approval = Registry.CurrentUser.OpenSubKey(ApprovalKeyPath, writable: true))
            {
                approval?.DeleteValue(ValueName, throwOnMissingValue: false);
                approval?.Flush();
            }
        }
    }
}
