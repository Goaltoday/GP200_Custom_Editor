# Build

Install the .NET 10 SDK for Windows x64, including Windows Desktop targeting packs. PowerShell 7 is recommended. Run from the repository root:

```powershell
.\scripts\build-app.ps1
```

The script publishes a self-contained Windows x64 application directory at `artifacts/app/`, including `GP200 Replica.exe`, the .NET runtime and application resources. It restores only Microsoft's .NET runtime packs from nuget.org; the application uses no NuGet packages. The publish keeps Spanish satellite resources (English uses neutral resources) and omits optional symbols and `createdump.exe`. GitHub Actions runs the same build and uploads the complete folder as an artifact. It does not create a release or communicate with a pedalboard.

The optional `-Dotnet` parameter selects an installed dotnet executable.

## Native source

The runtime includes GP200ConversionWorker.exe and r8brain-gp200.dll with their corresponding source and licenses. An optional native rebuild requires CMake 3.24 or newer, MSVC C++20 and Windows SDK:

```powershell
.\scripts\build-native.ps1
```

The CMake project uses local vendor sources. It does not download anything or require an official editor, firmware BIN, model library or archived installer. `-Install` replaces the two native runtime files. Native builds can differ byte-for-byte with compiler/toolchain changes.


