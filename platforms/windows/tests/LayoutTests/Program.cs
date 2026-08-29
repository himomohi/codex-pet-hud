using CodexPetLimitRings.Windows;
using CodexPetLimitRings.Windows.Services;
using System.Text.Json;

const double epsilon = 0.001;
var settings = new OverlaySettings { Scale = 0.9575376884422109, PotionGap = 10 };
var anchors = new List<PetAnchor>();

for (var x = 0; x <= 1800; x += 25)
{
    anchors.Add(new PetAnchor(x, 300, 112, 121, "primary", 0, 0, 1920, 1040));
    anchors.Add(new PetAnchor(x, 300, 224, 243, "primary-large", 0, 0, 1920, 1040));
}
anchors.Add(new PetAnchor(600, 300, 16, 18, "tiny", 0, 0, 1920, 1040));
anchors.Add(new PetAnchor(1000, 300, 448, 486, "huge", 0, 0, 1920, 1040));
for (var x = -1920; x <= -120; x += 25)
{
    anchors.Add(new PetAnchor(x, -20, 128, 139, "secondary", -1920, -91, 1920, 1040));
}
anchors.Add(new PetAnchor(5, 5, 112, 121, "top-left", 0, 0, 1920, 1040));
anchors.Add(new PetAnchor(1803, 914, 112, 121, "bottom-right", 0, 0, 1920, 1040));
anchors.Add(new PetAnchor(20, 200, 112, 121, "narrow", 0, 0, 160, 900));

var baseline = HudLayout.Calculate(anchors[0], settings);
if (UnifiedDragPolicy.IsDrag(new ScreenPointer(100, 100), new ScreenPointer(103, 100)))
    throw new Exception("Unified drag activated below the four-pixel threshold.");
if (!UnifiedDragPolicy.IsDrag(new ScreenPointer(100, 100), new ScreenPointer(104, 100)))
    throw new Exception("Unified drag did not activate at the four-pixel threshold.");
var virtualPointer = UnifiedDragPolicy.ToVirtualPointer(
    new ScreenPointer(100, 100),
    new ScreenPointer(500, 600),
    new ScreenPointer(135, 76));
Equal(535, virtualPointer.X, "potion drag virtual x");
Equal(576, virtualPointer.Y, "potion drag virtual y");
var dipDelta = UnifiedDragPolicy.ToDipDelta(
    new ScreenPointer(100, 100),
    new ScreenPointer(140, 120),
    2);
Equal(20, dipDelta.X, "unified drag DPI x");
Equal(10, dipDelta.Y, "unified drag DPI y");
var proxyPlacement = PetInputProxyLayout.Calculate(anchors[0]);
Equal(anchors[0].X, proxyPlacement.X, "pet proxy x");
Equal(anchors[0].Y, proxyPlacement.Y, "pet proxy y");
Equal(anchors[0].Width, proxyPlacement.Width, "pet proxy width");
Equal(anchors[0].Height, proxyPlacement.Height, "pet proxy height");
foreach (var anchor in anchors)
{
    foreach (var alignment in new[] { "split", "left", "right", "above", "below" })
    {
        var placement = HudLayout.Calculate(anchor, new OverlaySettings
        {
            Scale = settings.Scale,
            PotionGap = settings.PotionGap,
            Alignment = alignment
        });
        Equal(baseline.Scale, placement.Scale, $"scale changed at {alignment}:{anchor.DisplayId}:{anchor.X}");
        Equal(baseline.PotionWidth, placement.PotionWidth, $"width changed at {alignment}:{anchor.DisplayId}:{anchor.X}");
        Equal(baseline.PotionHeight, placement.PotionHeight, $"height changed at {alignment}:{anchor.DisplayId}:{anchor.X}");
        if (anchor.WorkWidth >= placement.PotionWidth)
        {
            InRange(placement.PrimaryX, anchor.WorkX, anchor.WorkRight - placement.PotionWidth, "primary x");
            InRange(placement.SecondaryX, anchor.WorkX, anchor.WorkRight - placement.PotionWidth, "secondary x");
        }
        if (anchor.WorkHeight >= placement.PotionHeight)
        {
            InRange(placement.Y, anchor.WorkY, anchor.WorkBottom - placement.PotionHeight, "y");
        }
    }
}

var leftEdge = HudLayout.Calculate(new PetAnchor(5, 300, 112, 121, null, 0, 0, 1920, 1040), settings);
if (leftEdge.PrimaryX < 117 || leftEdge.SecondaryX <= leftEdge.PrimaryX)
    throw new Exception("left-edge placement did not move both potions to the open side");

var rightEdge = HudLayout.Calculate(new PetAnchor(1803, 300, 112, 121, null, 0, 0, 1920, 1040), settings);
if (rightEdge.PrimaryX >= 1803 || rightEdge.SecondaryX >= rightEdge.PrimaryX)
    throw new Exception("right-edge placement did not move both potions to the open side");

