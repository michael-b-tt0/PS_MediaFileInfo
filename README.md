# Get-MediaInfo

`Get-MediaInfo` is a Windows x64 PowerShell module for reading technical media metadata through the native [MediaInfo](https://mediaarea.net/MediaInfo) library.

The module exports one cmdlet, `Get-MediaFileInfo`. It returns strongly typed result objects for video, audio, image, and unclassified media files.

## Requirements

- Windows x64
- PowerShell 7.6 or later
- .NET 10 SDK to build the module
- The native MediaInfo 26.05 library supplied at the project runtime path

The native library is loaded from `runtimes/win-x64/native/MediaInfo.dll` beside the managed module assembly. Ensure that DLL is present in source/build or distribution artifacts before building or importing the module.

## Build and import

From the repository root:

```powershell
dotnet build .\MediaInfo\MediaInfo.csproj -c Release
Import-Module .\MediaInfo\bin\Release\net10.0\Get-MediaInfo.psd1
```

To confirm the cmdlet is available:

```powershell
Get-Command Get-MediaFileInfo
```

Once imported, view the full cmdlet help with:

```powershell
Get-Help Get-MediaFileInfo -Full
```

The build output is a self-contained module directory. Copy the contents of `MediaInfo/bin/Release/net10.0` to a module directory on `PSModulePath` when installing it for general use.

## Usage

Inspect one file:

```powershell
Get-MediaFileInfo -Path .\media\clip.mp4
```

Inspect files matched by a wildcard and restrict candidates by extension:

```powershell
Get-MediaFileInfo -Path .\media\* -MediaType Video
```

Inspect multiple types concurrently:

```powershell
Get-MediaFileInfo -Path .\media\* -MediaType Video,Audio -ThrottleLimit 4
```

Use pipeline input:

```powershell
Get-ChildItem .\media -File |
    Get-MediaFileInfo -MediaType Audio
```

Use an exact path when a filename contains wildcard characters:

```powershell
Get-MediaFileInfo -LiteralPath '.\media\[release]\clip.mp4'
```

Show the detailed formatting view:

```powershell
Get-MediaFileInfo .\media\clip.mp4 -Detailed
```

The `-Detailed` switch changes only the default display view. The emitted object remains the same strongly typed result.

## Cmdlet parameters

### `-Path <string[]>`

Required positional input. Supports PowerShell wildcards, pipeline input, and pipeline property binding through the `FullName` alias.

An exact existing filesystem file is preferred before wildcard expansion. Paths must resolve through the FileSystem provider.

### `-LiteralPath <string[]>`

Required exact-path input. Wildcard characters are treated literally. The aliases are `-LP` and `-PSPath`.

### `-MediaType <MediaType[]>`

Optional extension-based input filter. Multiple values are combined with OR semantics:

```powershell
Get-MediaFileInfo .\media\* -MediaType Video,Image
```

The filter runs before `MediaInfoReader` opens a file. A skipped file produces no result and no error. `Unknown` matches extensions outside the known extension sets.

The extension map currently includes:

| Type | Extensions |
| --- | --- |
| Video | `.3g2`, `.3gp`, `.asf`, `.avi`, `.divx`, `.f4v`, `.flv`, `.m2ts`, `.m4v`, `.mkv`, `.mov`, `.mp4`, `.mpeg`, `.mpg`, `.mts`, `.mxf`, `.ogv`, `.rm`, `.rmvb`, `.ts`, `.vob`, `.webm`, `.wmv` |
| Audio | `.aac`, `.aif`, `.aiff`, `.alac`, `.amr`, `.ape`, `.au`, `.caf`, `.dts`, `.flac`, `.m4a`, `.m4b`, `.mka`, `.mp2`, `.mp3`, `.mpc`, `.oga`, `.ogg`, `.opus`, `.ra`, `.tak`, `.tta`, `.wav`, `.weba`, `.wma`, `.wv` |
| Image | `.avif`, `.bmp`, `.dng`, `.exr`, `.gif`, `.heic`, `.heif`, `.ico`, `.j2k`, `.jp2`, `.jpe`, `.jpeg`, `.jpg`, `.jxl`, `.png`, `.psd`, `.tif`, `.tiff`, `.webp` |

This is only a candidate filter. The final `MediaType` on a result is determined from MediaInfo's detected streams, so a file with a misleading extension can still produce a different result type.

When `-MediaType` is supplied, directories encountered during wildcard expansion are silently skipped. The cmdlet does not recursively enumerate a directory path by itself; use a wildcard, `Get-ChildItem`, or an explicit recursive enumeration when needed.

Aliases: `-Media`, `-Type`, `-M`.

### `-ThrottleLimit <int>`

Controls the maximum number of files inspected concurrently. Valid values are 1 through 8. The default is the processor count capped at 2.

Each worker owns an independent native MediaInfo reader. Results are emitted on the PowerShell pipeline thread in input order, regardless of which worker finishes first. Use `-ThrottleLimit 1` for sequential processing.

Aliases: `-threads`, `-workers`, `-t`.

### `-Detailed`

Displays all available properties in the extended formatting view. Alias: `-D`.

## Result types

Every result exposes common file and container properties, including:

- `FullName` / `PSPath`
- `Name` and `BaseName`
- `DirectoryName` and `Extension`
- `Length` and `SizeMiB`
- `LastWriteTime`
- `Duration`
- `ContainerFormat`
- `MediaType`

The concrete result type is selected using detected stream counts in this order:

1. One or more video streams → `VideoMediaInfoResult`
2. Otherwise, one or more audio streams → `AudioMediaInfoResult`
3. Otherwise, one or more image streams → `ImageMediaInfoResult`
4. Otherwise → `UnknownMediaInfoResult`

This means embedded album artwork does not turn an audio file into an image result. Video results can also include audio and text stream information.

### Video properties

`VideoMediaInfoResult` includes codec, stream count, width, height, `Resolution`, frame rate, bit rate, aspect ratio, format profile, scan type, color information, and attached audio/text stream details.

### Audio properties

`AudioMediaInfoResult` includes codec, stream count, bit rate, bit-rate mode, channels, sampling rate, artwork count, and common music metadata such as title, album, artist, track, genre, and recorded date.

### Image properties

`ImageMediaInfoResult` includes image stream count, format, width, height, `Resolution`, bit depth, color space, and title.

### Unknown properties

`UnknownMediaInfoResult` contains the common properties when MediaInfo opens a file but finds no video, audio, or image stream.

Inspect the complete object shape with:

```powershell
$result = Get-MediaFileInfo .\media\clip.mp4
$result.GetType().FullName
$result | Format-List *
```

## Error behavior

Path, provider, and native read failures are written as non-terminating PowerShell errors so other input files can continue to be processed. The cmdlet rejects non-filesystem providers.

Without `-MediaType`, an explicitly supplied directory reports a directory-not-a-file error. With `-MediaType`, directories are treated as non-candidates and skipped.

## Architecture

The cmdlet resolves paths and applies the extension filter on the PowerShell pipeline thread. Accepted files enter a bounded `System.Threading.Channels.Channel<T>` work queue. Worker tasks independently create `MediaInfoReader` instances and return typed results or captured errors. The pipeline thread drains completions, restores input order, and calls `WriteObject()` or `WriteError()`.

This keeps PowerShell APIs thread-confined while allowing native media inspection to run concurrently. Cancellation from downstream pipeline termination stops queued work and lets active readers finish safely.

## Development

Build the project from the repository root:

```powershell
dotnet restore .\MediaInfo\MediaInfo.csproj
dotnet build .\MediaInfo\MediaInfo.csproj -c Debug
```

The source help topic is `docs/Get-MediaFileInfo.md`. Regenerate the PowerShell MAML help file after editing it:

```powershell
Install-Module platyPS -Scope CurrentUser
Import-Module platyPS
New-ExternalHelp .\docs -OutputPath .\MediaInfo\en-US -Force
New-ExternalHelp .\docs -OutputPath .\MediaInfo\en-GB -Force
```

The generated `MediaInfo/en-US/Get-MediaInfo-help.xml` and `MediaInfo/en-GB/Get-MediaInfo-help.xml` files are copied into build and publish output beside the module manifest. Both currently use the same English source topic; the locale-specific directories allow PowerShell to select the appropriate help file for the host UI culture.

The managed project is in `MediaInfo/`. The upstream MediaInfo developer sources and documentation remain in `runtimes/Developers/`; the deployable native dependency is `MediaInfo/runtimes/win-x64/native/MediaInfo.dll`.

## Attribution and license

This project uses the MediaInfo library from MediaArea.net. See [runtimes/Developers/License.html](runtimes/Developers/License.html) for the bundled MediaInfo license and redistribution notices.
