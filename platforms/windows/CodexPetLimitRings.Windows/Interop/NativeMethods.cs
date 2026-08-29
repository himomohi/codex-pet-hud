using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace CodexPetLimitRings.Windows.Interop;

internal readonly record struct PetPointerRelayToken(
    nint TargetHandle,
    NativeMethods.NativePoint PhysicalStart,
    NativeMethods.NativePoint VirtualStart);

internal static class NativeMethods
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExLayered = 0x00080000;
    private const long WsExNoActivate = 0x08000000;
    private const long WsExToolWindow = 0x00000080;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const int VkLeftButton = 0x01;
    private const uint GaRoot = 2;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLeftButtonDown = 0x0201;
    private const uint WmLeftButtonUp = 0x0202;
    private const nuint MkLeftButton = 0x0001;
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);
    private const int DwmwaCloaked = 14;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ErrorInsufficientBuffer = 122;
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

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;

        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
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

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr handle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr handle, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr handle, uint flags);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr handle, uint message, nuint wParam, nint lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int valueSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(
        IntPtr process,
        int flags,
        StringBuilder executablePath,
        ref int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFullName(
        IntPtr process,
        ref uint packageFullNameLength,
        StringBuilder? packageFullName);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    public static void ConfigurePetInputProxy(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        style |= WsExNoActivate | WsExToolWindow;
        style &= ~WsExTransparent;
        TrySetExtendedStyle(handle, style);
        SetWindowPos(
            handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    public static void PlacePetInputProxy(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        SetWindowPos(
            handle,
            HwndTopmost,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    public static bool TryBeginPetPointerRelay(
        nint targetHandle,
        NativePoint physicalStart,
        NativePoint virtualStart,
        out PetPointerRelayToken token,
        out string diagnostic)
    {
        token = default;
        if (targetHandle == IntPtr.Zero || !IsWindowVisible(targetHandle))
        {
            diagnostic = "target window is unavailable";
            return false;
        }
        if (!TryPostScreenMouseMessage(
                targetHandle,
                WmMouseMove,
                0,
                virtualStart,
                out diagnostic))
        {
            return false;
        }
        if (!TryPostScreenMouseMessage(
                targetHandle,
                WmLeftButtonDown,
                MkLeftButton,
                virtualStart,
                out diagnostic))
        {
            return false;
        }
        token = new PetPointerRelayToken(targetHandle, physicalStart, virtualStart);
        diagnostic = $"target=0x{targetHandle.ToInt64():X}, virtual={virtualStart.X},{virtualStart.Y}";
        return true;
    }

    public static bool MovePetPointerRelay(
        PetPointerRelayToken token,
        NativePoint physicalCurrent,
        out string diagnostic)
    {
        if (token.TargetHandle == IntPtr.Zero)
        {
            diagnostic = "relay token is empty";
            return false;
        }
        var virtualCurrent = ToVirtualPoint(token, physicalCurrent);
        return TryPostScreenMouseMessage(
            token.TargetHandle,
            WmMouseMove,
            MkLeftButton,
            virtualCurrent,
            out diagnostic);
    }

    public static bool CompletePetPointerRelay(
        PetPointerRelayToken token,
        NativePoint physicalCurrent,
        out string diagnostic)
    {
        if (token.TargetHandle == IntPtr.Zero)
        {
            diagnostic = "relay token is empty";
            return false;
        }
        var virtualCurrent = ToVirtualPoint(token, physicalCurrent);
        var moved = TryPostScreenMouseMessage(
            token.TargetHandle,
            WmMouseMove,
            MkLeftButton,
            virtualCurrent,
            out var moveDiagnostic);
        var released = TryPostScreenMouseMessage(
            token.TargetHandle,
            WmLeftButtonUp,
            0,
            virtualCurrent,
            out var releaseDiagnostic);
        diagnostic = moved && released
            ? $"released at {virtualCurrent.X},{virtualCurrent.Y}"
            : $"move={moveDiagnostic}; release={releaseDiagnostic}";
        return moved && released;
    }

    public static bool TrySendPetClick(
        nint targetHandle,
        NativePoint screenPoint,
        out string diagnostic)
    {
        if (!TryBeginPetPointerRelay(
                targetHandle,
                screenPoint,
                screenPoint,
                out var token,
                out var beginDiagnostic))
        {
            diagnostic = beginDiagnostic;
            return false;
        }
        var released = CompletePetPointerRelay(token, screenPoint, out var releaseDiagnostic);
        diagnostic = released
            ? $"target=0x{targetHandle.ToInt64():X}, point={screenPoint.X},{screenPoint.Y}"
            : releaseDiagnostic;
        return released;
    }

    public static bool IsLeftButtonDown() =>
        (GetAsyncKeyState(VkLeftButton) & 0x8000) != 0;

    public static void ForwardPetHover(nint targetHandle)
    {
        if (targetHandle == IntPtr.Zero || !GetCursorPos(out var point)) return;
        TryPostScreenMouseMessage(
            targetHandle,
            WmMouseMove,
            IsLeftButtonDown() ? MkLeftButton : 0,
            point,
            out _);
    }

    internal static long GetExtendedStyle(nint handle) =>
        GetWindowLongPtr(handle, GwlExStyle).ToInt64();

    internal static nint GetRootWindowAtPoint(NativePoint point)
    {
        var hit = WindowFromPoint(point);
        if (hit == IntPtr.Zero) return IntPtr.Zero;
        var root = GetAncestor(hit, GaRoot);
        return root == IntPtr.Zero ? hit : root;
    }

    internal static bool TryGetCursorPosition(out NativePoint point) =>
        GetCursorPos(out point);

    internal static bool TrySetCursorPosition(int x, int y) =>
        SetCursorPos(x, y);

    internal static double GetDpiScaleForWindow(nint handle) =>
        GetDpiScale(handle);

    public static void ConfigurePotionInput(Window window, bool directPotionClicksEnabled)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        style |= WsExNoActivate | WsExToolWindow;
        style = directPotionClicksEnabled
            ? style & ~WsExTransparent
            : style | WsExTransparent;
        TrySetExtendedStyle(handle, style);
        SetWindowPos(
            handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    public static void PlacePotionWindow(
        Window window,
        nint petWindowHandle,
        bool directPotionClicksEnabled)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var flags = SwpNoMove | SwpNoSize | SwpNoActivate;
        if (directPotionClicksEnabled)
        {
            SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, flags);
            return;
        }

        SetWindowPos(handle, HwndNotTopmost, 0, 0, 0, 0, flags);
        if (petWindowHandle != IntPtr.Zero)
        {
            SetWindowPos(handle, petWindowHandle, 0, 0, 0, 0, flags);
        }
    }

    public static bool TryGetPhysicalWindowRect(Window window, out System.Drawing.Rectangle bounds)
    {
        bounds = System.Drawing.Rectangle.Empty;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect)) return false;
        bounds = System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static NativePoint ToVirtualPoint(
        PetPointerRelayToken token,
        NativePoint physicalCurrent) =>
        new(
            token.VirtualStart.X + physicalCurrent.X - token.PhysicalStart.X,
            token.VirtualStart.Y + physicalCurrent.Y - token.PhysicalStart.Y);

    private static bool TryPostScreenMouseMessage(
        nint targetHandle,
        uint message,
        nuint wParam,
        NativePoint screenPoint,
        out string diagnostic)
    {
        diagnostic = string.Empty;
        if (targetHandle == IntPtr.Zero || !IsWindowVisible(targetHandle))
        {
            diagnostic = "target window is unavailable";
            return false;
        }
        var clientPoint = screenPoint;
        Marshal.SetLastPInvokeError(0);
        if (!ScreenToClient(targetHandle, ref clientPoint))
        {
            diagnostic = $"ScreenToClient failed, error={Marshal.GetLastWin32Error()}";
            return false;
        }
        var packed = unchecked((nint)(
            ((clientPoint.Y & 0xFFFF) << 16) |
            (clientPoint.X & 0xFFFF)));
        Marshal.SetLastPInvokeError(0);
        if (!PostMessage(targetHandle, message, wParam, packed))
        {
            diagnostic = $"PostMessage 0x{message:X} failed, error={Marshal.GetLastWin32Error()}";
            return false;
        }
        diagnostic =
            $"message=0x{message:X}, screen={screenPoint.X},{screenPoint.Y}, " +
            $"client={clientPoint.X},{clientPoint.Y}";
        return true;
    }

    private static bool TrySetExtendedStyle(nint handle, long style)
    {
        Marshal.SetLastPInvokeError(0);
        var previous = SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
        return previous != IntPtr.Zero || Marshal.GetLastWin32Error() == 0;
    }

    private static void RestoreExtendedStyle(nint handle, long style)
    {
        TrySetExtendedStyle(handle, style);
        SetWindowPos(
            handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    public static PetAnchor? FindVisiblePetAnchor(PetWindowCandidate? candidate, out string diagnostic)
    {
        if (candidate is null)
        {
            diagnostic = "state candidate is null";
            return null;
        }
        if (candidate.DirectCoordinates)
        {
            return FindVisibleIntegratedPetAnchor(candidate, out diagnostic);
        }
        var anchor = FindVisibleCodexPetAnchor(candidate);
        diagnostic = anchor is null ? "legacy window match failed" : "legacy window match succeeded";
        return anchor;
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
                    workBottom - workY,
                    handle);
            }
            else if (score == bestScore)
            {
                ambiguous = true;
            }
            return true;
        }, IntPtr.Zero);

        return ambiguous ? null : found;
    }

    private static PetAnchor? FindVisibleIntegratedPetAnchor(PetWindowCandidate candidate, out string diagnostic)
    {
        PetAnchor? found = null;
        double smallestArea = double.MaxValue;
        var enumeratedCount = 0;
        var visibleCount = 0;
        var chatGptCount = 0;
        var codexCount = 0;
        var rectCount = 0;
        var nearbyCount = 0;
        var acceptedCount = 0;
        var closestDistance = double.MaxValue;
        var closestDescription = "none";
        EnumWindows((handle, _) =>
        {
            enumeratedCount++;
            if (!IsActuallyVisible(handle)) return true;
            visibleCount++;
            if (IsProcessNamed(handle, "ChatGPT")) chatGptCount++;
            if (!IsCodexWindow(handle)) return true;
            codexCount++;
            if (!GetWindowRect(handle, out var rect)) return true;
            rectCount++;

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
            if (pointDistance < closestDistance)
            {
                closestDistance = pointDistance;
                closestDescription =
                    $"distance={pointDistance:0.##}, rect={windowX:0.##},{windowY:0.##}," +
                    $"{windowWidth:0.##}x{windowHeight:0.##}, dpi={dpiScale:0.##}";
            }
            if (pointDistance > IntegratedCoordinateTolerance)
            {
                return true;
            }
            nearbyCount++;

            // When the pet is resized, the integrated state may expose its
            // position before it exposes the new width/height. The old check
            // rejected the containing window if the fallback rectangle was too
            // large for the remaining space, which made the HUD disappear at
            // the bottom/right edge. Clamp the estimate instead of rejecting a
            // valid coordinate; exact legacy mascot bounds remain untouched.
            var mascotWidth = Math.Min(Math.Max(1, candidate.MascotWidth), Math.Max(1, windowRight - candidate.WindowX));
            var mascotHeight = Math.Min(Math.Max(1, candidate.MascotHeight), Math.Max(1, windowBottom - candidate.WindowY));

            var area = windowWidth * windowHeight;
            // Current Codex builds may host the pet in a full-height transparent
            // sidecar rather than a small standalone overlay window. Direct state
            // coordinates already prove which point belongs to the visible pet;
            // select the smallest containing Codex window without rejecting it
            // solely because that host spans the display height.
            if (area >= smallestArea) return true;
            acceptedCount++;

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
                workBottom - workY,
                handle);
            return true;
        }, IntPtr.Zero);

        diagnostic =
            $"integrated enum={enumeratedCount}, visible={visibleCount}, chatgpt={chatGptCount}, " +
            $"codex={codexCount}, rect={rectCount}, near={nearbyCount}, accepted={acceptedCount}, " +
            $"closest={closestDescription}";
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
            if (TryGetProcessImagePath(processId, out var path) &&
                path.Contains("\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith("\\app\\ChatGPT.exe", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return TryGetPackageFullName(processId, out var packageName) &&
                   packageName.StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsProcessNamed(IntPtr handle, string expectedName)
    {
        GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0) return false;
        try
        {
            using var process = Process.GetProcessById(unchecked((int)processId));
            return string.Equals(process.ProcessName, expectedName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetProcessImagePath(uint processId, out string path)
    {
        path = string.Empty;
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero) return false;
        try
        {
            var capacity = 1024;
            var buffer = new StringBuilder(capacity);
            if (!QueryFullProcessImageName(process, 0, buffer, ref capacity)) return false;
            path = buffer.ToString();
            return !string.IsNullOrWhiteSpace(path);
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static bool TryGetPackageFullName(uint processId, out string packageName)
    {
        packageName = string.Empty;
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero) return false;
        try
        {
            uint length = 0;
            if (GetPackageFullName(process, ref length, null) != ErrorInsufficientBuffer || length == 0)
            {
                return false;
            }
            var buffer = new StringBuilder(checked((int)length));
            if (GetPackageFullName(process, ref length, buffer) != 0) return false;
            packageName = buffer.ToString();
            return !string.IsNullOrWhiteSpace(packageName);
        }
        finally
        {
            CloseHandle(process);
        }
    }
}