var resized = HudLayout.Calculate(anchors[0], new OverlaySettings { Scale = 1.2, PotionGap = 10 });
if (Math.Abs(resized.PotionWidth - baseline.PotionWidth) < epsilon)
    throw new Exception("an explicit scale setting no longer changes HUD size");

var centered = new PetAnchor(800, 400, 112, 121, "alignment", 0, 0, 1920, 1040);
var left = HudLayout.Calculate(centered, new OverlaySettings { Alignment = "left" });
if (left.PrimaryX >= centered.X || left.SecondaryX >= centered.X) throw new Exception("left alignment escaped the pet's left side");
var right = HudLayout.Calculate(centered, new OverlaySettings { Alignment = "right" });
if (right.PrimaryX <= centered.Right || right.SecondaryX <= centered.Right) throw new Exception("right alignment escaped the pet's right side");
var above = HudLayout.Calculate(centered, new OverlaySettings { Alignment = "above" });
if (above.Y + above.PotionHeight > centered.Y) throw new Exception("above alignment overlaps the pet vertically");
var below = HudLayout.Calculate(centered, new OverlaySettings { Alignment = "below" });
if (below.Y < centered.Y + centered.Height) throw new Exception("below alignment overlaps the pet vertically");

var explicitLeftEdge = new PetAnchor(5, 300, 112, 121, "left-edge", 0, 0, 1920, 1040);
var safeLeft = HudLayout.Calculate(explicitLeftEdge, new OverlaySettings { Alignment = "left" });
if (safeLeft.PrimaryX < explicitLeftEdge.Right || safeLeft.SecondaryX < explicitLeftEdge.Right)
    throw new Exception("left alignment did not fall back to the open right side at the screen edge");
var explicitRightEdge = new PetAnchor(1803, 300, 112, 121, "right-edge", 0, 0, 1920, 1040);
var safeRight = HudLayout.Calculate(explicitRightEdge, new OverlaySettings { Alignment = "right" });
if (safeRight.PrimaryX + safeRight.PotionWidth > explicitRightEdge.X || safeRight.SecondaryX + safeRight.PotionWidth > explicitRightEdge.X)
    throw new Exception("right alignment did not fall back to the open left side at the screen edge");
var explicitTopEdge = new PetAnchor(800, 1, 112, 121, "top-edge", 0, 0, 1920, 1040);
var safeAbove = HudLayout.Calculate(explicitTopEdge, new OverlaySettings { Alignment = "above" });
if (safeAbove.Y < explicitTopEdge.Y + explicitTopEdge.Height)
    throw new Exception("above alignment did not fall back below the pet at the screen edge");
var explicitBottomEdge = new PetAnchor(800, 918, 112, 121, "bottom-edge", 0, 0, 1920, 1040);
var safeBelow = HudLayout.Calculate(explicitBottomEdge, new OverlaySettings { Alignment = "below" });
if (safeBelow.Y + safeBelow.PotionHeight > explicitBottomEdge.Y)
    throw new Exception("below alignment did not fall back above the pet at the screen edge");

using var currentProUsage = JsonDocument.Parse(
    """
    {
      "rate_limit": {
        "primary_window": {
          "used_percent": 19,
          "limit_window_seconds": 604800,
          "reset_at": 1788462027
        },
        "secondary_window": null
      },
      "additional_rate_limits": [
        {
          "limit_name": "GPT-5.3-Codex-Spark",
          "rate_limit": {
            "primary_window": {
              "used_percent": 0,
              "limit_window_seconds": 18000,
              "reset_at": 1788004567
            },
            "secondary_window": {
              "used_percent": 0,
              "limit_window_seconds": 604800,
              "reset_at": 1788591367
            }
          }
        }
      ]
    }
    """);
var currentProSnapshot = UsageService.Parse(currentProUsage.RootElement)
    ?? throw new Exception("current Pro usage payload did not parse");
Equal(0, currentProSnapshot.PrimaryUsed ?? double.NaN, "current Pro 5-hour usage");
Equal(19, currentProSnapshot.SecondaryUsed ?? double.NaN, "current Pro weekly usage");

Console.WriteLine($"Layout adversarial tests passed: {anchors.Count * 5} movement/alignment cases; fixed={baseline.PotionWidth:F3}x{baseline.PotionHeight:F3}");
Console.WriteLine("Unified drag tests passed: threshold, potion-to-pet virtual pointer mapping, DPI delta.");
Console.WriteLine("Pet proxy tests passed: anchor bounds remain the exact interactive surface.");
Console.WriteLine("Usage compatibility test passed: additional_rate_limits 5-hour + weekly mapping.");
return;

static void Equal(double expected, double actual, string message)
{
    if (Math.Abs(expected - actual) > epsilon) throw new Exception($"{message}: expected {expected}, actual {actual}");
}

static void InRange(double value, double minimum, double maximum, string message)
{
    if (value < minimum - epsilon || value > maximum + epsilon)
        throw new Exception($"{message} out of range: {value} not in [{minimum}, {maximum}]");
}
