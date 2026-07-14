using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CodexPetLimitRings.Windows.Interop;

internal static class NativeMethods
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int DwmwaCloaked = 14;

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out Rect rect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int valueSize);

    public static void MakeNoActivate(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExNoActivate | WsExToolWindow));
    }

    public static PetAnchor? FindVisiblePetAnchor(PetWindowCandidate candidate)
    {
        Rect? found = null;
        long bestScore = long.MaxValue;
        EnumWindows((handle, _) =>
        {
            if (!IsActuallyVisible(handle) || !IsCodexWindow(handle) || !GetWindowRect(handle, out var rect))
            {
                return true;
            }

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            var widthDelta = Math.Abs(width - (int)Math.Round(candidate.WindowWidth));
            var heightDelta = Math.Abs(height - (int)Math.Round(candidate.WindowHeight));
            if (widthDelta > 48 || heightDelta > 48) return true;

            var positionDelta = Math.Abs((long)rect.Left - (long)Math.Round(candidate.WindowX)) +
                                Math.Abs((long)rect.Top - (long)Math.Round(candidate.WindowY));
            var score = (long)(widthDelta + heightDelta) * 10_000 + positionDelta;
            if (score < bestScore)
            {
                bestScore = score;
                found = rect;
            }
            return true;
        }, IntPtr.Zero);

        return found is { } live
            ? new PetAnchor(
                live.Left + candidate.MascotLeft,
                live.Top + candidate.MascotTop,
                candidate.MascotWidth,
                candidate.MascotHeight,
                candidate.DisplayId)
            : null;
    }

    private static bool IsActuallyVisible(IntPtr handle)
    {
        if (!IsWindowVisible(handle) || IsIconic(handle)) return false;
        return DwmGetWindowAttribute(handle, DwmwaCloaked, out var cloaked, sizeof(int)) != 0 || cloaked == 0;
    }

    private static bool IsCodexWindow(IntPtr handle)
    {
        GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0) return false;
        try
        {
            using var process = Process.GetProcessById(unchecked((int)processId));
            if (string.Equals(process.ProcessName, "Codex", StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.Equals(process.ProcessName, "ChatGPT", StringComparison.OrdinalIgnoreCase)) return false;
            var path = process.MainModule?.FileName ?? string.Empty;
            return path.Contains("\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) &&
                   path.EndsWith("\\app\\ChatGPT.exe", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
