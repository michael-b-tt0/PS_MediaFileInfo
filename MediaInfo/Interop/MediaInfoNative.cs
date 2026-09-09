using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace GetMediaInfo.Interop;

/// <summary>
/// The subset of the MediaInfo C ABI used by this module.
/// </summary>
internal static partial class MediaInfoNative
{
    // This is deliberately not "MediaInfo": the managed module is also named
    // MediaInfo.dll. NativeDependencyResolver maps this logical name to the
    // RID-specific native binary.
    internal const string LibraryName = "GetMediaInfo.MediaInfo.Native";

    static MediaInfoNative()
    {
        NativeDependencyResolver.Register(typeof(MediaInfoNative).Assembly);
    }

    [LibraryImport(LibraryName, EntryPoint = "MediaInfo_New")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    internal static partial MediaInfoHandle New();

    [LibraryImport(LibraryName, EntryPoint = "MediaInfo_Delete")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    internal static partial void Delete(nint handle);

    internal static nuint Open(MediaInfoHandle handle, string fileName)
    {
        if (!OperatingSystem.IsLinux())
        {
            return OpenUtf16(handle, fileName);
        }

        nint fileNameUtf32 = StringToUtf32(fileName);

        try
        {
            return OpenUtf32(handle, fileNameUtf32);
        }
        finally
        {
            Marshal.FreeHGlobal(fileNameUtf32);
        }
    }

    [LibraryImport(
        LibraryName,
        EntryPoint = "MediaInfo_Open",
        StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial nuint OpenUtf16(
        MediaInfoHandle handle,
        string fileName);

    [LibraryImport(LibraryName, EntryPoint = "MediaInfo_Open")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial nuint OpenUtf32(
        MediaInfoHandle handle,
        nint fileName);

    [LibraryImport(LibraryName, EntryPoint = "MediaInfo_Close")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    internal static partial void Close(MediaInfoHandle handle);

    internal static nint Get(
        MediaInfoHandle handle,
        MediaInfoStreamKind streamKind,
        nuint streamNumber,
        string parameter,
        MediaInfoValueKind kindOfInfo,
        MediaInfoValueKind kindOfSearch)
    {
        if (!OperatingSystem.IsLinux())
        {
            return GetUtf16(
                handle,
                streamKind,
                streamNumber,
                parameter,
                kindOfInfo,
                kindOfSearch);
        }

        nint parameterUtf32 = StringToUtf32(parameter);

        try
        {
            return GetUtf32(
                handle,
                streamKind,
                streamNumber,
                parameterUtf32,
                kindOfInfo,
                kindOfSearch);
        }
        finally
        {
            Marshal.FreeHGlobal(parameterUtf32);
        }
    }

    [LibraryImport(
        LibraryName,
        EntryPoint = "MediaInfo_Get",
        StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial nint GetUtf16(
        MediaInfoHandle handle,
        MediaInfoStreamKind streamKind,
        nuint streamNumber,
        string parameter,
        MediaInfoValueKind kindOfInfo,
        MediaInfoValueKind kindOfSearch);

    [LibraryImport(LibraryName, EntryPoint = "MediaInfo_Get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial nint GetUtf32(
        MediaInfoHandle handle,
        MediaInfoStreamKind streamKind,
        nuint streamNumber,
        nint parameter,
        MediaInfoValueKind kindOfInfo,
        MediaInfoValueKind kindOfSearch);

    [LibraryImport(LibraryName, EntryPoint = "MediaInfo_Inform")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    internal static partial nint Inform(
        MediaInfoHandle handle,
        nuint reserved);

    internal static nint Option(
        MediaInfoHandle handle,
        string option,
        string value)
    {
        if (!OperatingSystem.IsLinux())
        {
            return OptionUtf16(handle, option, value);
        }

        nint optionUtf32 = StringToUtf32(option);
        nint valueUtf32 = IntPtr.Zero;

        try
        {
            valueUtf32 = StringToUtf32(value);
            return OptionUtf32(handle, optionUtf32, valueUtf32);
        }
        finally
        {
            if (valueUtf32 != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(valueUtf32);
            }

            Marshal.FreeHGlobal(optionUtf32);
        }
    }

    [LibraryImport(
        LibraryName,
        EntryPoint = "MediaInfo_Option",
        StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial nint OptionUtf16(
        MediaInfoHandle handle,
        string option,
        string value);

    [LibraryImport(LibraryName, EntryPoint = "MediaInfo_Option")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial nint OptionUtf32(
        MediaInfoHandle handle,
        nint option,
        nint value);

    [LibraryImport(LibraryName, EntryPoint = "MediaInfo_Count_Get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    internal static partial nuint CountGet(
        MediaInfoHandle handle,
        MediaInfoStreamKind streamKind,
        nuint streamNumber);

    internal static string CopyString(nint value)
    {
        if (value == IntPtr.Zero)
        {
            return string.Empty;
        }

        if (!OperatingSystem.IsLinux())
        {
            return Marshal.PtrToStringUni(value) ?? string.Empty;
        }

        int byteLength = 0;

        while (Marshal.ReadInt32(value, byteLength) != 0)
        {
            byteLength = checked(byteLength + sizeof(int));
        }

        byte[] bytes = GC.AllocateUninitializedArray<byte>(byteLength);
        Marshal.Copy(value, bytes, 0, byteLength);
        return Encoding.UTF32.GetString(bytes);
    }

    private static nint StringToUtf32(string value)
    {
        byte[] bytes = Encoding.UTF32.GetBytes(value + '\0');
        nint buffer = Marshal.AllocHGlobal(bytes.Length);

        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            return buffer;
        }
        catch
        {
            Marshal.FreeHGlobal(buffer);
            throw;
        }
    }
}
