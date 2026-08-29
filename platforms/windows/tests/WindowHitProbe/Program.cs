using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

var assertPetProxy = args.Any(argument =>
    string.Equals(argument, "--assert-pet-proxy", StringComparison.OrdinalIgnoreCase));
var assertLiveRelayStyle = args.Any(argument =>
    string.Equals(argument, "--assert-live-relay-style", StringComparison.OrdinalIgnoreCase));
var postMessageDrag = args.Any(argument =>
    string.Equals(argument, "--post-message-drag", StringComparison.OrdinalIgnoreCase));
var unifiedSurface = ReadStringOption(args, "--unified-surface=");
var allowClamp = args.Any(argument =>
    string.Equals(argument, "--allow-clamp", StringComparison.OrdinalIgnoreCase));
var dragDx = ReadOption(args, "--drag-dx=", -80);
var dragDy = ReadOption(args, "--drag-dy=", -60);
var coordinateArgs = args
    .Where(argument => !argument.StartsWith("--", StringComparison.Ordinal))
    .ToArray();
var anchor = coordinateArgs.Length == 4
    ? new ProbeRect(
        int.Parse(coordinateArgs[0]),
        int.Parse(coordinateArgs[1]),
        int.Parse(coordinateArgs[2]),
        int.Parse(coordinateArgs[3]))
    : ReadPetAnchor();

Console.WriteLine($"pet_anchor={anchor.X},{anchor.Y},{anchor.Width}x{anchor.Height}");

var samples = new Dictionary<string, ProbePoint>
{
    ["top-left"] = new(anchor.X + 8, anchor.Y + 8),
    ["top-center"] = new(anchor.X + anchor.Width / 2, anchor.Y + 8),
    ["top-right"] = new(anchor.Right - 9, anchor.Y + 8),
    ["center-left"] = new(anchor.X + 8, anchor.Y + anchor.Height / 2),
    ["center"] = new(anchor.X + anchor.Width / 2, anchor.Y + anchor.Height / 2),
    ["center-right"] = new(anchor.Right - 9, anchor.Y + anchor.Height / 2),
    ["bottom-left"] = new(anchor.X + 8, anchor.Bottom - 9),
    ["bottom-center"] = new(anchor.X + anchor.Width / 2, anchor.Bottom - 9),
    ["bottom-right"] = new(anchor.Right - 9, anchor.Bottom - 9),
    ["voice-below"] = new(anchor.X + anchor.Width / 2, anchor.Bottom + 14)
};

foreach (var sample in samples)
{
    Console.WriteLine(DescribePoint(sample.Key, sample.Value));
}

var owners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
for (var y = anchor.Y + 4; y < anchor.Bottom; y += 12)
{
    for (var x = anchor.X + 4; x < anchor.Right; x += 12)
    {
        var owner = DescribeOwner(new ProbePoint(x, y));
        owners[owner] = owners.GetValueOrDefault(owner) + 1;
    }
}

Console.WriteLine("pet_grid_owners:");
foreach (var owner in owners.OrderByDescending(item => item.Value).ThenBy(item => item.Key))
{
    Console.WriteLine($"  {owner.Value,3} {owner.Key}");
}

Console.WriteLine("overlapping_top_level_windows:");
var overlapping = new List<string>();
var topLevelWindows = new List<IntPtr>();
NativeMethods.EnumWindows((handle, _) =>
{
    topLevelWindows.Add(handle);
    if (!NativeMethods.IsWindowVisible(handle) ||
        !NativeMethods.GetWindowRect(handle, out var rect) ||
        rect.Right <= anchor.X ||
        rect.Left >= anchor.Right ||
        rect.Bottom <= anchor.Y ||
        rect.Top >= anchor.Bottom)
    {
        return true;
    }

    overlapping.Add(DescribeWindow(handle));
    return true;
}, IntPtr.Zero);

foreach (var window in overlapping.OrderBy(value => value))
{
    Console.WriteLine($"  {window}");
}

var petHost = topLevelWindows.FirstOrDefault(handle =>
    NativeMethods.IsWindowVisible(handle) &&
    WindowContains(handle, new ProbePoint(anchor.X + anchor.Width / 2, anchor.Y + anchor.Height / 2)) &&
    GetProcessName(GetProcessId(handle)) is "ChatGPT" or "Codex");
