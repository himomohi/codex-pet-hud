using System.Drawing;
using CodexPetLimitRings.Windows.Interop;

namespace CodexPetLimitRings.Windows;

internal enum UnifiedDragSurface
{
    Pet,
    Potion
}

internal sealed class UnifiedDragController : IDisposable
{
    private Gesture? _gesture;
    private PetPointerRelayToken _relayToken;

    public bool IsDragging { get; private set; }
    public bool IsPressed => _gesture is not null;
    public event Action<PetAnchor>? DragStarted;
    public event Action<double, double>? DragMoved;
    public event Action<PetAnchor, double, double, bool>? DragFinished;
    public event Action<string>? StatusChanged;

    public void Press(
        UnifiedDragSurface surface,
        ScreenPointer pointer,
        PetAnchor anchor,
        Rectangle physicalPetBounds)
    {
        Cancel();
        var dpiScale = NativeMethods.GetDpiScaleForWindow(anchor.NativeWindowHandle);
        var virtualStart = surface == UnifiedDragSurface.Pet
            ? pointer
            : new ScreenPointer(
                physicalPetBounds.Left + physicalPetBounds.Width / 2.0,
                physicalPetBounds.Top + physicalPetBounds.Height / 2.0);
        _gesture = new Gesture(surface, pointer, virtualStart, pointer, anchor, dpiScale);
    }

    public void Move(ScreenPointer pointer)
    {
        var gesture = _gesture;
        if (gesture is null) return;
        gesture.LastPointer = pointer;

        if (!IsDragging)
        {
            if (!UnifiedDragPolicy.IsDrag(gesture.PressPointer, pointer)) return;
            if (!NativeMethods.TryBeginPetPointerRelay(
                    gesture.Anchor.NativeWindowHandle,
                    ToNativePoint(gesture.PressPointer),
                    ToNativePoint(gesture.VirtualStart),
                    out _relayToken,
                    out var diagnostic))
            {
                StatusChanged?.Invoke($"Unified drag rejected: {diagnostic}.");
                _gesture = null;
                return;
            }

            IsDragging = true;
            DragStarted?.Invoke(gesture.Anchor);
            StatusChanged?.Invoke(
                $"Unified drag started from {gesture.Surface.ToString().ToLowerInvariant()}: {diagnostic}.");
        }

        if (!NativeMethods.MovePetPointerRelay(
                _relayToken,
                ToNativePoint(pointer),
                out var moveDiagnostic))
        {
            Finish(pointer, delivered: false, $"move failed: {moveDiagnostic}");
            return;
        }

        var delta = UnifiedDragPolicy.ToDipDelta(
            gesture.PressPointer,
            pointer,
            gesture.DpiScale);
        DragMoved?.Invoke(delta.X, delta.Y);
    }

    public void Release(ScreenPointer pointer)
    {
        var gesture = _gesture;
        if (gesture is null) return;
        gesture.LastPointer = pointer;

        if (IsDragging)
        {
            var delivered = NativeMethods.CompletePetPointerRelay(
                _relayToken,
                ToNativePoint(pointer),
                out var diagnostic);
            Finish(pointer, delivered, diagnostic);
            return;
        }

        if (gesture.Surface == UnifiedDragSurface.Pet)
        {
            var delivered = NativeMethods.TrySendPetClick(
                gesture.Anchor.NativeWindowHandle,
                ToNativePoint(pointer),
                out var diagnostic);
            StatusChanged?.Invoke(
                delivered
                    ? $"Pet click delivered: {diagnostic}."
                    : $"Pet click rejected: {diagnostic}.");
        }
        _gesture = null;
    }

    public void Cancel()
    {
        var gesture = _gesture;
        if (gesture is null) return;
        if (IsDragging)
        {
            var delivered = NativeMethods.CompletePetPointerRelay(
                _relayToken,
                ToNativePoint(gesture.LastPointer),
                out var diagnostic);
            Finish(
                gesture.LastPointer,
                delivered,
                delivered ? $"cancelled; {diagnostic}" : $"cancel failed: {diagnostic}");
            return;
        }
        _gesture = null;
    }

    private void Finish(ScreenPointer pointer, bool delivered, string diagnostic)
    {
        var gesture = _gesture;
        if (gesture is null) return;
        var delta = UnifiedDragPolicy.ToDipDelta(
            gesture.PressPointer,
            pointer,
            gesture.DpiScale);
        IsDragging = false;
        _relayToken = default;
        _gesture = null;
        DragFinished?.Invoke(gesture.Anchor, delta.X, delta.Y, delivered);
        StatusChanged?.Invoke(
            delivered
                ? $"Unified drag completed: delta={delta.X:0.##},{delta.Y:0.##}; {diagnostic}."
                : $"Unified drag failed: delta={delta.X:0.##},{delta.Y:0.##}; {diagnostic}.");
    }

    private static NativeMethods.NativePoint ToNativePoint(ScreenPointer pointer) =>
        new(
            checked((int)Math.Round(pointer.X)),
            checked((int)Math.Round(pointer.Y)));

    public void Dispose() => Cancel();

    private sealed class Gesture(
        UnifiedDragSurface surface,
        ScreenPointer pressPointer,
        ScreenPointer virtualStart,
        ScreenPointer lastPointer,
        PetAnchor anchor,
        double dpiScale)
    {
        public UnifiedDragSurface Surface { get; } = surface;
        public ScreenPointer PressPointer { get; } = pressPointer;
        public ScreenPointer VirtualStart { get; } = virtualStart;
        public ScreenPointer LastPointer { get; set; } = lastPointer;
        public PetAnchor Anchor { get; } = anchor;
        public double DpiScale { get; } = dpiScale;
    }
}
