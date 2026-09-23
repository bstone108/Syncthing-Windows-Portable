# Build Portable Syncthing for Windows.
# -CompileOnly proves the Release build for pull-request verification and does
# not assign a date.build version or download WebView2. A real package requires
# -Version YYYY.M.D.N, stamps that version into the assembly, downloads the pinned
# Fixed Version WebView2 Runtime, and writes the portable zip plus SHA256SUMS.

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

function Get-Sha256Lower {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

function Get-WebView2FixedRuntimePin {
    $pinPath = Join-Path $RepoRoot "scripts/webview2-fixed-runtime.json"
    if (-not (Test-Path -LiteralPath $pinPath)) {
        throw "Missing WebView2 Fixed Version pin: $pinPath"
    }
    $pin = Get-Content -LiteralPath $pinPath -Raw | ConvertFrom-Json
    foreach ($name in @("version", "architecture", "fileName", "url", "sha256", "cabBytes")) {
        $value = [string]$pin.$name
        if ([string]::IsNullOrWhiteSpace($value)) {
            throw "WebView2 pin is missing '$name'."
        }
    }
    if ([string]$pin.architecture -ne "x64") {
        throw "This portable app packages the win-x64 Fixed Version WebView2 runtime."
    }
    $expectedName = "Microsoft.WebView2.FixedVersionRuntime.$($pin.version).x64.cab"
    if ([string]$pin.fileName -ne $expectedName) {
        throw "WebView2 pin fileName must be $expectedName."
    }
    $url = [string]$pin.url
    if (-not $url.StartsWith("https://msedge.sf.dl.delivery.mp.microsoft.com/")) {
        throw "WebView2 pin URL must use the official Microsoft Edge download host."
    }
    if (-not $url.EndsWith("/$expectedName")) {
        throw "WebView2 pin URL must end with /$expectedName."
    }
    if ([string]$pin.sha256 -notmatch '^[0-9a-f]{64}$') {
        throw "WebView2 pin sha256 must be 64 lowercase hex characters."
    }
    if ([int64]$pin.cabBytes -le 0) {
        throw "WebView2 pin cabBytes must be a positive file size."
    }
    return $pin
}

function Save-WebView2Cab {
    param(
        [Parameter(Mandatory = $true)]
        $Pin,
        [Parameter(Mandatory = $true)]
        [string]$Destination
    )
    $ProgressPreference = "SilentlyContinue"
    $attempts = 3
    for ($attempt = 1; $attempt -le $attempts; $attempt++) {
        if (Test-Path -LiteralPath $Destination) {
            Remove-Item -LiteralPath $Destination -Force
        }
        try {
            Invoke-WebRequest -Uri ([string]$Pin.url) -OutFile $Destination -UseBasicParsing
            return
        }
        catch {
            if ($attempt -eq $attempts) { throw }
            Write-Host "WebView2 download failed (attempt $attempt of $attempts): $($_.Exception.Message)"
            Start-Sleep -Seconds (5 * $attempt)
        }
    }
}

function Install-FixedWebView2Runtime {
    param(
        [Parameter(Mandatory = $true)]
        $Pin,
        [Parameter(Mandatory = $true)]
        [string]$LayoutDirectory
    )
    $cacheDir = Join-Path $RepoRoot ".toolchain/webview2"
    New-Item -ItemType Directory -Force -Path $cacheDir | Out-Null
    $cabPath = Join-Path $cacheDir ([string]$Pin.fileName)
    $expectedHash = ([string]$Pin.sha256).ToLowerInvariant()
    $expectedBytes = [int64]$Pin.cabBytes
    $cached = (Test-Path -LiteralPath $cabPath) -and ((Get-Item -LiteralPath $cabPath).Length -eq $expectedBytes) -and ((Get-Sha256Lower $cabPath) -eq $expectedHash)
    if ($cached) {
        Write-Host "Using cached Fixed Version WebView2 $($Pin.version) x64"
    }
    else {
        Write-Host "Downloading Fixed Version WebView2 $($Pin.version) x64"
        Save-WebView2Cab -Pin $Pin -Destination $cabPath
    }
    $actualHash = Get-Sha256Lower $cabPath
    if ($actualHash -ne $expectedHash) {
        throw "WebView2 CAB SHA-256 mismatch. Expected $expectedHash, got $actualHash."
    }
    if ((Get-Item -LiteralPath $cabPath).Length -ne $expectedBytes) {
        throw "WebView2 CAB size mismatch. Expected $expectedBytes bytes."
    }

    $extractDir = Join-Path $cacheDir "extract-$($Pin.version)"
    if (Test-Path -LiteralPath $extractDir) {
        Remove-Item -LiteralPath $extractDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $extractDir | Out-Null
    $expand = Join-Path $env:SystemRoot "System32\expand.exe"
    if (-not (Test-Path -LiteralPath $expand)) {
        throw "Windows expand.exe is required to unpack the Fixed Version WebView2 CAB."
    }
    Write-Host "Expanding Fixed Version WebView2 into WebView2Runtime"
    # Invoke directly so function-local paths stay in scope. Invoke-Checked runs its
    # scriptblock in a child scope that cannot see those locals.
    & $expand $cabPath "-F:*" $extractDir
    if ($LASTEXITCODE -ne 0) {
        throw "expand.exe failed with exit code $LASTEXITCODE"
    }
    $browser = Get-ChildItem -LiteralPath $extractDir -Recurse -Filter "msedgewebview2.exe" -File | Select-Object -First 1
    if ($null -eq $browser) {
        throw "Fixed Version package did not contain msedgewebview2.exe."
    }
    $dest = Join-Path $LayoutDirectory "WebView2Runtime"
    if (Test-Path -LiteralPath $dest) {
        Remove-Item -LiteralPath $dest -Recurse -Force
    }
    Move-Item -LiteralPath $browser.Directory.FullName -Destination $dest
    if (Test-Path -LiteralPath $extractDir) {
        Remove-Item -LiteralPath $extractDir -Recurse -Force
    }
    if (-not (Test-Path -LiteralPath (Join-Path $dest "msedgewebview2.exe"))) {
        throw "WebView2Runtime is missing msedgewebview2.exe after extraction."
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

$webView2 = Get-WebView2FixedRuntimePin

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

The embedded Syncthing GUI uses the Fixed Version WebView2 Runtime $($webView2.version) bundled in WebView2Runtime (win-x64). The Evergreen WebView2 Runtime does not need to be installed. Keep this folder on a local drive. Fixed Version WebView2 cannot start from a UNC or network path.

On Windows 10, the first launch grants AppContainer read and execute access on WebView2Runtime. That grant requires NTFS. Windows 11 does not need the grant.
"@
Write-Utf8File -Path (Join-Path $publishDir "VERSION.txt") -Text $Version
Write-Utf8File -Path (Join-Path $publishDir "README.txt") -Text $readme

New-Item -ItemType Directory -Force -Path $layoutDir | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $layoutDir -Recurse -Force
Install-FixedWebView2Runtime -Pin $webView2 -LayoutDirectory $layoutDir

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
