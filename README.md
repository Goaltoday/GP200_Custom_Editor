# GP200 Community Editor

Independent Windows editor for the Valeton GP-200, maintained by [Goaltoday](https://github.com/Goaltoday). Supports **Stock** and **Custom** firmware profiles. Custom keeps the internal MX42 protocol identifier. This project is not an official Valeton product and is not endorsed by the hardware manufacturer.

Project-owned code: **GPL-3.0-only**. See [LICENSE](LICENSE) and [third-party notices](THIRD_PARTY_NOTICES.md).

## Features

- Live effect editing, parameters, bypass, insert-based block ordering, Undo/Redo and double-click reset.
- Preset library, Save and Save As, import/export, backup and verified device readback.
- Custom universal slots, NONE, parallel CHAIN, BLEND, ALIGN and phase inversion.
- Scenes A–D, BASE, recall/capture, pending changes and supported footswitch assignments.
- Native NAM loading to 32 stored slots, maximum one selected NAM block per preset, including bypassed blocks.
- NAM→CLO SnapTone conversion with Tone Match, optional corrective IR and Convert only.
- IR loading, global settings, expression assignments, MIDI settings and drums.

Firmware updates and factory reset are excluded. Backup and readback do not make multi-preset operations atomic; see [limitations](docs/LIMITATIONS.md).

## Requirements and build

Windows x64 and **.NET 10 SDK with Windows Desktop support**. PowerShell 7 is recommended. No SDK, firmware image or official editor installation is bundled or needed to build.

The GitHub Actions artifact includes a private .NET runtime for Windows x64 and does not require the user to install .NET. Runtime DLLs are stored under `runtime/`, not beside the editor executable. Build it locally with the .NET 10 SDK:

```powershell
.\scripts\build-app.ps1
```

Run `artifacts/app/GP200_Custom_Editor/GP200 Replica.exe`. Keep the `runtime`, `Data` and `Converter` folders alongside the executable. The application can connect automatically to a connected GP-200 according to its preferences. First verify the selected Stock/Custom profile in Settings.

The GitHub Actions artifact extracts to a short `README.txt` and a `GP200_Custom_Editor` folder. Open the folder and run `GP200 Replica.exe`; keep its contents together. The workflow does not create a GitHub Release. [Build details](docs/BUILD.md).

## Images

The source includes 208 project-created, unbranded images for AMP, CAB, DST, EQ, NR and WAH. Original manufacturer illustrations, official chain icons and generated WAH illustrations with visible brand marks are excluded. Other missing artwork uses the existing text fallback; effects and controls still work. [Artwork provenance and asset list](docs/ASSETS.md).

## Included native components

The small native conversion worker and IR resampler are included with their corresponding source under Native and their third-party notices. Rebuild them with CMake and MSVC using `scripts/build-native.ps1`. The normal editor build does not require a C++ compiler. See [native dependencies](THIRD_PARTY_NOTICES.md).

## Upload this folder

Publish the **contents of this folder**, including hidden .github/.gitignore/.gitattributes files. GitHub Desktop or Git is recommended for the vendored source tree. Do not upload the internal FUENTES handoff or its ZIP. GitHub Desktop can add this folder as a local repository and publish it. No remote repository has been created by these scripts. [Instrucciones en español](docs/PUBLICAR.md).

The supplied GitHub Actions workflow builds the Windows application folder with its private runtime under `runtime/` and uploads it as an artifact. It does not rebuild native components, create a GitHub Release, access a pedalboard, or flash firmware.
