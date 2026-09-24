# Portable Syncthing for Windows

A standalone Windows application that owns the Syncthing process for one portable workspace. It is **not tray-focused**: it opens a normal window with a status dashboard, live Syncthing process logs, and the official Syncthing GUI embedded in the application.

## Portable layout

```text
PortableSyncthing\
├── PortableSyncthing.exe
├── WebView2Runtime\               # Fixed Version WebView2 Runtime (win-x64)
│   └── msedgewebview2.exe
├── bin\current\syncthing.exe      # verified managed runtime
├── data\
│   ├── syncthing\                 # Syncthing config.xml, database and device keys
│   ├── portable-folders.json       # folder-id → portable-relative-path map
│   ├── logs\syncthing.log
│   └── webview2\                  # embedded browser profile
└── updates\                        # verified staged release downloads
```

The first launch automatically downloads the matching official Syncthing runtime, verifies its release SHA-256 asset, and places it in `bin\current`. Future updates remain available through **Check and Update**. `WebView2Runtime\` is already in the zip, so that first launch does not need the Evergreen WebView2 Runtime.

Nothing is installed as a service. The app does not create a tray icon. Closing its main window asks Syncthing to shut down through its local REST API, waits briefly, and force-stops only the child process if graceful shutdown fails.

## Folder portability

Before every `syncthing serve` launch, the app:

1. reads `data\syncthing\config.xml`;
2. loads the durable `data\portable-folders.json` mapping;
3. discovers any folders already under the current portable root;
4. rewrites only mapped paths to the current drive letter/root; and
5. atomically saves the configuration before Syncthing starts.

External folders are deliberately **not** changed. Do not run two copied portable directories at once: their `cert.pem`/`key.pem` identify the same Syncthing device.

## Syncthing runtime updates

**Check and Update** queries the official `syncthing/syncthing` release API, downloads the matching Windows archive, requires the release SHA-256 asset, verifies the archive, stages it beneath `data\updates`, backs up the previous executable to `bin\previous`, and only then replaces `bin\current\syncthing.exe`.

The app launches Syncthing with `--no-upgrade`; binary updates remain owned by this wrapper so that portable paths and rollback are controlled in one place.

## Build

The Windows UI targets `net8.0-windows` and WebView2. Build it on Windows with the .NET 8 SDK:

```powershell
dotnet build .\src\PortableSyncthing.Windows\PortableSyncthing.Windows.csproj -c Release
./scripts/package-windows.ps1 -CompileOnly
./scripts/package-windows.ps1 -Version 2026.9.23.1
```

`-CompileOnly` checks the Release build and does not download WebView2 or assign a date.build version. The versioned command is what writes the portable zip. A plain `dotnet publish` does not include `WebView2Runtime\`; use `package-windows.ps1` for a folder that can start the GUI.

Published releases are unsigned. The app is intentionally `asInvoker`: it never requires elevation.

## Fixed Version WebView2

Release zips bundle Microsoft's Fixed Version WebView2 Runtime for win-x64 in `WebView2Runtime\`, next to `PortableSyncthing.exe`. On startup the app calls `CoreWebView2Environment.CreateAsync` with that folder as `browserExecutableFolder` and `data\webview2` as the browser profile. The Evergreen WebView2 Runtime does not need to be installed. The GUI can open without a WebView2 download.

The package pin is `scripts/webview2-fixed-runtime.json`:

- Fixed Version `153.0.4234.48`, x64
- CAB `Microsoft.WebView2.FixedVersionRuntime.153.0.4234.48.x64.cab` (308,509,880 bytes, about 294 MB)
- SHA-256 `11e8240cb0bc56dcd3e4498907203c251346f65107fe35a3a13e152c7d51c79e`
- URL from the [WebView2 download page](https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download-section), host `msedge.sf.dl.delivery.mp.microsoft.com`
- Extracted runtime about 668 MB (699,773,121 bytes) under `WebView2Runtime\`, including `msedgewebview2.exe`

The CAB is not committed. Packaging downloads it (cached under gitignored `.toolchain\webview2\`), checks the size and SHA-256, expands it with `expand.exe -F:*`, and moves the folder that contains `msedgewebview2.exe` to `WebView2Runtime\`. Pull-request CI does not download it. The Release workflow downloads it only when it builds a date.build zip.

Microsoft keeps recent Fixed Version CABs on that page, and the CDN link includes a file id that can change if the package is republished. To move the pin, download the x64 Fixed Version CAB you want, replace `version`, `fileName`, `url`, `sha256`, and `cabBytes` in the pin file, and leave the CAB out of git.

Keep the portable folder on a local drive. Fixed Version WebView2 cannot start from a UNC path. On Windows 10, Fixed Version 120 and later run the renderer in an AppContainer, so the first launch grants All Application Packages and All Restricted Application Packages read and execute on `WebView2Runtime` (`icacls`, including files already in the folder). That grant needs NTFS. Windows 11 does not need it. Later launches skip the grant when `msedgewebview2.exe` already has those ACEs.

## Continuous integration

`.github/workflows/ci.yml` runs on pull requests, pushes to `main`, and manual `workflow_dispatch`. The job uses `windows-latest` with .NET 8 and Python 3:

- `python tests/test_contracts.py`
- `dotnet run --project tests/PortableSyncthing.ContractTests`
- `dotnet build src/PortableSyncthing.Windows/PortableSyncthing.Windows.csproj -c Release`
- an unsigned self-contained publish, uploaded as `PortableSyncthing-ci-<git-sha>-win-x64`

CI does not assign or bump a `YYYY.M.D.N` version and does not create a GitHub Release. The CI publish is stamped `0.0.0` with informational version `ci-<sha>` so it cannot be mistaken for a shipped date.build. That publish folder does not include `WebView2Runtime`; the date.build zip from the Release workflow does. `permissions` stay `contents: read`.

## Security

`.github/workflows/codeql.yml` runs CodeQL for C# (`security-extended`) on pushes to `main` and `cursor/**`, on pull requests, and weekly. Dependabot alerts are enabled. Automated Dependabot pull requests are off, so dependency findings stay alerts and do not open version-update or security pull requests.

## Versioning

Real releases use `year.month.day.build` in America/Chicago, with the month, day, and build unpadded. Example: `2026.9.23.1`. The git tag is `v2026.9.23.1`.

`N` resets each Central-time calendar day. It is one higher than any existing git tag or GitHub release for that day, or `1` when none exist. Padded spellings such as `2026.09.23.01` are not this scheme.

`scripts/next-date-build-version.py` is the only assigner. It reads git tags and `gh` releases. Pull requests and CI must not call it to assign a version. `--self-test` checks the Chicago date rules. `--from-tag vYYYY.M.D.N` reuses that tag and does not increment `N`.

`VERSION.txt` is written into the release zip at package time. It is not committed on pull requests, because there is no shipped version until a release is cut.

On Windows, the helper needs IANA time zone data: `python -m pip install tzdata`.

## Cutting a release

After this workflow is on `main`, cut the first release from GitHub:

1. Open **Actions → Release → Run workflow**.
2. Select branch `main`.
3. Leave the version input empty.

The workflow assigns the next America/Chicago date.build (for example `2026.9.23.1` on the first publish that Central-time day), builds the self-contained win-x64 single-file app, and publishes GitHub Release `v{version}` titled `Portable Syncthing {version}`. Assets:

- `PortableSyncthing-{version}-win-x64.zip`
- `SHA256SUMS`

The zip contains a `PortableSyncthing\` folder with `PortableSyncthing.exe`, `WebView2Runtime\` (the pinned Fixed Version WebView2 Runtime), any sibling files from `dotnet publish` (including the WebView2 loader when the SDK emits it next to the exe), `VERSION.txt`, and a short `README.txt`. The first launch still downloads the Syncthing runtime into `bin\current`. The release is unsigned. Evergreen WebView2 is not required.

That run creates the tag. The tag push starts the Release workflow again; if the GitHub Release already exists, the second run skips instead of rebuilding or overwriting it.

To publish a tag you created yourself, push `vYYYY.M.D.N` that does not already have a release. The workflow packages that exact version (`--from-tag`) and publishes it. It rejects padded tags.

To publish a specific version from the Actions form, enter `YYYY.M.D.N`. If that tag or release already exists, the run fails instead of overwriting it. Leave the input empty to take the next free `N`.

Real publishes share one concurrency queue, so two runs cannot take the same `N`. Pull requests on the Release workflow compile only: they do not assign a version and they do not publish.

Local packaging (this does not create the GitHub Release):

```powershell
./scripts/package-windows.ps1 -CompileOnly
./scripts/package-windows.ps1 -Version 2026.9.23.1
```

`-CompileOnly` is the pull-request check. The versioned command expects an already chosen `YYYY.M.D.N` and writes `dist\PortableSyncthing-<version>-win-x64.zip` plus `dist\SHA256SUMS`. Prefer the Release workflow so the next `N` is chosen from tags and GitHub releases together.

## Test contracts

```powershell
dotnet run --project .\tests\PortableSyncthing.ContractTests
python .\tests\test_contracts.py
```
