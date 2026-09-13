using System.Windows.Media.Imaging;

namespace CodexPetLimitRings.Windows;

public sealed record PotionStyleDefinition(
    string Id, string Name, string Description,
    BitmapSource? Preview, BitmapSource? Frame, BitmapSource? Mask,
    double ChamberTop, double ChamberBottom);

public static class PotionStyles
{
    public static IReadOnlyList<PotionStyleDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        new PotionStyleDefinition("classic", "기본 포션", "기존 둥근 병", null, null, null, 0, 67),
        Create("celestial-orb", "천청 오브", "푸른빛 둥근 병", 25, 80),
        Create("rose-heart", "장미 하트", "따뜻한 하트 병", 28, 81),
        Create("amber-star", "호박빛 별", "빛나는 별 모양", 29, 80),
        Create("lunar-crescent", "보랏빛 초승달", "보랏빛 달 모양", 28, 83),
        Create("verdant-leaf", "신록 잎새", "싱그러운 잎 모양", 29, 85)
    });

    public static string Normalize(string? id) => PotionStyleIds.Normalize(id);
    public static PotionStyleDefinition Get(string? id) => All.First(style => style.Id == Normalize(id));

    private static PotionStyleDefinition Create(string id, string name, string description, double top, double bottom) =>
        new(id, name, description, ReadBitmap(id), ReadBitmap(id + "-frame"), ReadBitmap(id + "-mask"), top, bottom);

    private static BitmapSource ReadBitmap(string name)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri($"pack://application:,,,/CodexPetLimitRings;component/Assets/Potions/{name}.png", UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
