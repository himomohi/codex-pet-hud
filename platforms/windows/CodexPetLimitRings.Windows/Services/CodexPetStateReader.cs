using System.Text.Json;

namespace CodexPetLimitRings.Windows.Services;

public sealed class CodexPetStateReader
{
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
                !TryNumber(bounds, "y", out var windowY) ||
                !TryNumber(bounds, "width", out var windowWidth) ||
                !TryNumber(bounds, "height", out var windowHeight) ||
                windowWidth <= 0 || windowHeight <= 0)
            {
                return UpdateCache(info, null);
            }

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
            if (!hasMascot)
            {
                if (!bounds.TryGetProperty("anchor", out var anchor) ||
                    anchor.ValueKind is not JsonValueKind.Object ||
                    !TryNumber(anchor, "x", out var anchorX) ||
                    !TryNumber(anchor, "y", out var anchorY) ||
                    !TryNumber(anchor, "width", out mascotWidth) ||
                    !TryNumber(anchor, "height", out mascotHeight))
                {
                    return UpdateCache(info, null);
                }
                mascotLeft = anchorX - windowX;
                mascotTop = anchorY - windowY;
            }
            if (mascotWidth <= 0 || mascotHeight <= 0) return UpdateCache(info, null);

            string? displayId = null;
            if (bounds.TryGetProperty("displayId", out var display))
            {
                displayId = display.ValueKind == JsonValueKind.String ? display.GetString() : display.GetRawText();
            }
            return UpdateCache(info, new PetWindowCandidate(
                windowX,
                windowY,
                windowWidth,
                windowHeight,
                mascotLeft,
                mascotTop,
                mascotWidth,
                mascotHeight,
                displayId));
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
