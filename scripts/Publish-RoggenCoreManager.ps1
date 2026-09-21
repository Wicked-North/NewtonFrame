param(
    [string]$Runtime = 'win-x64',
    [string]$Output = "$PSScriptRoot\..\artifacts\RoggenCore-Manager"
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot\..").Path
dotnet test (Join-Path $repo 'RoggenCore.sln') -c Release
if ($LASTEXITCODE) { exit $LASTEXITCODE }
Remove-Item $Output -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish (Join-Path $repo 'tools\RoggenCore.Manager\RoggenCore.Manager.csproj') -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o $Output
if ($LASTEXITCODE) { exit $LASTEXITCODE }
$zip = "$Output-$Runtime.zip"
Compress-Archive "$Output\*" $zip -Force
Get-FileHash $zip -Algorithm SHA256 | Format-List
