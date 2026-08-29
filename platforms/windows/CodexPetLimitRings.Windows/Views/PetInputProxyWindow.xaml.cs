using System.Windows;
using System.Windows.Input;
using CodexPetLimitRings.Windows.Interop;
using InputMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace CodexPetLimitRings.Windows.Views;

public partial class PetInputProxyWindow : Window
{
    private bool _pointerPressed;
    internal event Action<ScreenPointer>? PointerPressed;
    internal event Action<ScreenPointer>? PointerMoved;
    internal event Action<ScreenPointer>? PointerReleased;
    internal event Action? PointerCancelled;
    internal event Action? HoverMoved;

    public PetInputProxyWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => NativeMethods.ConfigurePetInputProxy(this);
        PreviewMouseLeftButtonDown += OnMouseLeftButtonDown;
        PreviewMouseMove += OnMouseMove;
        PreviewMouseLeftButtonUp += OnMouseLeftButtonUp;
        LostMouseCapture += OnLostMouseCapture;
    }

    public void Apply(PetInputProxyPlacement placement)
    {
        Left = placement.X;
        Top = placement.Y;
        Width = placement.Width;
        Height = placement.Height;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Left) return;
        eventArgs.Handled = true;
        _pointerPressed = true;
        Mouse.Capture(this, CaptureMode.Element);
        PointerPressed?.Invoke(ToScreenPointer(eventArgs));
    }

    private void OnMouseMove(object sender, InputMouseEventArgs eventArgs)
    {
        if (!_pointerPressed)
        {
            HoverMoved?.Invoke();
            return;
        }
        eventArgs.Handled = true;
        PointerMoved?.Invoke(ToScreenPointer(eventArgs));
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (!_pointerPressed || eventArgs.ChangedButton != MouseButton.Left) return;
        eventArgs.Handled = true;
        var pointer = ToScreenPointer(eventArgs);
        _pointerPressed = false;
        PointerReleased?.Invoke(pointer);
        if (IsMouseCaptured) Mouse.Capture(null);
    }

    private void OnLostMouseCapture(object sender, InputMouseEventArgs eventArgs)
    {
        if (!_pointerPressed) return;
        _pointerPressed = false;
        PointerCancelled?.Invoke();
    }

    private ScreenPointer ToScreenPointer(InputMouseEventArgs eventArgs)
    {
        var point = PointToScreen(eventArgs.GetPosition(this));
        return new ScreenPointer(point.X, point.Y);
    }
}
