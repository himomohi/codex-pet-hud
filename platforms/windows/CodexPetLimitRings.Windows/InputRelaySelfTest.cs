using System.Text.Json;
using CodexPetLimitRings.Windows.Interop;
using Forms = System.Windows.Forms;
using DrawingPoint = System.Drawing.Point;

namespace CodexPetLimitRings.Windows;

internal static class InputRelaySelfTest
{
    public static async Task<int> RunAsync(string outputPath)
    {
        var report = new InputRelaySelfTestReport { StartedAt = DateTimeOffset.Now };
        Forms.Form? target = null;
        try
        {
            var work = Forms.Screen.PrimaryScreen?.WorkingArea
                       ?? Forms.SystemInformation.VirtualScreen;
            target = CreateTargetWindow(work.Left + 72, work.Top + 72);
            var mouseDownCount = 0;
            var mouseMoveCount = 0;
            var mouseUpCount = 0;
            var dragging = false;
            var pointerStart = new DrawingPoint();
            var windowStart = new DrawingPoint();

            target.MouseDown += (_, eventArgs) =>
            {
                if (eventArgs.Button != Forms.MouseButtons.Left) return;
                mouseDownCount++;
                dragging = true;
                pointerStart = target.PointToScreen(new DrawingPoint(eventArgs.X, eventArgs.Y));
                windowStart = target.Location;
            };
            target.MouseMove += (_, eventArgs) =>
            {
                if (!dragging) return;
                var current = target.PointToScreen(new DrawingPoint(eventArgs.X, eventArgs.Y));
                target.Location = new DrawingPoint(
                    windowStart.X + current.X - pointerStart.X,
                    windowStart.Y + current.Y - pointerStart.Y);
                mouseMoveCount++;
            };
            target.MouseUp += (_, eventArgs) =>
            {
                if (eventArgs.Button != Forms.MouseButtons.Left) return;
                mouseUpCount++;
                dragging = false;
            };

            target.Show();
            await Task.Delay(100);
            var targetHandle = target.Handle;
            var initialLeft = target.Left;
            var initialTop = target.Top;
            var virtualStart = new NativeMethods.NativePoint(
                target.Left + target.Width / 2,
                target.Top + target.Height / 2);
            var physicalStart = new NativeMethods.NativePoint(
                work.Left + 320,
                work.Top + 240);

            if (!NativeMethods.TryBeginPetPointerRelay(
                    targetHandle,
                    physicalStart,
                    virtualStart,
                    out var token,
                    out var beginDiagnostic))
            {
                throw new InvalidOperationException($"Relay begin failed: {beginDiagnostic}.");
            }
            await Task.Delay(50);
            for (var step = 1; step <= 4; step++)
            {
                var current = new NativeMethods.NativePoint(
                    physicalStart.X + 10 * step,
                    physicalStart.Y + 7 * step);
                if (!NativeMethods.MovePetPointerRelay(token, current, out var moveDiagnostic))
                {
                    throw new InvalidOperationException($"Relay move failed: {moveDiagnostic}.");
                }
                await Task.Delay(35);
            }
            var physicalEnd = new NativeMethods.NativePoint(
                physicalStart.X + 40,
                physicalStart.Y + 28);
            if (!NativeMethods.CompletePetPointerRelay(
                    token,
                    physicalEnd,
                    out var releaseDiagnostic))
            {
                throw new InvalidOperationException($"Relay release failed: {releaseDiagnostic}.");
            }
            await Task.Delay(140);

            var movedX = target.Left - initialLeft;
            var movedY = target.Top - initialTop;
            report.TargetMouseDownCount = mouseDownCount;
            report.TargetMouseMoveCount = mouseMoveCount;
            report.TargetMouseUpCount = mouseUpCount;
            report.MovedX = movedX;
            report.MovedY = movedY;
            report.BeginDiagnostic = beginDiagnostic;
            report.ReleaseDiagnostic = releaseDiagnostic;
            if (mouseDownCount != 1 ||
                mouseMoveCount < 1 ||
                mouseUpCount != 1 ||
                Math.Abs(movedX - 40) > 2 ||
                Math.Abs(movedY - 28) > 2)
            {
                throw new InvalidOperationException(
                    "Posted drag assertions failed: " +
                    $"down={mouseDownCount}, move={mouseMoveCount}, up={mouseUpCount}, " +
                    $"moved={movedX},{movedY}.");
            }

            var clickPoint = new NativeMethods.NativePoint(
                target.Left + target.Width / 2,
                target.Top + target.Height / 2);
            if (!NativeMethods.TrySendPetClick(targetHandle, clickPoint, out var clickDiagnostic))
            {
                throw new InvalidOperationException($"Posted click failed: {clickDiagnostic}.");
            }
            await Task.Delay(100);
            report.ClickDiagnostic = clickDiagnostic;
            report.TargetMouseDownCount = mouseDownCount;
            report.TargetMouseMoveCount = mouseMoveCount;
            report.TargetMouseUpCount = mouseUpCount;
            if (mouseDownCount != 2 || mouseUpCount != 2)
            {
                throw new InvalidOperationException(
                    $"Posted click assertions failed: down={mouseDownCount}, up={mouseUpCount}.");
            }

            report.Passed = true;
        }
        catch (Exception error)
        {
            report.Passed = false;
            report.Error = $"{error.GetType().Name}: {error.Message}";
        }
        finally
        {
            target?.Close();
            target?.Dispose();
            report.CompletedAt = DateTimeOffset.Now;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            await File.WriteAllTextAsync(
                outputPath,
                JsonSerializer.Serialize(
                    report,
                    new JsonSerializerOptions { WriteIndented = true }));
        }

        return report.Passed ? 0 : 1;
    }

    private static Forms.Form CreateTargetWindow(int left, int top) =>
        new()
        {
            Text = "CodexPetPostedInputSelfTestTarget",
            Left = left,
            Top = top,
            Width = 96,
            Height = 96,
            FormBorderStyle = Forms.FormBorderStyle.None,
            BackColor = System.Drawing.Color.Black,
            StartPosition = Forms.FormStartPosition.Manual,
            AutoScaleMode = Forms.AutoScaleMode.None,
            ShowInTaskbar = false,
            TopMost = true
        };

    private sealed class InputRelaySelfTestReport
    {
        public bool Passed { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset CompletedAt { get; set; }
        public int TargetMouseDownCount { get; set; }
        public int TargetMouseMoveCount { get; set; }
        public int TargetMouseUpCount { get; set; }
        public int MovedX { get; set; }
        public int MovedY { get; set; }
        public string? BeginDiagnostic { get; set; }
        public string? ReleaseDiagnostic { get; set; }
        public string? ClickDiagnostic { get; set; }
        public string? Error { get; set; }
    }
}
