using System.Text;
using XOnERPStudio.Forms;

namespace XOnERPStudio;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Gömülü lsl.dll'i çıkar ve P/Invoke çözücüsünü kaydet (LSL tiplerinden önce!)
        Lsl.NativeLibraryLoader.Initialize();

        // XDF/CSV içindeki Windows-1254 (Türkçe ANSI) metinleri çözebilmek için
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Arayüzsüz doğrulama: XOnERPStudio.exe --selftest <çıktı klasörü>
        if (args.Length > 0 && args[0] == "--selftest")
            return SelfTest.Run(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "XOnERPStudio_selftest"));

        if (args.Length > 0 && args[0] == "--lsltest")
            return SelfTest.RunLsl(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "XOnERPStudio_selftest"));

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.ToString(), "Beklenmeyen Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);

        var main = new MainForm();
        if (args.Length > 0 && args[0] == "--screens")
        {
            string dir = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "XOnERPStudio_screens");
            main.Shown += async (_, _) => { await main.CaptureScreensAsync(dir); main.Close(); };
        }
        Application.Run(main);
        return 0;
    }
}
