---
external help file: Get-MediaInfo-help.xml
Module Name: Get-MediaInfo
online version:
schema: 2.0.0
---

# Get-MediaFileInfo

## SYNOPSIS

Gets technical metadata from media files.

## SYNTAX

### Path (Default)

```powershell
Get-MediaFileInfo [-Path] <String[]> [[-MediaType] <MediaType[]>] [[-ThrottleLimit] <Int32>] [-Detailed] [<CommonParameters>]
```

### LiteralPath

```powershell
Get-MediaFileInfo -LiteralPath <String[]> [[-MediaType] <MediaType[]>] [[-ThrottleLimit] <Int32>] [-Detailed] [<CommonParameters>]
```

## DESCRIPTION

`Get-MediaFileInfo` opens each accepted filesystem file with the bundled MediaInfo library and returns a strongly typed result object.

Each file is classified independently from its detected streams. Results are emitted in input order even when multiple files are inspected concurrently.

The cmdlet supports video, audio, image, and unknown result types. A file with video streams is classified as video first, followed by audio, image, and then unknown when no supported stream is detected. Embedded artwork does not turn an audio file into an image result. This means that media files with the wrong file extension should still be correctly resolved.

When `-MediaType` is specified, it is an extension-based prefilter. It runs before MediaInfo opens a file and does not replace the final stream-based classification on the result object.

## EXAMPLES

### Example 1: Inspect one media file

```powershell
Get-MediaFileInfo -Path .\media\clip.mp4
```

Gets metadata for a single file.

### Example 2: Inspect wildcard-matched video files

```powershell
Get-MediaFileInfo -Path .\media\* -MediaType Video
```

Uses the filename extension to skip non-video candidates before MediaInfo reads them. Skipped files produce no output or error.

### Example 3: Inspect multiple media types concurrently

```powershell
Get-MediaFileInfo -Path .\media\* -MediaType Video,Audio -ThrottleLimit 4
```

Inspects video and audio candidates with up to four worker tasks. By default the application uses 2 workers. Results remain in input order. Rule of thumb is that you should use more works the faster your disk storage.

### Example 4: Process files from the pipeline

```powershell
Get-ChildItem .\media -File |
    Get-MediaFileInfo -MediaType Audio
```

Reads files supplied by `Get-ChildItem` through the `FullName` or `PSPath` property.

### Example 5: Use an exact path

```powershell
Get-MediaFileInfo -LiteralPath '.\media\[release]\clip.mp4'
```

Treats wildcard characters in the filename literally. The `-LiteralPath` aliases are `-LP` and `-PSPath`.

### Example 6: Display all result properties

```powershell
Get-MediaFileInfo .\media\clip.mp4 -Detailed
```

Uses the extended formatting view. `-Detailed` changes display formatting only; the emitted object remains strongly typed.

### Example 7: Select a result type in the pipeline

```powershell
Get-MediaFileInfo .\media\* |
    Where-Object MediaType -eq Video
```

Filters by the final MediaInfo-detected result type after inspection.

## PARAMETERS

### -Path

Specifies one or more filesystem paths to inspect. Wildcard characters are supported. An exact existing file is preferred before wildcard expansion.

`Path` is the positional parameter and accepts pipeline input. The `FullName` alias supports pipeline property binding.

```yaml
Type: System.String[]
Parameter Sets: Path
Aliases: FullName
Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue, ByPropertyName)
Accept wildcard characters: True
```

### -LiteralPath

Specifies one or more filesystem paths to inspect exactly as entered. Wildcard characters are treated literally.

```yaml
Type: System.String[]
Parameter Sets: LiteralPath
Aliases: LP, PSPath
Required: True
Position: Named
Default value: None
Accept pipeline input: True (ByPropertyName)
Accept wildcard characters: False
```

### -MediaType

Specifies one or more extension-based candidate types to include. Multiple values use OR semantics. Valid values are `Video`, `Audio`, `Image`, and `Unknown`.

`Unknown` matches extensions outside the known video, audio, and image extension sets. The filter does not inspect file contents. A misleading extension can therefore pass the filter and produce a different final `MediaType` after MediaInfo reads the file.

When this parameter is supplied, directories encountered during wildcard expansion are silently skipped. The cmdlet does not recursively enumerate a directory path by itself.

```yaml
Type: GetMediaInfo.MediaType[]
Parameter Sets: (All)
Aliases: Media, Type, M
Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ThrottleLimit

Specifies the maximum number of files inspected concurrently. Valid values are 1 through 8. The default is the processor count capped at 2. Use `1` for sequential processing.

Each worker owns an independent MediaInfo reader. This parameter does not change result classification or output ordering.

```yaml
Type: System.Int32
Parameter Sets: (All)
Aliases: threads, workers, t
Required: False
Position: Named
Default value: Processor count capped at 2
Accept pipeline input: False
Accept wildcard characters: False
```

### -Detailed

Displays all available properties in the extended formatting view. The result object itself is unchanged.

```yaml
Type: System.Management.Automation.SwitchParameter
Parameter Sets: (All)
Aliases: D
Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters

This cmdlet supports the common parameters, including `-ErrorAction`, `-ErrorVariable`, `-WarningAction`, `-Verbose`, `-Debug`, `-PipelineVariable`, and `-OutVariable`.

## INPUTS

### System.String

You can pipe path strings to `Get-MediaFileInfo`.

### System.Management.Automation.PSObject

Pipeline objects with a `FullName` or `PSPath` property can bind to the path parameter.

## OUTPUTS

### GetMediaInfo.VideoMediaInfoResult

Returned when MediaInfo detects one or more video streams.

### GetMediaInfo.AudioMediaInfoResult

Returned when no video stream is present and MediaInfo detects one or more audio streams.

### GetMediaInfo.ImageMediaInfoResult

Returned when no video or audio stream is present and MediaInfo detects one or more image streams.

### GetMediaInfo.UnknownMediaInfoResult

Returned when MediaInfo opens the file but detects no video, audio, or image stream.

## NOTES

The bundled Linux x64 native libraries were built with GCC 11.4 and require GLIBC_2.33 or later. MediaInfo was compiled with libmms, libcurl, and Graphviz support disabled.

The result objects expose common file properties such as `FullName`, `PSPath`, `Name`, `BaseName`, `DirectoryName`, `Extension`, `Length`, `SizeMiB`, `LastWriteTime`, `Duration`, `ContainerFormat`, and `MediaType`.

Video results additionally expose video codec, resolution, frame rate, bit rate, aspect ratio, color, audio-stream, and text-stream information. Audio results expose codec, bit rate, channels, sampling rate, artwork count, and music metadata. Image results expose image format, resolution, bit depth, color space, and title.

Path, provider, and native read failures are written as non-terminating errors so other input files can continue. Non-filesystem providers are rejected.

## RELATED LINKS

[MediaInfo](https://mediaarea.net/MediaInfo)
[Get-MediaInfo README](../README.md)
