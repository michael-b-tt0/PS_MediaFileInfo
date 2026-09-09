@{
    RootModule = 'MediaInfo.dll'
    ModuleVersion = '4.0.4'
    GUID = '115ad8ce-bfd9-4cb4-844c-e20fa04f2634'
    Author = 'Get-MediaInfo contributors'
    Copyright = '(c) Get-MediaInfo contributors. All rights reserved.'
    Description = 'MediaInfo integration for PowerShell.'
    PowerShellVersion = '7.6'
    CompatiblePSEditions = @('Core')
    ProcessorArchitecture = 'None'
    FormatsToProcess = @('GetMediaInfo.Format.ps1xml')
    FunctionsToExport = @()
    CmdletsToExport = @('Get-MediaFileInfo')
    VariablesToExport = @()
    AliasesToExport = @()
    PrivateData = @{
        PSData = @{
            Tags = @('MediaInfo', 'Multimedia', 'Metadata', 'Video', 'Audio', 'Image', 'FileInfo')
            ProjectUri = 'https://github.com/michael-b-tt0/PS_MediaFileInfo'
        }
    }
}
