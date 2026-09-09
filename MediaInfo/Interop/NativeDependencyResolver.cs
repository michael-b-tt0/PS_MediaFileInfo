using System.Reflection;
using System.Runtime.InteropServices;

namespace GetMediaInfo.Interop;

internal static partial class NativeDependencyResolver
{
    private const int LocaleCategoryCharacterType = 0;

    internal static void Register(Assembly assembly)
    {
        NativeLibrary.SetDllImportResolver(
            assembly,
            Resolve);
    }

    private static nint Resolve(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (!string.Equals(
                libraryName,
                MediaInfoNative.LibraryName,
                StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        (string runtimeIdentifier, string nativeLibraryFileName) =
            (OperatingSystem.IsWindows(), RuntimeInformation.ProcessArchitecture) switch
        {
            (true, Architecture.X64) => ("win-x64", "MediaInfo.dll"),
            (true, Architecture.Arm64) => ("win-arm64", "MediaInfo.dll"),
            (false, Architecture.X64) when OperatingSystem.IsLinux() =>
                ("linux-x64", "libmediainfo.so"),
            _ => throw new PlatformNotSupportedException(
                $"Get-MediaInfo does not support {RuntimeInformation.OSDescription} " +
                $"on the '{RuntimeInformation.ProcessArchitecture}' process architecture. " +
                "Supported platforms are Windows x64, Windows ARM64, and Linux x64.")
        };

        string? assemblyDirectory = Path.GetDirectoryName(assembly.Location);

        if (string.IsNullOrEmpty(assemblyDirectory))
        {
            throw new DllNotFoundException(
                "The Get-MediaInfo module directory could not be determined.");
        }

        string nativeDirectory = Path.Combine(
            assemblyDirectory,
            "runtimes",
            runtimeIdentifier,
            "native");

        string nativeLibraryPath = Path.Combine(
            nativeDirectory,
            nativeLibraryFileName);

        if (!File.Exists(nativeLibraryPath))
        {
            throw new DllNotFoundException(
                $"The bundled MediaInfo library was not found at '{nativeLibraryPath}'.");
        }

        try
        {
            if (OperatingSystem.IsLinux())
            {
                // A managed Linux host does not necessarily initialize libc's
                // locale. MediaInfo needs the configured character-type locale
                // when converting wide paths to native filesystem paths.
                SetLocale(LocaleCategoryCharacterType, string.Empty);

                // The MediaArea Linux build depends on libzen. Loading the
                // bundled, fully-versioned file first avoids requiring a
                // libzen.so.0 symlink or a system-wide libzen installation.
                LoadFirstBundledMatch(nativeDirectory, "libzen.so*");

            }

            return NativeLibrary.Load(nativeLibraryPath);
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or BadImageFormatException)
        {
            throw new DllNotFoundException(
                $"The bundled MediaInfo library at '{nativeLibraryPath}' or one of " +
                $"its native dependencies could not be loaded: {exception.Message} " +
                "On Linux, use 'ldd' on the library to identify any missing " +
                "system dependency.",
                exception);
        }
    }

    private static void LoadFirstBundledMatch(
        string nativeDirectory,
        string searchPattern)
    {
        string? dependencyPath = Directory
            .EnumerateFiles(nativeDirectory, searchPattern)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();

        if (dependencyPath is not null)
        {
            NativeLibrary.Load(dependencyPath);
        }
    }

    [LibraryImport(
        "libc",
        EntryPoint = "setlocale",
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint SetLocale(int category, string locale);
}
