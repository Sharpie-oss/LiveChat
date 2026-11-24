using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiveChat.Models;

namespace LiveChat.Services;

/// <summary>Génère des éléments d'aperçu pour vérifier l'affichage sans passer par Discord.</summary>
public static class TestCard
{
    /// <summary>Image de test générée localement (menu « Tester l'affichage »).</summary>
    public static MediaItem Create()
    {
        const int width = 960;
        const int height = 540;

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var background = new LinearGradientBrush(Color.FromRgb(0x70, 0x7B, 0xF8), Color.FromRgb(0x23, 0x25, 0x2A), 35);
            context.DrawRectangle(background, null, new Rect(0, 0, width, height));
            context.DrawEllipse(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), null, new Point(820, 110), 170, 170);
            context.DrawEllipse(new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)), null, new Point(130, 470), 230, 230);

            var culture = CultureInfo.GetCultureInfo("fr-FR");
            var bold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var regular = new Typeface("Segoe UI");

            var title = new FormattedText("LiveChat fonctionne !", culture, FlowDirection.LeftToRight, bold, 68, Brushes.White, 1.0);
            context.DrawText(title, new Point((width - title.Width) / 2, 185));

            var subtitle = new FormattedText($"Aperçu de l'affichage · {DateTime.Now:HH:mm:ss}", culture, FlowDirection.LeftToRight,
                regular, 30, new SolidColorBrush(Color.FromArgb(225, 255, 255, 255)), 1.0);
            context.DrawText(subtitle, new Point((width - subtitle.Width) / 2, 290));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        var directory = Path.Combine(Path.GetTempPath(), "LiveChat");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "apercu.png");
        using (var file = File.Create(path))
        {
            encoder.Save(file);
        }

        return new MediaItem(MediaKind.Image, "LiveChat")
        {
            Url = new Uri(path).AbsoluteUri,
            Caption = "Les médias postés dans le salon s'afficheront ici.",
        };
    }

    /// <summary>Aperçu d'un fichier local ou d'une URL (option --preview), pour tester un format.</summary>
    public static MediaItem? FromPathOrUrl(string pathOrUrl)
    {
        string url;
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            url = uri.AbsoluteUri;
        }
        else if (File.Exists(pathOrUrl))
        {
            url = new Uri(Path.GetFullPath(pathOrUrl)).AbsoluteUri;
        }
        else
        {
            return null;
        }

        var kind = MessageParser.Classify(null, uri?.IsFile == false ? uri.AbsolutePath : pathOrUrl);
        return kind == MediaKind.File
            ? null
            : new MediaItem(kind, "Aperçu") { Url = url, Caption = Path.GetFileName(pathOrUrl) };
    }
}
