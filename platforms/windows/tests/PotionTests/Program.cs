using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CodexPetLimitRings.Windows;
using CodexPetLimitRings.Windows.Views;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var output = args.Length > 0 ? args[0] : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "native-hud-potion-review");
        Directory.CreateDirectory(output);
        var app = new App();
        app.InitializeComponent();
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var settings = JsonSerializer.Deserialize<OverlaySettings>("""
            {"scale":0.96,"potionGap":17,"alignment":"left","usageAlertsEnabled":false,"futureOption":{"keep":true}}
            """, options)!;
        settings.Normalize();
        Check(settings.PotionStyle == "classic", "기존 설정의 기본 모양");
        foreach (var style in PotionStyles.All)
        {
            settings.PotionStyle = style.Id;
            var reloaded = JsonSerializer.Deserialize<OverlaySettings>(JsonSerializer.Serialize(settings, options), options)!;
            reloaded.Normalize();
            Check(reloaded.PotionStyle == style.Id && reloaded.Scale == .96 && reloaded.PotionGap == 17 && reloaded.Alignment == "left" && !reloaded.UsageAlertsEnabled, "모양 저장 중 기존 설정 보존");
            Check(reloaded.AdditionalSettings!["futureOption"].GetProperty("keep").GetBoolean(), "알 수 없는 설정 보존");
            if (style.Id != "classic")
            {
                foreach (var bitmap in new[] { style.Frame, style.Mask, style.Preview })
                    Check(bitmap is { PixelWidth: 96, PixelHeight: 96, IsFrozen: true }, "내장 이미지 크기와 로딩");
            }
            var potion = new PotionWindow("5H", Colors.DarkRed, Colors.OrangeRed, Colors.Orange, Colors.Gold);
            potion.UpdateUsage(50, null, "none");
            potion.ApplyStyle(style.Id);
            Check(((TextBlock)potion.FindName("PercentText")).Text == "50%", "모양 변경 직후 잔여량 보존");
            foreach (var scale in new[] { .5, 1.0, 1.8 })
            {
                potion.ApplyScale(scale);
                Check(potion.Width == 92 * scale && potion.Height == 110 * scale, "HUD 창 크기 계약");
                foreach (var name in new[] { "PercentBackdrop", "LabelContainer" })
                {
                    var element = (FrameworkElement)potion.FindName(name);
                    Check(Canvas.GetTop(element) >= 0 && Canvas.GetTop(element) + element.Height <= potion.Height + .01, "최소 배율의 글자 잘림");
                }
                foreach (var usage in new double?[] { null, 0, 50, 100 })
                {
                    potion.UpdateUsage(usage, null, "none");
                    var text = ((TextBlock)potion.FindName("PercentText")).Text;
                    Check(text == (usage is null ? "—" : $"{usage:0}%"), "미확인과 실제 0 구별");
                    if (style.Id == "classic") continue;
                    var chamber = (Canvas)potion.FindName("StyledChamber");
                    var liquid = (Rectangle)potion.FindName("StyledLiquid");
                    var expectedHeight = (style.ChamberBottom - style.ChamberTop) * (usage ?? 0) / 100;
                    Check(Math.Abs(liquid.Height - expectedHeight) < .001, "포션 형태별 채움 높이");
                    Check(chamber.Width == 96 && chamber.Height == 96 && chamber.OpacityMask is ImageBrush mask && ReferenceEquals(mask.ImageSource, style.Mask), "고정 마스크 영역");
                }
            }
            potion.ApplyScale(1.5);
            potion.UpdateUsage(50, null, "none");
            Render((FrameworkElement)potion.Content, potion.Width, potion.Height, System.IO.Path.Combine(output, style.Id + ".png"));
            potion.Close();
        }
        settings.PotionStyle = "invalid";
        settings.Normalize();
        Check(settings.PotionStyle == "classic", "알 수 없는 모양의 안전한 기본값");
        var window = new SettingsWindow();
        window.Apply(settings);
        var choices = (ListBox)window.FindName("PotionStyleChoices");
        Check(choices.Items.Count == 6, "기본과 새 포션 5종");
        foreach (var size in new[] { new Size(480, 530), new Size(520, 660) })
        {
            var content = (FrameworkElement)window.Content;
            Render(content, size.Width, size.Height, System.IO.Path.Combine(output, $"settings-{size.Width:0}.png"));
            var bounds = choices.TransformToAncestor(content).TransformBounds(new Rect(choices.RenderSize));
            Check(bounds.Top >= 0 && bounds.Bottom <= size.Height, "첫 화면 안에 6개 선택 카드 노출");
            Check(bounds.Left >= 0 && bounds.Right <= size.Width, "작은 창에서 카드 가로 잘림 없음");
        }
        window.ClosePermanently();
        app.Shutdown();
        Console.WriteLine("PASS: 6개 모양 저장/재로드·기존 설정 보존, 15개 내장 이미지, 잔여량 null/0/50/100, 3개 배율, 첫 화면 선택 카드. 실제 사용자 입력은 별도 검증.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Render(FrameworkElement element, double width, double height, string path)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
