using System.Reflection;
using System.Runtime.InteropServices;

namespace XOnERPStudio.Lsl;

/// <summary>
/// Gömülü lsl.dll kaynağını LocalAppData altına çıkarır ve "lsl" adlı P/Invoke
/// çağrıları için bir DllImportResolver kaydeder. Program başlangıcında,
/// herhangi bir LSL tipi kullanılmadan önce bir kez çağrılmalıdır.
/// </summary>
public static class NativeLibraryLoader
{
    private static string _extractedDllPath;
    private static IntPtr _handle = IntPtr.Zero;
    private static bool _initialized;

    /// <summary>Son yükleme hatası (LSL kullanılamıyorsa kullanıcıya gösterilir).</summary>
    public static string LastError { get; private set; }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            ExtractNativeDll();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        NativeLibrary.SetDllImportResolver(typeof(LSL.StreamInfo).Assembly, Resolve);
    }

    /// <summary>LSL kütüphanesinin gerçekten yüklenebildiğini doğrular.</summary>
    public static bool IsAvailable(out string version)
    {
        version = null;
        try
        {
            version = LSL.LSL.library_version().ToString();
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private static void ExtractNativeDll()
    {
        var asm = Assembly.GetExecutingAssembly();
        var version = asm.GetName().Version?.ToString() ?? "0";
        var targetDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XOnERPStudio", "native", version);
        Directory.CreateDirectory(targetDir);
        _extractedDllPath = Path.Combine(targetDir, "lsl.dll");

        using var stream = asm.GetManifestResourceStream("lsl.dll")
            ?? throw new FileNotFoundException("Gömülü lsl.dll kaynağı bulunamadı.");

        // Dosya yoksa ya da boyutu farklıysa (bozuk/eski) yeniden yaz
        if (!File.Exists(_extractedDllPath) || new FileInfo(_extractedDllPath).Length != stream.Length)
        {
            using var fs = new FileStream(_extractedDllPath, FileMode.Create, FileAccess.Write, FileShare.None);
            stream.CopyTo(fs);
        }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != "lsl") return IntPtr.Zero;
        if (_handle != IntPtr.Zero) return _handle;

        if (!string.IsNullOrEmpty(_extractedDllPath) && File.Exists(_extractedDllPath)
            && NativeLibrary.TryLoad(_extractedDllPath, out _handle))
            return _handle;

        var sideBySide = Path.Combine(AppContext.BaseDirectory, "lsl.dll");
        if (File.Exists(sideBySide) && NativeLibrary.TryLoad(sideBySide, out _handle))
            return _handle;

        NativeLibrary.TryLoad("lsl", assembly, searchPath, out _handle);
        return _handle;
    }
}
