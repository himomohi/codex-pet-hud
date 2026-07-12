using System.Text.Json;

namespace CodexPetLimitRings.Windows.Services;

public sealed class CodexPetStateReader
{
    public string StatePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codex",
        ".codex-global-state.json");

    public PetAnchor? ReadVisibleAnchor()
    {
        try
        {
            using var stream = new FileStream(StatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (!root.TryGetProperty("electron-avatar-overlay-open", out var open) ||
                open.ValueKind is not JsonValueKind.True)
            {
                return null;
            }

            if (!root.TryGetProperty("electron-avatar-overlay-bounds", out var bounds) ||
                !bounds.TryGetProperty("anchor", out var anchor) ||
                !TryNumber(anchor, "x", out var x) ||
                !TryNumber(anchor, "y", out var y) ||
                !TryNumber(anchor, "width", out var width) ||
                !TryNumber(anchor, "height", out var height) ||
                width <= 0 || height <= 0)
            {
                return null;
            }

            string? displayId = null;
            if (bounds.TryGetProperty("displayId", out var display))
            {
                displayId = display.ValueKind == JsonValueKind.String ? display.GetString() : display.GetRawText();
            }
            return new PetAnchor(x, y, width, height, displayId);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    private static bool TryNumber(JsonElement parent, string name, out double value)
    {
        value = 0;
        return parent.TryGetProperty(name, out var element) && element.TryGetDouble(out value);
    }
}
