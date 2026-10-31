# Build

Install the .NET 10 SDK for Windows x64, including Windows Desktop targeting packs. PowerShell 7 is recommended. Run from the repository root:

```powershell
.\scripts\build-app.ps1
```

The script publishes `artifacts/app/GP200_Custom_Editor/`. `GP200 Replica.exe` is a single-file, framework-dependent apphost; the matching private .NET 10 runtime installation is copied into `runtime/`. The apphost is configured to search that relative folder, so users do not need to install .NET and runtime DLLs do not clutter the app root. Project resources remain in `Data/` and `Converter/`, as expected by the editor. Keep all three folders with the executable.

The build restores Microsoft's .NET runtime packs from nuget.org; the application uses no NuGet packages. It keeps Spanish satellite resources (English uses neutral resources) and omits optional symbols and `createdump.exe`. GitHub Actions uploads `README.txt` beside `GP200_Custom_Editor/`; these paths give the artifact a common root at `artifacts/app/`, so the downloaded ZIP retains the app directory. It does not create a release or communicate with a pedalboard.

The optional `-Dotnet` parameter selects an installed dotnet executable.

## Native source

The runtime includes GP200ConversionWorker.exe and r8brain-gp200.dll with their corresponding source and licenses. An optional native rebuild requires CMake 3.24 or newer, MSVC C++20 and Windows SDK:

```powershell
.\scripts\build-native.ps1
```

The CMake project uses local vendor sources. It does not download anything or require an official editor, firmware BIN, model library or archived installer. `-Install` replaces the two native runtime files. Native builds can differ byte-for-byte with compiler/toolchain changes.


