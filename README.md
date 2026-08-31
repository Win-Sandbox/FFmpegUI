# FFmpegUI

A modern **Fluent UI frontend for FFmpeg** for Windows.

FFmpegUI provides a native graphical interface for common FFmpeg workflows, allowing users to transcode, trim, merge, compress, extract, convert images, inspect media information, play media, and run advanced FFmpeg operations without having to construct command-line arguments manually.

The project is designed around Windows-native technologies and Microsoft's Fluent Design language, with FFmpeg remaining the underlying media-processing engine.

> **Status:** Early-stage open-source project. The repository is actively under development and may contain incomplete features or breaking changes.

## Features

- 🎬 **Media transcoding** — Configure common video and audio encoding operations through a graphical interface.
- ✂️ **Trim media** — Cut media files using a dedicated trimming workflow.
- 🔗 **Merge media** — Combine multiple media inputs into a single output.
- 📦 **Compress media** — Reduce media size through configurable encoding options.
- 📤 **Extract media** — Extract streams or media components from existing files.
- 🖼️ **Image conversion** — Convert images between supported formats using FFmpeg-based processing.
- 🔍 **Media probing** — Inspect detailed media metadata through `ffprobe`.
- ▶️ **Media playback** — Preview media through `ffplay`.
- ⚙️ **Advanced FFmpeg controls** — Access lower-level FFmpeg options when the standard workflows are insufficient.
- 📋 **Task queue** — Manage encoding and processing tasks through a dedicated task interface.
- 💾 **Presets** — Save and reuse frequently used processing configurations.
- 🧩 **Command generation** — Builds FFmpeg, FFprobe, and FFplay command lines from structured application settings.
- 🔎 **FFmpeg detection** — Locates the FFmpeg toolchain used by the application.
- 🈶 **Localization infrastructure** — Uses Windows resource management and currently includes Simplified Chinese resources.
- 🖥️ **High-DPI support** — Configured for per-monitor DPI awareness.
- 📦 **Self-contained deployment** — Configured as an unpackaged, self-contained Windows x64 application.

## Why FFmpegUI?

FFmpeg is extremely powerful, but its command-line interface exposes users directly to a large number of codecs, containers, filters, stream mappings, flags, and encoding parameters.

FFmpegUI puts a graphical layer between the user and FFmpeg:

```text
User
  │
  ▼
Fluent UI
  │
  ▼
ViewModels / Models
  │
  ▼
Command Builders
  │
  ├── ffmpeg
  ├── ffprobe
  └── ffplay
  │
  ▼
Media Processing
```

The application does **not** attempt to replace FFmpeg. Instead, it provides a structured interface for constructing and executing FFmpeg commands.

## Supported Workflows

The current project contains dedicated pages and view models for:

| Workflow | Description |
|---|---|
| Transcode | Encode or convert media using FFmpeg |
| Trim | Cut media to a selected time range |
| Merge | Combine media inputs |
| Compress | Reduce file size through encoding |
| Extract | Extract media components |
| Image Convert | Convert image formats |
| Play | Preview media with FFplay |
| Probe | Inspect media information with FFprobe |
| Advanced | Configure advanced FFmpeg operations |
| Presets | Manage reusable command presets |
| Tasks | Monitor processing tasks |
| Settings | Configure application behavior |

These workflows correspond to the current View and ViewModel structure in the repository.

## Technology Stack

| Component | Technology |
|---|---|
| Language | C# |
| UI | WinUI 3 / XAML |
| Runtime | .NET 8 |
| Windows framework | Windows App SDK 2.0.1 |
| MVVM | CommunityToolkit.Mvvm 8.2.2 |
| Media engine | FFmpeg |
| Media inspection | FFprobe |
| Media playback | FFplay |
| Target architecture | x64 |
| Target platform | Windows |
| License | AGPL-3.0 |

The project targets `net8.0-windows10.0.22621.0`, has a minimum platform version of Windows 10 build 17763, and uses Windows App SDK 2.0.1, Windows SDK Build Tools 10.0.26100.4654, and CommunityToolkit.Mvvm 8.2.2.

## Architecture

FFmpegUI follows a WinUI/MVVM-oriented architecture.

```text
FFmpegUI/
├── Controls/
├── Helpers/
├── Models/
├── Services/
├── Strings/
│   └── zh-CN/
├── ViewModels/
├── Views/
├── App.xaml
├── App.xaml.cs
├── MainWindow.xaml
├── MainWindow.xaml.cs
├── FFmpegUI.csproj
├── FFmpegUI.slnx
├── app.manifest
└── LICENSE
```

### Models

The `Models` directory contains the application's domain objects and configuration models, including:

- `EncodingTask`
- `FfmpegOptions`
- `FfplayOptions`
- `FfprobeOptions`
- `ImageConvertOptions`
- `MediaInfo`
- `OutputNameOptions`
- `Preset`
- `CommandTemplate`

### ViewModels

The ViewModel layer separates UI state and commands from the XAML views.

Current ViewModels include:

- `TranscodeViewModel`
- `TrimViewModel`
- `MergeViewModel`
- `CompressViewModel`
- `ExtractViewModel`
- `ImageConvertViewModel`
- `PlayViewModel`
- `ProbeViewModel`
- `AdvancedViewModel`
- `PresetsPageViewModel`
- `TasksViewModel`
- `TaskPageViewModel`
- `SettingsViewModel`