var petHostZ = petHost == IntPtr.Zero ? -1 : topLevelWindows.IndexOf(petHost);
Console.WriteLine(
    petHost == IntPtr.Zero
        ? "pet_host=not-found"
        : $"pet_host=z{petHostZ}; {DescribeWindow(petHost)}");

Console.WriteLine("live_hud_windows:");
var hudWindows = topLevelWindows
    .Where(handle =>
        NativeMethods.IsWindowVisible(handle) &&
        string.Equals(
            GetProcessName(GetProcessId(handle)),
            "CodexPetLimitRings",
            StringComparison.OrdinalIgnoreCase))
    .ToList();
foreach (var handle in hudWindows)
{
    NativeMethods.GetWindowRect(handle, out var rect);
    var center = new ProbePoint(
        rect.Left + (rect.Right - rect.Left) / 2,
        rect.Top + (rect.Bottom - rect.Top) / 2);
    var z = topLevelWindows.IndexOf(handle);
    var hit = NativeMethods.SendNcHitTest(handle, center);
    var relation = petHostZ < 0
        ? "pet-host-unknown"
        : z > petHostZ ? "behind-pet" : "above-pet";
    Console.WriteLine($"  z={z} relation={relation} center_hit={hit}; {DescribeWindow(handle)}");
}
if (hudWindows.Count == 0) Console.WriteLine("  none");

if (unifiedSurface is not null)
{
    var proxy = hudWindows.SingleOrDefault(handle =>
        string.Equals(
            NativeMethods.ReadWindowTitle(handle),
            "CodexPetInputProxy",
            StringComparison.Ordinal));
    var potions = hudWindows
        .Where(handle => handle != proxy)
        .Select(handle =>
        {
            NativeMethods.GetWindowRect(handle, out var rect);
            return (Handle: handle, Rect: rect);
        })
        .OrderBy(item => item.Rect.Left)
        .ToArray();
    if (proxy == IntPtr.Zero || potions.Length != 2)
    {
        throw new InvalidOperationException(
            $"ASSERT FAIL: unified HUD needs one pet proxy and two potions; " +
            $"proxy=0x{proxy.ToInt64():X}, potions={potions.Length}.");
    }

    var target = unifiedSurface.ToLowerInvariant() switch
    {
        "pet" => proxy,
        "left-potion" => potions[0].Handle,
        "right-potion" => potions[1].Handle,
        _ => throw new InvalidOperationException(
            $"Unknown unified drag surface: {unifiedSurface}.")
    };
    var beforeRects = hudWindows.ToDictionary(
        handle => handle,
        handle =>
        {
            NativeMethods.GetWindowRect(handle, out var rect);
            return rect;
        });
    var initialSavedBounds = ReadSavedBounds();
    var targetRect = beforeRects[target];
    var start = new ProbePoint(
        targetRect.Left + (targetRect.Right - targetRect.Left) / 2,
        targetRect.Top + (targetRect.Bottom - targetRect.Top) / 2);
    var end = new ProbePoint(start.X + dragDx, start.Y + dragDy);
    var initialHit = NativeMethods.GetRootAtPoint(start);
    if (initialHit != target)
    {
        throw new InvalidOperationException(
            $"ASSERT FAIL: {unifiedSurface} is not clickable: " +
            $"expected=0x{target.ToInt64():X}, actual=0x{initialHit.ToInt64():X}.");
    }

    NativeMethods.GetCursorPos(out var savedCursor);
    try
    {
        NativeMethods.EnsureCursorPosition(start);
        Thread.Sleep(60);
        NativeMethods.EnsureLeftButton(down: true);
        Thread.Sleep(80);
        for (var step = 1; step <= 8; step++)
        {
            NativeMethods.EnsureCursorPosition(new ProbePoint(
                start.X + (end.X - start.X) * step / 8,
                start.Y + (end.Y - start.Y) * step / 8));
            Thread.Sleep(35);
        }
        NativeMethods.EnsureLeftButton(down: false);
        Thread.Sleep(1200);
    }
    finally
    {
        if (NativeMethods.IsLeftButtonDown()) NativeMethods.EnsureLeftButton(down: false);
        NativeMethods.EnsureCursorPosition(new ProbePoint(savedCursor.X, savedCursor.Y));
    }

    var finalSavedBounds = ReadSavedBounds();
    var stateDx = finalSavedBounds.X - initialSavedBounds.X;
    var stateDy = finalSavedBounds.Y - initialSavedBounds.Y;
    var movedWindows = new List<string>();
    foreach (var handle in hudWindows)
    {
        if (!NativeMethods.GetWindowRect(handle, out var after))
            throw new InvalidOperationException(
                $"ASSERT FAIL: HUD window disappeared: 0x{handle.ToInt64():X}.");
        var before = beforeRects[handle];
        var movedX = after.Left - before.Left;
        var movedY = after.Top - before.Top;
        movedWindows.Add($"0x{handle.ToInt64():X}={movedX},{movedY}");
        var mustTrackStateExactly = !allowClamp || handle == proxy;
        if (mustTrackStateExactly &&
            (Math.Abs(movedX - stateDx) > 3 || Math.Abs(movedY - stateDy) > 3))
        {
            throw new InvalidOperationException(
                $"ASSERT FAIL: HUD window 0x{handle.ToInt64():X} did not move with the pet: " +
                $"window={movedX},{movedY}, state={stateDx},{stateDy}.");
        }
        if (allowClamp &&
            (after.Left < 0 || after.Top < 0 || after.Right > 1920 || after.Bottom > 1080))
        {
            throw new InvalidOperationException(
                $"ASSERT FAIL: clamped HUD window escaped the display: " +
                $"0x{handle.ToInt64():X}={after.Left},{after.Top}," +
                $"{after.Right - after.Left}x{after.Bottom - after.Top}.");
        }
        var center = new ProbePoint(
            after.Left + (after.Right - after.Left) / 2,
            after.Top + (after.Bottom - after.Top) / 2);
        var finalHit = NativeMethods.GetRootAtPoint(center);
        if (finalHit != handle)
        {
            throw new InvalidOperationException(
                $"ASSERT FAIL: moved HUD window is no longer clickable: " +
                $"expected=0x{handle.ToInt64():X}, actual=0x{finalHit.ToInt64():X}.");
        }
    }
    if (!allowClamp &&
        (Math.Abs(stateDx - dragDx) > 3 || Math.Abs(stateDy - dragDy) > 3))
    {
        throw new InvalidOperationException(
            $"ASSERT FAIL: {unifiedSurface} drag moved the pet by {stateDx},{stateDy}, " +
            $"expected {dragDx},{dragDy}.");
    }
    if (allowClamp &&
        (finalSavedBounds.X < 0 ||
         finalSavedBounds.Y < 0 ||
         finalSavedBounds.X >= 1920 ||
         finalSavedBounds.Y >= 1080))
    {
        throw new InvalidOperationException(
            $"ASSERT FAIL: clamped pet bounds escaped the display: " +
            $"{finalSavedBounds.X},{finalSavedBounds.Y},{anchor.Width}x{anchor.Height}.");
    }

    Console.WriteLine(
        $"ASSERT PASS: unified {unifiedSurface} drag moved pet state and all three HUD " +
        $"windows by {stateDx},{stateDy}; clickable-after-move=true; " +
        $"windows=[{string.Join("; ", movedWindows)}].");
}

