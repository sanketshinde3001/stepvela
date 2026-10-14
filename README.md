<div align="center">
  <img src="assets/logo.png" alt="StepVela" width="80" height="80">
  <h1>StepVela</h1>
  <p>A lightweight, offline dynamic step recorder for Windows.</p>
  <p>Record a workflow. Refine the steps. Share a clear guide.</p>
  <p>
    <a href="https://github.com/sanketshinde3001/stepvela/releases/latest/download/StepVela-Setup.msi"><strong>Download installer</strong></a>
    · <a href="https://github.com/sanketshinde3001/stepvela/releases/latest/download/StepVela-Portable.zip">Portable ZIP</a>
    · <a href="https://github.com/sanketshinde3001/stepvela/issues">Report an issue</a>
  </p>
</div>

![StepVela light workspace](assets/workspace-light.jpg)

<details>
<summary>Contents</summary>

- [About](#about)
- [Features](#features)
- [Install](#install)
- [Use](#use)
- [Privacy](#privacy)
- [Built with](#built-with)
- [How it works](#how-it-works)
- [Performance and verification](#performance-and-verification)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [License and contact](#license-and-contact)
- [Acknowledgments](#acknowledgments)

</details>

## About

Explaining a desktop workflow often means taking screenshots, numbering them, and writing the same instructions again. StepVela turns your interactions into an editable guide, so you can focus on the instructions that matter.

Use it for onboarding, support instructions, bug reports, and personal documentation. Everything runs locally, and exported guides open without an account or internet connection.

## Features

| Capture | Refine | Share |
| --- | --- | --- |
| Mouse clicks and grouped scroll events | Editable guide and step titles | Offline HTML guide |
| Generic typing events and common shortcuts | Descriptions and manual steps | Markdown and structured JSON |
| Active-window or all-display screenshots | Reorder and remove steps | One portable export ZIP |
| Pause/resume and global controls | Replace, crop, highlight, or redact images | Reopen editable `.stepvela` projects |

Warm light and charcoal dark workspaces, native controls, and a compact codebase keep the app comfortable on everyday PCs.

<details>
<summary>Dark workspace</summary>

![StepVela dark workspace](assets/workspace-dark.jpg)

</details>

<details>
<summary>Crop, highlight, and solid redaction</summary>

![StepVela image editor applying solid redaction](assets/image-editor.jpg)

</details>

## Install

**Requirements:** Windows 10 or 11 with .NET Framework 4.8 or newer. The current release has been tested on Windows 11; Windows 10 support has not yet been verified on separate hardware.

1. Download [StepVela-Setup.msi](https://github.com/sanketshinde3001/stepvela/releases/latest/download/StepVela-Setup.msi).
2. Open it, follow the setup wizard, and launch **StepVela** from the Start Menu.
3. Uninstall or repair through Windows **Settings → Apps**. Saved guides remain yours.

The installer targets your Windows user profile and needs no background service or startup task. For portable use, extract [StepVela-Portable.zip](https://github.com/sanketshinde3001/stepvela/releases/latest/download/StepVela-Portable.zip) and open `StepVela.exe`.

**Publisher trust:** the initial release is unsigned. Windows SmartScreen or your organization's application-control policy may flag or block it. Source and [SHA-256 checksums](https://github.com/sanketshinde3001/stepvela/releases/latest/download/SHA256SUMS.txt) are available for inspection; checksums are an integrity check, not a signing certificate. Keep your organization's security policy in place.

To compare a download's hash with the release checksum:

```powershell
Get-FileHash .\StepVela-Setup.msi -Algorithm SHA256
```

## Use

1. Choose your capture area in **Settings**, then select **Record**.
2. Perform the workflow in another application. Use **Pause** when you need a break and **Finish** when done.
3. Select a step to change its title or description. Use **Edit image** for crop, highlight, and solid pixel redaction. **Replace** supplies a new image; **Add step** adds an instruction manually.
4. **Save guide** creates an editable `.stepvela` file. **Export** creates a ZIP containing `report.html`, `guide.md`, `data.json`, and retained screenshots.
5. Extract the ZIP and open `report.html`. Use your browser's Print dialog if you need a PDF.

Try the [checklist example](examples/checklist.stepvela): download the file, select **Open** in StepVela, and inspect its four recorded Notepad steps. The last image was cropped in the image editor. All sample text was created for this demonstration.

| Shortcut | Action |
| --- | --- |
| `Ctrl + Shift + F8` | Start, pause, or resume |
| `Ctrl + Shift + F9` | Finish |

Shortcuts are available while StepVela is open and may conflict with another application's registrations. The toolbar remains available.

The default captures the active window and keeps the most recent **25 screenshots**. Settings allow **1–999** retained images, or metadata-only recording. Step descriptions remain when older images are removed.

## Privacy

- No account, telemetry, cloud uploads, or automatic update service.
- Printable keyboard characters and text-field values are not stored as text. Screenshots can still show text you typed.
- Window and accessibility labels can include private information. Review both images and text before sharing.
- Redaction changes the pixels saved in the current guide; it cannot change copies previously exported or shared.
- Temporary recording images live under `%LOCALAPPDATA%\StepVela\Sessions`. Normal close removes the current temporary session. A crash or external file lock can leave a session folder behind.

Read [SECURITY.md](SECURITY.md) for file-handling limits and vulnerability reporting.

## Built with

- C# and .NET Framework 4.8
- Windows Forms and GDI screen capture
- Windows input hooks and UI Automation
- ZIP, JSON, HTML, and Markdown
- WiX 3.14 for the current-user MSI

No Electron, embedded browser, NuGet dependency, or bundled runtime. [Build instructions](docs/BUILDING.md) include the application and installer commands.

## How it works

1. Native input hooks enqueue events while allowing Windows to deliver them normally.
2. A separate worker waits briefly for the target app to update, captures a bounded screenshot, and requests accessible control labels with a timeout.
3. The editor keeps step metadata separate from temporary images and writes saves through a temporary file before replacing the destination.
4. Export bundles local assets. Import accepts the versioned document and expected image paths with bounded sizes.

This repository ships a finished application rather than an agent workflow, so it does not require an agent skill to use or build.

## Performance and verification

Screenshots have a maximum **1600-pixel long edge**. The capture queue holds **16 requests**, and guides are limited to **10,000 steps**. When the recorder cannot keep up, it reports skipped events instead of silently claiming a complete guide. Idle capture timers and hooks are stopped.

See [verification results](docs/VERIFICATION.md) for measured memory, package sizes, live recording checks, and installer results. Measurements describe one development PC, not a guarantee for every device.

Known limits: elevated/secure desktop windows may be inaccessible; app accessibility providers vary; rapidly changing screens can be captured after a later change; protected content may appear blank. Imported guides are limited to 256 MB, and replacement images to 16 MB and 16 million pixels. There is no video recording or cloud collaboration.

## Roadmap

- Code signing and additional Windows 10/11 and low-memory hardware testing
- Crash-session recovery
- More annotation tools and export layouts
- Verified upgrade tests across released versions

## Contributing

Issues and focused pull requests are welcome. Include reproducible steps and the checks you ran; add light and dark screenshots for UI changes. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License and contact

MIT © 2026 Sanket Rajendra Shinde. See [LICENSE](LICENSE).

[GitHub profile](https://github.com/sanketshinde3001) · [Issues](https://github.com/sanketshinde3001/stepvela/issues) · [Releases](https://github.com/sanketshinde3001/stepvela/releases)

## Acknowledgments

[Best-README-Template](https://github.com/othneildrew/Best-README-Template) inspired this README's organization, also used in [Qwen Image Studio](https://github.com/sanketshinde3001/qwen-image-studio). [OpenSteps](https://github.com/ebanez8/openstep) and [Folge](https://folge.me) helped inform the feature research. StepVela's implementation and artwork are original to this project.
