# Build Portable Syncthing for Windows.
# -CompileOnly proves the Release build for pull-request verification and does
# not assign a date.build version. A real package requires -Version YYYY.M.D.N,
# stamps that version into the assembly, and writes the portable zip plus SHA256SUMS.

[CmdletBinding()]
param(
    [string]$Version = "",
    [switch]$CompileOnly,
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Project = "src/PortableSyncthing.Windows/PortableSyncthing.Windows.csproj",
    [string]$DistRoot = "dist"
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest

$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot
# .NET file APIs use Environment.CurrentDirectory, which does not always follow Set-Location.
[Environment]::CurrentDirectory = (Get-Location).Path

function Get-PythonCommand {
    foreach ($name in @("python", "python3")) {
        $cmd = Get-Command $name -ErrorAction SilentlyContinue
        if ($null -ne $cmd -and $cmd.Source) {
            return $cmd.Source
        }
    }
    throw "Python 3 is required to validate date.build versions. On Windows, America/Chicago also needs: python -m pip install tzdata"
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command"
    }
}

function Write-Utf8File {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Text
    )
    $normalized = $Text -replace "`r`n", "`n" -replace "`n", "`r`n"
    if (-not $normalized.EndsWith("`r`n")) {
        $normalized += "`r`n"
    }
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($fullPath, $normalized, $utf8)
}

$python = Get-PythonCommand

if ($CompileOnly) {
    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        throw "-CompileOnly does not accept -Version. Pull-request verification must not stamp a date.build version."
    }
    Write-Host "Compile-only: building $Configuration. Not assigning a date.build version or packaging a release."
    Invoke-Checked { & $python (Join-Path $RepoRoot "scripts/next-date-build-version.py") --self-test }
    Invoke-Checked { dotnet build $Project -c $Configuration }
    Write-Host "Compile-only build succeeded."
    exit 0
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "-Version YYYY.M.D.N is required unless -CompileOnly is set. This script does not assign the next build number."
}

$parsed = (& $python (Join-Path $RepoRoot "scripts/next-date-build-version.py") --from-tag $Version | Select-Object -Last 1)
if ($LASTEXITCODE -ne 0) {
    throw "Refusing to package invalid date.build version '$Version'."
}
$Version = "$parsed".Trim()
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version helper did not return a date.build version."
}

$publishDir = Join-Path $DistRoot $Runtime
$stageRoot = Join-Path $DistRoot "staging-$Version"
$layoutDir = Join-Path $stageRoot "PortableSyncthing"
$zipName = "PortableSyncthing-$Version-$Runtime.zip"
$zipPath = Join-Path $DistRoot $zipName

if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}
if (Test-Path $stageRoot) {
    Remove-Item -Recurse -Force $stageRoot
}
if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

Write-Host "Publishing self-contained $Runtime single-file build $Version"
Invoke-Checked {
    dotnet publish $Project `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        "-p:Version=$Version" `
        "-p:AssemblyVersion=$Version" `
        "-p:FileVersion=$Version" `
        "-p:InformationalVersion=$Version" `
        -p:IncludeSourceRevisionInInformationalVersion=false `
        -o $publishDir
}

$exePath = Join-Path $publishDir "PortableSyncthing.exe"
if (-not (Test-Path $exePath)) {
    throw "Publish did not produce $exePath"
}

$readme = @"
Portable Syncthing $Version

Run PortableSyncthing.exe from this folder. The folder is the portable root.
Nothing is installed as a service, and the app does not use a tray icon.

The first launch downloads the official Syncthing runtime, verifies its release SHA-256, and places syncthing.exe in bin\current. That runtime is not inside this zip.

The embedded Syncthing GUI uses the Evergreen WebView2 Runtime already installed on this PC. A fixed WebView2 runtime is not bundled with this build.
"@
Write-Utf8File -Path (Join-Path $publishDir "VERSION.txt") -Text $Version
Write-Utf8File -Path (Join-Path $publishDir "README.txt") -Text $readme

New-Item -ItemType Directory -Force -Path $layoutDir | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $layoutDir -Recurse -Force

$distFull = [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $DistRoot))
$stageFull = [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $stageRoot))
$zipFull = Join-Path $distFull $zipName
$sumsFull = Join-Path $distFull "SHA256SUMS"
if (Test-Path -LiteralPath $zipFull) {
    Remove-Item -LiteralPath $zipFull -Force
}
if (-not ("System.IO.Compression.ZipFile" -as [type])) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
}
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stageFull,
    $zipFull,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)

if (-not (Test-Path -LiteralPath $zipFull)) {
    throw "Expected zip was not created at $zipFull"
}

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipFull).Hash.ToLowerInvariant()
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($sumsFull, "$hash  $zipName`n", $utf8)

Remove-Item -Recurse -Force $stageRoot
Write-Host "Packaged $zipFull"
Write-Host "SHA256 $hash  $zipName"