if (postMessageDrag)
{
    if (petHost == IntPtr.Zero)
        throw new InvalidOperationException("ASSERT FAIL: live pet host was not found.");
    if (!NativeMethods.GetWindowRect(petHost, out var petHostRect))
        throw new InvalidOperationException("ASSERT FAIL: live pet host bounds were unavailable.");

    var start = new ProbePoint(
        anchor.X + anchor.Width / 2,
        anchor.Y + anchor.Height / 2);
    var end = new ProbePoint(start.X + dragDx, start.Y + dragDy);
    var initialSavedBounds = ReadSavedBounds();

    NativeMethods.PostScreenMouseMove(petHost, start, leftButtonDown: false);
    Thread.Sleep(40);
    NativeMethods.PostScreenLeftButtonDown(petHost, start);
    Thread.Sleep(80);
    for (var step = 1; step <= 8; step++)
    {
        var point = new ProbePoint(
            start.X + (end.X - start.X) * step / 8,
            start.Y + (end.Y - start.Y) * step / 8);
        NativeMethods.PostScreenMouseMove(petHost, point, leftButtonDown: true);
        Thread.Sleep(25);
    }
    NativeMethods.PostScreenLeftButtonUp(petHost, end);
    Thread.Sleep(800);

    var finalSavedBounds = ReadSavedBounds();
    var movedX = finalSavedBounds.X - initialSavedBounds.X;
    var movedY = finalSavedBounds.Y - initialSavedBounds.Y;
    Console.WriteLine(
        $"post_message_drag saved={initialSavedBounds.X},{initialSavedBounds.Y}" +
        $"->{finalSavedBounds.X},{finalSavedBounds.Y}; moved={movedX},{movedY}");
    if (Math.Abs(movedX) < 20 || Math.Abs(movedY) < 20)
    {
        throw new InvalidOperationException(
            $"ASSERT FAIL: posted mouse drag did not move persisted pet bounds: {movedX},{movedY}.");
    }
    Console.WriteLine("ASSERT PASS: posted mouse drag moved and persisted the live Codex pet.");
}

