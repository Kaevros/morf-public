using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class FriendSetup
{
    private static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    private static TextBlock status;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            VerifyPayload();
            if (args.Length == 1 && args[0] == "--verify") return 0;
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var content = Content();
            if (args.Length == 1 && args[0] == "--render")
            {
                content.Measure(new Size(560, 490)); content.Arrange(new Rect(0, 0, 560, 490)); content.UpdateLayout();
                var bitmap = new RenderTargetBitmap(560, 490, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(Root, "setup-preview.png"))) png.Save(stream);
                app.Shutdown(); return 0;
            }
            app.Run(new Window { Title = "Morf kurulumu", Content = content, Width = 576, Height = 529,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.FromRgb(28, 28, 31)), FontFamily = new FontFamily("Segoe UI"),
                Icon = Logo() });
            return 0;
        }
        catch (Exception error)
        {
            if (args.Length == 1 && args[0] == "--verify") return 1;
            MessageBox.Show("Kurulum paketi doğrulanamadı. ZIP'i tekrar bir klasöre çıkarın.\n\n" + error.Message, "Morf kurulumu", MessageBoxButton.OK, MessageBoxImage.Information);
            return 1;
        }
    }
    private static void VerifyPayload()
    {
        Verify(SetupPayload.Package, SetupPayload.PackageHash);
        Verify("Morf.cer", SetupPayload.CertificateHash);
    }
    private static void Verify(string file, string expected)
    {
        using (var hash = SHA256.Create())
        using (var stream = File.OpenRead(Path.Combine(Root, file)))
            if (!string.Equals(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Bir kurulum dosyası eksik veya değişmiş: " + file);
    }
    private static BitmapFrame Logo()
    {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MorfSetupLogo"))
            return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    }
    private static Grid Content()
    {
        var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(28, 28, 31)) };
        var body = new StackPanel { Margin = new Thickness(32, 26, 32, 24) }; root.Children.Add(body);
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new Image { Source = Logo(), Width = 44, Height = 44, Margin = new Thickness(0, 0, 14, 0) });
        var heading = new StackPanel(); heading.Children.Add(Label("Morf", 25, true)); heading.Children.Add(Label(SetupPayload.Version + " · Windows x64", 12)); header.Children.Add(heading); body.Children.Add(header);
        var intro = Label("ZIP'i bir klasöre çıkarın. Morf açıksa önce uygulamanın Çıkış menüsünü kullanın. Mevcut ayarlarınız güncellemede korunur.", 13);
        intro.Margin = new Thickness(0, 22, 0, 18); body.Children.Add(intro);
        body.Children.Add(Action("1. Sertifikayı aç", "Morf.cer"));
        var certificate = Label("İlk kurulumda: Sertifikayı Yükle → Yerel Makine → Güvenilen Kişiler. Bu yerel test imzasıdır; Windows yönetici onayı isteyebilir. Zaten güvenilen bir güncellemede bu adımı geçebilirsiniz.", 12);
        certificate.Margin = new Thickness(0, 8, 0, 18); body.Children.Add(certificate);
        body.Children.Add(Action("2. Morf’u yükle / güncelle", SetupPayload.Package));
        var install = Label("Windows’un kurulum penceresinde Yükle / Güncelle’yi seçin. Ardından Morf’u Başlat menüsünden açın. Bildirimler için Morf ayarlarından Windows iznini verin.", 12);
        install.Margin = new Thickness(0, 8, 0, 16); body.Children.Add(install);
        status = Label("Dosyalar doğrulandı. Kurulum adımını seçebilirsiniz.", 12); body.Children.Add(status);
        return root;
    }
    private static TextBlock Label(string value, double size, bool bold = false)
    { return new TextBlock { Text = value, FontSize = size, Foreground = new SolidColorBrush(bold ? Color.FromRgb(245,245,248) : Color.FromRgb(190,190,198)), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.45 }; }
    private static Button Action(string title, string file)
    {
        var button = new Button { Content = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(70, 64, 110)), HorizontalContentAlignment = HorizontalAlignment.Center, MinHeight = 42 };
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.BackgroundProperty, button.Background); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10)); border.SetValue(Border.PaddingProperty, new Thickness(12, 10, 12, 10));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); border.AppendChild(presenter); template.VisualTree = border; button.Template = template;
        button.Click += (_, __) => { try { VerifyPayload(); using (var process = Process.Start(new ProcessStartInfo(Path.Combine(Root, file)) { UseShellExecute = true })) { } status.Text = file == "Morf.cer" ? "Windows sertifika penceresi açıldı. Güven ekleme adımını kendiniz onaylayın." : "Windows kurulum isteği iletildi. Kurulumu açılan pencerede tamamlayın."; } catch (Exception error) { status.Text = "Windows bu dosyayı açamadı: " + error.Message; } };
        return button;
    }
}
