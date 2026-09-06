using System.Reflection;
using System.Runtime.InteropServices;

namespace GetMediaInfo.Interop;

internal static class NativeDependencyResolver
{
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

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Get-MediaInfo is supported only on Windows x64 and Windows ARM64.");
        }

        string runtimeIdentifier = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.Arm64 => "win-arm64",
            _ => throw new PlatformNotSupportedException(
                $"Get-MediaInfo does not support the '{RuntimeInformation.ProcessArchitecture}' process architecture. " +
                "Supported architectures are x64 and ARM64.")
        };

        string? assemblyDirectory = Path.GetDirectoryName(assembly.Location);

        if (string.IsNullOrEmpty(assemblyDirectory))
        {
            throw new DllNotFoundException(
                "The Get-MediaInfo module directory could not be determined.");
        }

        string nativeLibraryPath = Path.Combine(
            assemblyDirectory,
            "runtimes",
            runtimeIdentifier,
            "native",
            "MediaInfo.dll");

        if (!File.Exists(nativeLibraryPath))
        {
            throw new DllNotFoundException(
                $"The bundled MediaInfo library was not found at '{nativeLibraryPath}'.");
        }

        return NativeLibrary.Load(nativeLibraryPath);
    }
}