if (assertPetProxy)
{
    var proxy = hudWindows.SingleOrDefault(handle =>
        string.Equals(
            NativeMethods.ReadWindowTitle(handle),
            "CodexPetInputProxy",
            StringComparison.Ordinal));
    if (proxy == IntPtr.Zero)
        throw new InvalidOperationException("ASSERT FAIL: CodexPetInputProxy window was not found.");

    var proxyZ = topLevelWindows.IndexOf(proxy);
    var proxyStyle = NativeMethods.GetWindowLongPtr(proxy, NativeMethods.GwlExStyle).ToInt64();
    var anchorCenter = new ProbePoint(
        anchor.X + anchor.Width / 2,
        anchor.Y + anchor.Height / 2);
    var centerHit = NativeMethods.WindowFromPoint(
        new NativeMethods.Point(anchorCenter.X, anchorCenter.Y));
    var centerRoot = centerHit == IntPtr.Zero
        ? IntPtr.Zero
        : NativeMethods.GetAncestor(centerHit, NativeMethods.GaRoot);
    if (centerRoot == IntPtr.Zero) centerRoot = centerHit;

    if ((proxyStyle & NativeMethods.WsExTransparent) != 0)
        throw new InvalidOperationException("ASSERT FAIL: pet input proxy is WS_EX_TRANSPARENT.");
    if ((proxyStyle & NativeMethods.WsExLayered) == 0)
        throw new InvalidOperationException("ASSERT FAIL: pet input proxy is not layered.");
    if (petHostZ < 0 || proxyZ < 0 || proxyZ >= petHostZ)
        throw new InvalidOperationException(
            $"ASSERT FAIL: pet input proxy z-order is not above the pet host: proxy={proxyZ}, host={petHostZ}.");
    if (centerRoot != proxy)
        throw new InvalidOperationException(
            $"ASSERT FAIL: pet center is not owned by the input proxy: " +
            $"expected=0x{proxy.ToInt64():X}, actual=0x{centerRoot.ToInt64():X}.");

    var potionWindows = hudWindows.Where(handle => handle != proxy).ToList();
    if (potionWindows.Count != 2)
        throw new InvalidOperationException(
            $"ASSERT FAIL: expected two potion windows, found {potionWindows.Count}.");
    foreach (var potion in potionWindows)
    {
        var style = NativeMethods.GetWindowLongPtr(potion, NativeMethods.GwlExStyle).ToInt64();
        NativeMethods.GetWindowRect(potion, out var rect);
        var center = new ProbePoint(
            rect.Left + (rect.Right - rect.Left) / 2,
            rect.Top + (rect.Bottom - rect.Top) / 2);
        var hit = NativeMethods.SendNcHitTest(potion, center);
        var z = topLevelWindows.IndexOf(potion);
        if ((style & NativeMethods.WsExTransparent) == 0 || hit != -1 || z <= petHostZ)
        {
            throw new InvalidOperationException(
                $"ASSERT FAIL: potion routing is unsafe: z={z}, host={petHostZ}, " +
                $"style=0x{style:X}, hit={hit}.");
        }
    }

    Console.WriteLine(
        $"ASSERT PASS: proxy owns pet center, z={proxyZ} above host={petHostZ}; " +
        "two potion windows remain HTTRANSPARENT behind the pet.");

    if (assertLiveRelayStyle)
    {
        var originalHostStyle = NativeMethods.GetWindowLongPtr(
            petHost,
            NativeMethods.GwlExStyle).ToInt64();
        var interactiveHostStyle =
            originalHostStyle &
            ~(NativeMethods.WsExTransparent | NativeMethods.WsExLayered);
        try
        {
            NativeMethods.ShowWindow(proxy, NativeMethods.SwHide);
            NativeMethods.SetWindowLongPtr(
                petHost,
                NativeMethods.GwlExStyle,
                new IntPtr(interactiveHostStyle));
            NativeMethods.RefreshWindowStyle(petHost);
            Thread.Sleep(20);

            var liveHit = NativeMethods.WindowFromPoint(
                new NativeMethods.Point(anchorCenter.X, anchorCenter.Y));
            var liveRoot = liveHit == IntPtr.Zero
                ? IntPtr.Zero
                : NativeMethods.GetAncestor(liveHit, NativeMethods.GaRoot);
            if (liveRoot == IntPtr.Zero) liveRoot = liveHit;
            if (liveRoot != petHost)
            {
                throw new InvalidOperationException(
                    $"ASSERT FAIL: live pet host did not become the hit root: " +
                    $"expected=0x{petHost.ToInt64():X}, actual=0x{liveRoot.ToInt64():X}, " +
                    $"style=0x{originalHostStyle:X}->0x{interactiveHostStyle:X}.");
            }
        }
        finally
        {
            NativeMethods.SetWindowLongPtr(
                petHost,
                NativeMethods.GwlExStyle,
                new IntPtr(originalHostStyle));
            NativeMethods.RefreshWindowStyle(petHost);
            NativeMethods.ShowWindow(proxy, NativeMethods.SwShowNoActivate);
            NativeMethods.PlaceTopmost(proxy);
            Thread.Sleep(20);
        }

        var restoredHit = NativeMethods.WindowFromPoint(
            new NativeMethods.Point(anchorCenter.X, anchorCenter.Y));
        var restoredRoot = restoredHit == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.GetAncestor(restoredHit, NativeMethods.GaRoot);
        if (restoredRoot == IntPtr.Zero) restoredRoot = restoredHit;
        var restoredHostStyle = NativeMethods.GetWindowLongPtr(
            petHost,
            NativeMethods.GwlExStyle).ToInt64();
        if (restoredHostStyle != originalHostStyle || restoredRoot != proxy)
        {
            throw new InvalidOperationException(
                $"ASSERT FAIL: live relay style/proxy restoration failed: " +
                $"style=0x{restoredHostStyle:X}/0x{originalHostStyle:X}, " +
                $"hit=0x{restoredRoot.ToInt64():X}/0x{proxy.ToInt64():X}.");
        }

        Console.WriteLine(
            $"ASSERT PASS: live ChatGPT host became the hit root with " +
            $"style 0x{originalHostStyle:X}->0x{interactiveHostStyle:X}; " +
            "host style and proxy hit ownership were restored.");
    }
}