citeturn2view0

### Services

The service layer contains the core FFmpeg integration and application infrastructure:

- `FfmpegLocator` — Locates the FFmpeg executable.
- `FfmpegCommandBuilder` — Builds FFmpeg command lines.
- `FfmpegRunner` — Executes FFmpeg processes.
- `FfprobeCommandBuilder` — Builds FFprobe commands.
- `FfprobeService` — Retrieves media information.
- `FfplayCommandBuilder` — Builds FFplay commands.
- `FfplayHost` — Handles FFplay integration.
- `CodecCatalog` — Provides codec-related information.
- `ImageFormatCatalog` — Provides image-format information.
- `ImageCapabilityService` — Determines image-processing capabilities.
- `ImageConverter` — Handles image conversion.
- `TaskQueueService` — Manages processing tasks.
- `PresetStore` — Stores presets.
- `TemplateService` — Handles command templates.
- `SettingsService` / `AppSettings` — Manage application settings.


This separation keeps FFmpeg process execution and command construction out of the UI layer.

## FFmpeg Integration

FFmpegUI treats FFmpeg as an external processing engine.

The application constructs commands from structured models rather than requiring users to manually write complete command lines.

For example:

```text
UI options
    ↓
FfmpegOptions
    ↓
FfmpegCommandBuilder
    ↓
ffmpeg.exe
    ↓
Output file
```

The same architecture is used for FFprobe and FFplay.

This approach makes it possible to provide high-level workflows while still retaining an advanced path for users who need direct control over FFmpeg options.

## Requirements

To build FFmpegUI from source, you will need:

- Windows 10 or later
- .NET 8 SDK
- A Windows development environment capable of building WinUI 3 applications
- Windows App SDK dependencies restored through NuGet
- x64 build environment
- FFmpeg / FFprobe / FFplay binaries available to the application when running media operations

The project itself is configured for a self-contained Windows App SDK deployment, but the FFmpeg toolchain remains the actual media-processing backend.

## Building from Source

Clone the repository:

```powershell
git clone https://github.com/Win-Sandbox/FFmpegUI.git
cd FFmpegUI
```

Restore dependencies:

```powershell
dotnet restore
```

Build the project:

```powershell
dotnet build -c Release
```

Run the application:

```powershell
dotnet run -c Release
```

## Publishing

The project is configured for:

- `win-x64`
- Self-contained deployment
- Windows App SDK self-contained deployment
- Unpackaged deployment
- Per-monitor DPI awareness
- `resources.pri` resource packaging

These settings are defined in `FFmpegUI.csproj`. citeturn1view0

A release build can be published with:

```powershell
dotnet publish -c Release -r win-x64
```

## Design Philosophy

FFmpegUI follows three main principles:

### Native Windows

The application is built with WinUI 3 and XAML rather than a browser-based frontend or cross-platform UI toolkit.

### Fluent UI

The interface follows Microsoft's Fluent Design language and uses Windows-native controls where practical.

### FFmpeg First

FFmpeg remains the actual media-processing engine. FFmpegUI focuses on making FFmpeg easier to use without hiding its capabilities behind a proprietary processing layer.

The result is intended to be a graphical frontend rather than a replacement media engine.

## Localization

The project uses Windows resource management and currently declares `zh-CN` as its default language. The repository contains a `Strings/zh-CN` resource directory.

Additional languages can be added through the existing resource structure.

## Current Development Status

The repository is currently at an early development stage. The public repository contains a small initial history and no published releases at the time of writing.

The current codebase already establishes the major application layers and several media-processing workflows, but the project should be considered **work in progress** rather than a finished production application.

Potential future work may include:

- More complete FFmpeg option coverage
- Improved codec and format discovery
- More advanced filter support
- Better task progress reporting
- More robust error handling
- More extensive preset functionality
- Additional localization
- Improved media preview
- More polished Fluent UI interactions
- Packaged distribution and release automation

Future features are subject to change.

## Contributing

Contributions are welcome.

Before submitting a pull request:

1. Fork the repository.
2. Create a dedicated feature or fix branch.
3. Keep changes focused.
4. Follow the existing MVVM and service-layer architecture.
5. Test the application on a supported Windows environment.
6. Document significant behavioral or architectural changes.
7. Submit a pull request with a clear description.

Bug reports and feature requests can be submitted through GitHub Issues.

## License

FFmpegUI is licensed under the **GNU Affero General Public License v3.0 (AGPL-3.0)**.

See [`LICENSE`](LICENSE) for the complete license text.

## FFmpeg

FFmpegUI relies on the FFmpeg project for media processing.

FFmpeg is an independent open-source multimedia framework. FFmpegUI is a separate project and is not affiliated with or endorsed by the FFmpeg project.

For FFmpeg licensing and distribution requirements, refer to the FFmpeg project's official documentation and license information.

## Acknowledgements

- **FFmpeg** — Media processing, encoding, decoding, probing, and playback.
- **Microsoft WinUI 3 / Windows App SDK** — Native Windows UI and application platform.
- **CommunityToolkit.Mvvm** — MVVM infrastructure.

---

**FFmpegUI** — A modern Fluent UI frontend for FFmpeg on Windows.
