# Verification — 0.1.0

Checked on 2026-10-14 on a Windows 11 development PC (build 26200, .NET Framework 4.8.1). These checks do not establish crash-free operation on every PC. Windows 10 and low-memory hardware testing remain on the roadmap.

## Automated checks

`./scripts/build.ps1 -Test` passes:

- Valid/invalid state transitions and stop while paused.
- Printable-key and AltGr classification; named shortcut handling.
- Screenshot marker, retention, and metadata-only export.
- Atomic overwrite; Unicode and HTML/Markdown escaping.
- JSON metadata and portable image paths, including negative monitor coordinates.
- Session timestamps and skipped-event warnings.
- Step reordering and edited guide content.
- Pixel redaction, JPEG persistence, and project reopening.
- Rejection of an image path that attempts archive traversal.

These automated tests use synthetic images, without desktop capture or input hooks.

## Installer

WiX source compilation and ICE validation passed. ICE91 reports the intentional current-user installation path. The final MSI completed fresh installation, repeat installation, repair, and uninstall with exit code **0** for each operation. The initial installer test also verified the Start Menu shortcut, installed version, and executable hash. Uninstall removed the application and shortcut while a saved guide remained intact.

The full interactive setup wizard has not been exercised automatically. The installer uses WiX's standard directory/license/finish dialogs with original StepVela artwork. Release binaries are unsigned.

## Live application checks

A harmless Notepad checklist workflow recorded four steps. Global start/pause/resume/finish controls worked from Notepad; an action during pause did not change the three-step count. Recording resumed and appended the fourth action. The 150 ms settling window captured the completed paste, including the second line.

The running editor accepted a guide title, step title, and description. Descriptions persisted when changing the selected step. Move up/down changed the step order and numbering. The image editor cropped the screenshot and applied a solid redaction to a selected word. Undo restored the cropped image. The live guide was saved and reopened; its JSON and HTML retained the edited content with zero skipped events or capture warnings. Light and dark workspaces were visually inspected.

## Footprint

| Measurement | Result |
| --- | ---: |
| Executable | 98,304 bytes (96 KiB) |
| Portable ZIP | 53,343 bytes (about 52 KiB) |
| MSI | 1,085,440 bytes (about 1.04 MiB) |
| Idle working set | 57.96 MiB |
| Idle private memory | 38.15 MiB |
| Idle CPU in a 10.05-second sample | 0 ms reported |
| Four-step recording working set | 78.61 MiB |
| Four-step recording private memory | 53.92 MiB |

The idle sample used the open, empty workspace after switching appearance. Memory varies with Windows, the runtime, retained images, and app accessibility providers. It excludes the shared installed .NET runtime. CPU timing has finite resolution; zero in this sample does not promise zero usage at all times.

The design bounds screenshot resolution to a 1600-pixel long edge, limits queued capture requests to 16, and allows only one outstanding accessibility lookup with a 400 ms wait. Recording uses a separate worker and a 150 ms settling window after an input event; busy capture may miss intermediate states. Idle timers and input hooks stop when recording finishes.