return;

static ProbeRect ReadPetAnchor()
{
    var statePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codex",
        ".codex-global-state.json");
    using var stream = new FileStream(
        statePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete);
    using var document = JsonDocument.Parse(stream);
    var bounds = document.RootElement.GetProperty("electron-avatar-overlay-bounds");
    var x = ReadNumber(bounds, "x");
    var y = ReadNumber(bounds, "y");

    if (bounds.TryGetProperty("mascot", out var mascot) &&
        mascot.ValueKind == JsonValueKind.Object)
    {
        return new ProbeRect(
            x + ReadNumber(mascot, "left"),
            y + ReadNumber(mascot, "top"),
            ReadNumber(mascot, "width"),
            ReadNumber(mascot, "height"));
    }

    if (bounds.TryGetProperty("anchor", out var anchor) &&
        anchor.ValueKind == JsonValueKind.Object)
    {
        return new ProbeRect(
            ReadNumber(anchor, "x"),
            ReadNumber(anchor, "y"),
            ReadNumber(anchor, "width"),
            ReadNumber(anchor, "height"));
    }

    return new ProbeRect(x, y, 112, 121);
}

static ProbePoint ReadSavedBounds()
{
    var statePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codex",
        ".codex-global-state.json");
    using var stream = new FileStream(
        statePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete);
    using var document = JsonDocument.Parse(stream);
    var bounds = document.RootElement.GetProperty("electron-avatar-overlay-bounds");
    return new ProbePoint(ReadNumber(bounds, "x"), ReadNumber(bounds, "y"));
}

static int ReadNumber(JsonElement parent, string name) =>
    checked((int)Math.Round(parent.GetProperty(name).GetDouble()));

