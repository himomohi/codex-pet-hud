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
    // The integrated Codex state can lag a native window by a few frames while
    // the pet is being resized or moved. Keep matching tolerant to that small
    // coordinate drift, but never use an unrelated window far away from the pet.
    private const double IntegratedCoordinateTolerance = 48;

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
    private static extern uint GetDpiForWindow(IntPtr handle);

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

    public static PetAnchor? FindVisiblePetAnchor(PetWindowCandidate? candidate)
    {
        if (candidate is null) return null;
        return candidate.DirectCoordinates
            ? FindVisibleIntegratedPetAnchor(candidate)
            : FindVisibleCodexPetAnchor(candidate);
    }

    private static PetAnchor? FindVisibleCodexPetAnchor(PetWindowCandidate candidate)
    {
        PetAnchor? found = null;
        long bestScore = long.MaxValue;
        var ambiguous = false;
        EnumWindows((handle, _) =>
        {
            if (!IsActuallyVisible(handle) || !IsCodexWindow(handle) || !GetWindowRect(handle, out var rect))
            {
                return true;
            }

            var dpiScale = GetDpiScale(handle);
            var width = (rect.Right - rect.Left) / dpiScale;
            var height = (rect.Bottom - rect.Top) / dpiScale;
            var widthDelta = Math.Abs(width - (int)Math.Round(candidate.WindowWidth));
            var heightDelta = Math.Abs(height - (int)Math.Round(candidate.WindowHeight));
            if (widthDelta > 48 || heightDelta > 48) return true;

            var screen = System.Windows.Forms.Screen.FromHandle(handle);
            var displayX = candidate.DisplayX.GetValueOrDefault();
            var displayY = candidate.DisplayY.GetValueOrDefault();
            var hasDisplayBounds = candidate.DisplayX is not null &&
                                   candidate.DisplayY is not null &&
                                   candidate.DisplayWidth is not null &&
                                   candidate.DisplayHeight is not null;
            var liveWindowX = hasDisplayBounds
                ? displayX + (rect.Left - screen.Bounds.Left) / dpiScale
                : candidate.WindowX;
            var liveWindowY = hasDisplayBounds
                ? displayY + (rect.Top - screen.Bounds.Top) / dpiScale
                : candidate.WindowY;
            var positionDelta = Math.Abs(liveWindowX - candidate.WindowX) +
                                Math.Abs(liveWindowY - candidate.WindowY);
            var displaySizeDelta = hasDisplayBounds
                ? Math.Abs(screen.Bounds.Width / dpiScale - candidate.DisplayWidth!.Value) +
                  Math.Abs(screen.Bounds.Height / dpiScale - candidate.DisplayHeight!.Value)
                : 0;
            var score = (long)Math.Round(
                (widthDelta + heightDelta) * 10_000 +
                displaySizeDelta * 100 +
                positionDelta);
            if (score < bestScore)
            {
                bestScore = score;
                ambiguous = false;
                var work = screen.WorkingArea;
                // Electron persists the overlay and mascot bounds in DIPs. Keep that
                // coordinate space as the WPF contract, and only map native work-area
                // pixels relative to the matched window to avoid mixed-DPI origin drift.
                var workX = hasDisplayBounds
                    ? displayX + (work.Left - screen.Bounds.Left) / dpiScale
                    : candidate.WindowX + (work.Left - rect.Left) / dpiScale;
                var workY = hasDisplayBounds
                    ? displayY + (work.Top - screen.Bounds.Top) / dpiScale
                    : candidate.WindowY + (work.Top - rect.Top) / dpiScale;
                var workRight = hasDisplayBounds
                    ? displayX + (work.Right - screen.Bounds.Left) / dpiScale
                    : candidate.WindowX + (work.Right - rect.Left) / dpiScale;
                var workBottom = hasDisplayBounds
                    ? displayY + (work.Bottom - screen.Bounds.Top) / dpiScale
                    : candidate.WindowY + (work.Bottom - rect.Top) / dpiScale;
                found = new PetAnchor(
                    liveWindowX + candidate.MascotLeft,
                    liveWindowY + candidate.MascotTop,
                    candidate.MascotWidth,
                    candidate.MascotHeight,
                    candidate.DisplayId,
                    workX,
                    workY,
                    workRight - workX,
                    workBottom - workY);
            }
            else if (score == bestScore)
            {
                ambiguous = true;
            }
            return true;
        }, IntPtr.Zero);

        return ambiguous ? null : found;
    }

    private static PetAnchor? FindVisibleIntegratedPetAnchor(PetWindowCandidate candidate)
    {
        PetAnchor? found = null;
        double smallestArea = double.MaxValue;
        EnumWindows((handle, _) =>
        {
            if (!IsActuallyVisible(handle) || !IsCodexWindow(handle) || !GetWindowRect(handle, out var rect))
            {
                return true;
            }

            var dpiScale = GetDpiScale(handle);
            var screen = System.Windows.Forms.Screen.FromHandle(handle);
            var displayX = candidate.DisplayX ?? screen.Bounds.Left / dpiScale;
            var displayY = candidate.DisplayY ?? screen.Bounds.Top / dpiScale;
            var windowX = displayX + (rect.Left - screen.Bounds.Left) / dpiScale;
            var windowY = displayY + (rect.Top - screen.Bounds.Top) / dpiScale;
            var windowWidth = (rect.Right - rect.Left) / dpiScale;
            var windowHeight = (rect.Bottom - rect.Top) / dpiScale;
            var windowRight = windowX + windowWidth;
            var windowBottom = windowY + windowHeight;
            var pointDistance = DistanceOutside(candidate.WindowX, windowX, windowRight) +
                                DistanceOutside(candidate.WindowY, windowY, windowBottom);
            if (pointDistance > IntegratedCoordinateTolerance)
            {
                return true;
            }

            // When the pet is resized, the integrated state may expose its
            // position before it exposes the new width/height. The old check
            // rejected the containing window if the fallback rectangle was too
            // large for the remaining space, which made the HUD disappear at
            // the bottom/right edge. Clamp the estimate instead of rejecting a
            // valid coordinate; exact legacy mascot bounds remain untouched.
            var mascotWidth = Math.Min(Math.Max(1, candidate.MascotWidth), Math.Max(1, windowRight - candidate.WindowX));
            var mascotHeight = Math.Min(Math.Max(1, candidate.MascotHeight), Math.Max(1, windowBottom - candidate.WindowY));

            var area = windowWidth * windowHeight;
            if (area >= smallestArea ||
                windowWidth > Math.Max(600, mascotWidth * 6) ||
                windowHeight > Math.Max(600, mascotHeight * 6))
            {
                return true;
            }

            var work = screen.WorkingArea;
            var workX = displayX + (work.Left - screen.Bounds.Left) / dpiScale;
            var workY = displayY + (work.Top - screen.Bounds.Top) / dpiScale;
            var workRight = displayX + (work.Right - screen.Bounds.Left) / dpiScale;
            var workBottom = displayY + (work.Bottom - screen.Bounds.Top) / dpiScale;
            smallestArea = area;
            found = new PetAnchor(
                candidate.WindowX,
                candidate.WindowY,
                mascotWidth,
                mascotHeight,
                candidate.DisplayId,
                workX,
                workY,
                workRight - workX,
                workBottom - workY);
            return true;
        }, IntPtr.Zero);

        return found;
    }

    private static double DistanceOutside(double value, double minimum, double maximum)
    {
        if (value < minimum) return minimum - value;
        if (value > maximum) return value - maximum;
        return 0;
    }

    private static double GetDpiScale(IntPtr handle)
    {
        var dpi = GetDpiForWindow(handle);
        return dpi > 0 ? dpi / 96.0 : 1;
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
