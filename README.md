# Portable Syncthing for Windows

A standalone Windows application that owns the Syncthing process for one portable workspace. It is **not tray-focused**: it opens a normal window with a status dashboard, live Syncthing process logs, and the official Syncthing GUI embedded in the application.

## Portable layout

```text
PortableSyncthing\
├── PortableSyncthing.exe
├── bin\current\syncthing.exe      # verified managed runtime
├── data\
│   ├── syncthing\                 # Syncthing config.xml, database and device keys
│   ├── portable-folders.json       # folder-id → portable-relative-path map
│   ├── logs\syncthing.log
│   └── webview2\                  # embedded browser profile
└── updates\                        # verified staged release downloads
```

The first launch automatically downloads the matching official Syncthing runtime, verifies its release SHA-256 asset, and places it in `bin\current`. Future updates remain available through **Check and Update**.

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
dotnet publish .\src\PortableSyncthing.Windows\PortableSyncthing.Windows.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\dist\win-x64
```

Published releases are unsigned. They use the Evergreen WebView2 Runtime already installed on the PC. Bundling a fixed WebView2 runtime, so the app has no host WebView2 dependency, is a follow-up and is not required for this release. The app itself is intentionally `asInvoker`: it never requires elevation.

## Continuous integration

`.github/workflows/ci.yml` runs on pull requests, pushes to `main`, and manual `workflow_dispatch`. The job uses `windows-latest` with .NET 8 and Python 3:

- `python tests/test_contracts.py`
- `dotnet run --project tests/PortableSyncthing.ContractTests`
- `dotnet build src/PortableSyncthing.Windows/PortableSyncthing.Windows.csproj -c Release`
- an unsigned self-contained publish, uploaded as `PortableSyncthing-ci-<git-sha>-win-x64`

CI does not assign or bump a `YYYY.M.D.N` version and does not create a GitHub Release. The CI publish is stamped `0.0.0` with informational version `ci-<sha>` so it cannot be mistaken for a shipped date.build. `permissions` stay `contents: read`.

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

The zip contains a `PortableSyncthing\` folder with `PortableSyncthing.exe`, any sibling files from `dotnet publish` (including the WebView2 loader when the SDK emits it next to the exe), `VERSION.txt`, and a short `README.txt`. The first launch still downloads the Syncthing runtime into `bin\current`. The release is unsigned. Evergreen WebView2 must already be installed.

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