static int ReadOption(string[] arguments, string prefix, int fallback)
{
    var value = arguments.FirstOrDefault(argument =>
        argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    return value is null ? fallback : int.Parse(value[prefix.Length..]);
}

static string? ReadStringOption(string[] arguments, string prefix) =>
    arguments.FirstOrDefault(argument =>
        argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

static string DescribePoint(string label, ProbePoint point)
{
    var handle = NativeMethods.WindowFromPoint(new NativeMethods.Point(point.X, point.Y));
    if (handle == IntPtr.Zero) return $"{label}@{point.X},{point.Y}: no-window";
    var root = NativeMethods.GetAncestor(handle, NativeMethods.GaRoot);
    if (root == IntPtr.Zero) root = handle;
    var hit = NativeMethods.SendNcHitTest(root, point);
    return $"{label}@{point.X},{point.Y}: hit={hit}; {DescribeWindow(root)}";
}

static string DescribeOwner(ProbePoint point)
{
    var handle = NativeMethods.WindowFromPoint(new NativeMethods.Point(point.X, point.Y));
    if (handle == IntPtr.Zero) return "no-window";
    var root = NativeMethods.GetAncestor(handle, NativeMethods.GaRoot);
    if (root == IntPtr.Zero) root = handle;
    NativeMethods.GetWindowThreadProcessId(root, out var processId);
    return $"{GetProcessName(processId)}[{processId}] hwnd=0x{root.ToInt64():X}";
}

static bool WindowContains(IntPtr handle, ProbePoint point) =>
    NativeMethods.GetWindowRect(handle, out var rect) &&
    point.X >= rect.Left &&
    point.X < rect.Right &&
    point.Y >= rect.Top &&
    point.Y < rect.Bottom;

static uint GetProcessId(IntPtr handle)
{
    NativeMethods.GetWindowThreadProcessId(handle, out var processId);
    return processId;
}

static string DescribeWindow(IntPtr handle)
{
    NativeMethods.GetWindowThreadProcessId(handle, out var processId);
    var processName = GetProcessName(processId);
    var className = NativeMethods.ReadClassName(handle);
    var title = NativeMethods.ReadWindowTitle(handle);
    NativeMethods.GetWindowRect(handle, out var rect);
    var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
    var flags = new List<string>();
    if ((style & NativeMethods.WsExTransparent) != 0) flags.Add("transparent");
    if ((style & NativeMethods.WsExNoActivate) != 0) flags.Add("noactivate");
    if ((style & NativeMethods.WsExToolWindow) != 0) flags.Add("toolwindow");
    if ((style & NativeMethods.WsExLayered) != 0) flags.Add("layered");
    var flagText = flags.Count == 0 ? "none" : string.Join(",", flags);
    return
        $"{processName}[{processId}] hwnd=0x{handle.ToInt64():X} " +
        $"class={className} title=\"{title}\" " +
        $"rect={rect.Left},{rect.Top},{rect.Right - rect.Left}x{rect.Bottom - rect.Top} " +
        $"ex=0x{style:X}({flagText})";
}

static string GetProcessName(uint processId)
{
    try
    {
        using var process = Process.GetProcessById(checked((int)processId));
        return process.ProcessName;
    }
    catch
    {
        return "unknown";
    }
}

readonly record struct ProbePoint(int X, int Y);

readonly record struct ProbeRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

static class NativeMethods
{
    public const uint GaRoot = 2;
    public const int GwlExStyle = -20;
    public const long WsExTransparent = 0x00000020;
    public const long WsExToolWindow = 0x00000080;
    public const long WsExLayered = 0x00080000;
    public const long WsExNoActivate = 0x08000000;
    public const int SwHide = 0;
    public const int SwShowNoActivate = 4;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint WmNcHitTest = 0x0084;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLeftButtonDown = 0x0201;
    private const uint WmLeftButtonUp = 0x0202;
    private const nuint MkLeftButton = 0x0001;
    private const uint InputMouse = 0;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const int VkLeftButton = 0x01;
    private const uint SmtoAbortIfHung = 0x0002;

    public delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    public readonly record struct Point(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr handle, out Rect rect);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr handle, uint flags);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr handle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr handle, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr handle,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out IntPtr result);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(
        IntPtr handle,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ScreenToClient(IntPtr handle, ref Point point);

    public static string ReadClassName(IntPtr handle)
    {
        var buffer = new StringBuilder(256);
        return GetClassName(handle, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }

    public static string ReadWindowTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        var buffer = new StringBuilder(Math.Max(1, length + 1));
        return GetWindowText(handle, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }

    public static long SendNcHitTest(IntPtr handle, ProbePoint point)
    {
        var packed = (point.Y << 16) | (point.X & 0xFFFF);
        return SendMessageTimeout(
            handle,
            WmNcHitTest,
            IntPtr.Zero,
            new IntPtr(packed),
            SmtoAbortIfHung,
            200,
            out var result) == IntPtr.Zero
            ? long.MinValue
            : result.ToInt64();
    }

    public static void PostMouseMove(IntPtr handle, ProbePoint point, bool leftButtonDown) =>
        EnsurePosted(
            handle,
            WmMouseMove,
            leftButtonDown ? MkLeftButton : 0,
            PackClientPoint(point),
            "mouse move");

    public static void PostLeftButtonDown(IntPtr handle, ProbePoint point) =>
        EnsurePosted(handle, WmLeftButtonDown, MkLeftButton, PackClientPoint(point), "left down");

    public static void PostLeftButtonUp(IntPtr handle, ProbePoint point) =>
        EnsurePosted(handle, WmLeftButtonUp, 0, PackClientPoint(point), "left up");

    public static void PostScreenMouseMove(
        IntPtr handle,
        ProbePoint screenPoint,
        bool leftButtonDown) =>
        PostMouseMove(handle, ToClientPoint(handle, screenPoint), leftButtonDown);

    public static void PostScreenLeftButtonDown(IntPtr handle, ProbePoint screenPoint) =>
        PostLeftButtonDown(handle, ToClientPoint(handle, screenPoint));

    public static void PostScreenLeftButtonUp(IntPtr handle, ProbePoint screenPoint) =>
        PostLeftButtonUp(handle, ToClientPoint(handle, screenPoint));

    public static IntPtr GetRootAtPoint(ProbePoint point)
    {
        var hit = WindowFromPoint(new Point(point.X, point.Y));
        if (hit == IntPtr.Zero) return IntPtr.Zero;
        var root = GetAncestor(hit, GaRoot);
        return root == IntPtr.Zero ? hit : root;
    }

    public static void EnsureCursorPosition(ProbePoint point)
    {
        if (!SetCursorPos(point.X, point.Y))
        {
            throw new InvalidOperationException(
                $"SetCursorPos failed: {Marshal.GetLastWin32Error()}.");
        }
    }

    public static bool IsLeftButtonDown() =>
        (GetAsyncKeyState(VkLeftButton) & 0x8000) != 0;

    public static void EnsureLeftButton(bool down)
    {
        var inputs = new[]
        {
            new Input
            {
                Type = InputMouse,
                Mouse = new MouseInput
                {
                    Flags = down ? MouseEventLeftDown : MouseEventLeftUp
                }
            }
        };
        if (SendInput(1, inputs, Marshal.SizeOf<Input>()) != 1)
        {
            throw new InvalidOperationException(
                $"SendInput left {(down ? "down" : "up")} failed: " +
                $"{Marshal.GetLastWin32Error()}.");
        }
    }

    private static ProbePoint ToClientPoint(IntPtr handle, ProbePoint screenPoint)
    {
        var point = new Point(screenPoint.X, screenPoint.Y);
        if (!ScreenToClient(handle, ref point))
        {
            throw new InvalidOperationException(
                $"ScreenToClient failed: {Marshal.GetLastWin32Error()}.");
        }
        return new ProbePoint(point.X, point.Y);
    }

    private static nint PackClientPoint(ProbePoint point) =>
        unchecked((nint)(((point.Y & 0xFFFF) << 16) | (point.X & 0xFFFF)));

    private static void EnsurePosted(
        IntPtr handle,
        uint message,
        nuint wParam,
        nint lParam,
        string operation)
    {
        if (!PostMessage(handle, message, wParam, lParam))
        {
            throw new InvalidOperationException(
                $"PostMessage {operation} failed: {Marshal.GetLastWin32Error()}.");
        }
    }

    public static void RefreshWindowStyle(IntPtr handle) =>
        SetWindowPos(
            handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);

    public static void PlaceTopmost(IntPtr handle) =>
        SetWindowPos(
            handle,
            HwndTopmost,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
}
