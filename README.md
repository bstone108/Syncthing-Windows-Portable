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

A fixed WebView2 runtime should be bundled with a release for strict no-host-dependency portability. The app itself is intentionally `asInvoker`: it never requires elevation.

## Test contracts

```powershell
dotnet run --project .\tests\PortableSyncthing.ContractTests
python .\tests\test_contracts.py
```
