using System.Text.Json;

namespace CodexPetLimitRings.Windows.Services;

public sealed class CodexPetStateReader
{
    private const double DefaultPetWidth = 112;
    private const double DefaultPetHeight = 121;
    private DateTime _lastWriteTimeUtc = DateTime.MinValue;
    private long _lastLength = -1;
    private PetWindowCandidate? _cachedCandidate;
    public string StatePath { get; } = Path.Combine(CodexPaths.Home, ".codex-global-state.json");

    public PetWindowCandidate? ReadVisibleCandidate()
    {
        try
        {
            var info = new FileInfo(StatePath);
            if (!info.Exists)
            {
                ClearCache();
                return null;
            }
            if (info.LastWriteTimeUtc == _lastWriteTimeUtc && info.Length == _lastLength)
            {
                return _cachedCandidate;
            }

            using var stream = new FileStream(StatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (!root.TryGetProperty("electron-avatar-overlay-open", out var open) ||
                open.ValueKind is not JsonValueKind.True)
            {
                return UpdateCache(info, null);
            }

            if (!root.TryGetProperty("electron-avatar-overlay-bounds", out var bounds) ||
                bounds.ValueKind is not JsonValueKind.Object ||
                !TryNumber(bounds, "x", out var windowX) ||
                !TryNumber(bounds, "y", out var windowY))
            {
                return UpdateCache(info, null);
            }

            string? displayId = null;
            if (bounds.TryGetProperty("displayId", out var display))
            {
                displayId = display.ValueKind == JsonValueKind.String ? display.GetString() : display.GetRawText();
            }
            double? displayX = null;
            double? displayY = null;
            double? displayWidth = null;
            double? displayHeight = null;
            if (bounds.TryGetProperty("displayBounds", out var displayBounds) &&
                displayBounds.ValueKind == JsonValueKind.Object &&
                TryNumber(displayBounds, "x", out var parsedDisplayX) &&
                TryNumber(displayBounds, "y", out var parsedDisplayY) &&
                TryNumber(displayBounds, "width", out var parsedDisplayWidth) &&
                TryNumber(displayBounds, "height", out var parsedDisplayHeight) &&
                parsedDisplayWidth > 0 && parsedDisplayHeight > 0)
            {
                displayX = parsedDisplayX;
                displayY = parsedDisplayY;
                displayWidth = parsedDisplayWidth;
                displayHeight = parsedDisplayHeight;
            }

            double windowWidth = 0;
            double windowHeight = 0;
            var hasWindowSize = TryNumber(bounds, "width", out windowWidth) &&
                                TryNumber(bounds, "height", out windowHeight) &&
                                windowWidth > 0 && windowHeight > 0;
            double mascotLeft = 0;
            double mascotTop = 0;
            double mascotWidth = 0;
            double mascotHeight = 0;
            var hasMascot = bounds.TryGetProperty("mascot", out var mascot) &&
                            mascot.ValueKind is JsonValueKind.Object &&
                            TryNumber(mascot, "left", out mascotLeft) &&
                            TryNumber(mascot, "top", out mascotTop) &&
                            TryNumber(mascot, "width", out mascotWidth) &&
                            TryNumber(mascot, "height", out mascotHeight);
            if (!hasMascot && hasWindowSize)
            {
                if (bounds.TryGetProperty("anchor", out var anchor) &&
                    anchor.ValueKind is JsonValueKind.Object &&
                    TryNumber(anchor, "x", out var anchorX) &&
                    TryNumber(anchor, "y", out var anchorY) &&
                    TryNumber(anchor, "width", out mascotWidth) &&
                    TryNumber(anchor, "height", out mascotHeight))
                {
                    mascotLeft = anchorX - windowX;
                    mascotTop = anchorY - windowY;
                    hasMascot = true;
                }
            }

            if (hasWindowSize && hasMascot && mascotWidth > 0 && mascotHeight > 0)
            {
                return UpdateCache(info, new PetWindowCandidate(
                    windowX,
                    windowY,
                    windowWidth,
                    windowHeight,
                    mascotLeft,
                    mascotTop,
                    mascotWidth,
                    mascotHeight,
                    displayId,
                    displayX,
                    displayY,
                    displayWidth,
                    displayHeight,
                    false));
            }

            if (!TryRememberedPetSize(bounds, displayId, displayWidth, displayHeight, out mascotWidth, out mascotHeight))
            {
                mascotWidth = DefaultPetWidth;
                mascotHeight = DefaultPetHeight;
            }
            return UpdateCache(info, new PetWindowCandidate(
                windowX,
                windowY,
                0,
                0,
                0,
                0,
                mascotWidth,
                mascotHeight,
                displayId,
                displayX,
                displayY,
                displayWidth,
                displayHeight,
                true));
        }
        catch (IOException) { ClearCache(); return null; }
        catch (UnauthorizedAccessException) { ClearCache(); return null; }
        catch (JsonException) { ClearCache(); return null; }
        catch (InvalidOperationException) { ClearCache(); return null; }
    }

    private static bool TryNumber(JsonElement parent, string name, out double value)
    {
        value = 0;
        return parent.TryGetProperty(name, out var element) &&
               element.ValueKind is JsonValueKind.Number &&
               element.TryGetDouble(out value) &&
               double.IsFinite(value);
    }

    private static bool TryRememberedPetSize(
        JsonElement bounds,
        string? displayId,
        double? displayWidth,
        double? displayHeight,
        out double width,
        out double height)
    {
        width = 0;
        height = 0;
        if (!string.IsNullOrWhiteSpace(displayId) &&
            bounds.TryGetProperty("byDisplayId", out var byDisplayId) &&
            byDisplayId.ValueKind is JsonValueKind.Object &&
            byDisplayId.TryGetProperty(displayId, out var displayCandidate) &&
            TryPetSize(displayCandidate, out width, out height))
        {
            return true;
        }

        if (displayWidth is { } rememberedDisplayWidth &&
            displayHeight is { } rememberedDisplayHeight &&
            bounds.TryGetProperty("byResolution", out var byResolution) &&
            byResolution.ValueKind is JsonValueKind.Object)
        {
            var resolution = $"{Math.Round(rememberedDisplayWidth):0}x{Math.Round(rememberedDisplayHeight):0}";
            if (byResolution.TryGetProperty(resolution, out var resolutionCandidate) &&
                TryPetSize(resolutionCandidate, out width, out height))
            {
                return true;
            }
        }
        return false;
    }

    private static bool TryPetSize(JsonElement candidate, out double width, out double height)
    {
        width = 0;
        height = 0;
        if (candidate.ValueKind is not JsonValueKind.Object) return false;
        foreach (var name in new[] { "anchor", "mascot" })
        {
            if (candidate.TryGetProperty(name, out var rect) &&
                rect.ValueKind is JsonValueKind.Object &&
                TryNumber(rect, "width", out width) &&
                TryNumber(rect, "height", out height) &&
                width > 0 && height > 0)
            {
                return true;
            }
        }
        return false;
    }

    private PetWindowCandidate? UpdateCache(FileInfo info, PetWindowCandidate? candidate)
    {
        _lastWriteTimeUtc = info.LastWriteTimeUtc;
        _lastLength = info.Length;
        _cachedCandidate = candidate;
        return candidate;
    }

    private void ClearCache()
    {
        _lastWriteTimeUtc = DateTime.MinValue;
        _lastLength = -1;
        _cachedCandidate = null;
    }
}
