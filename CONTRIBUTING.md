# Contributing

StepVela is a small native Windows application. Changes should preserve that footprint and keep recording, editing, and export easy to understand.

1. Open an issue describing the problem or proposed behavior. Include your Windows version and reproducible steps; redact private information from screenshots.
2. Build with `./scripts/build.ps1 -Test` on Windows with .NET Framework 4.8 or newer.
3. For recording changes, test a harmless desktop workflow, pause/resume, finish, and reopen the saved guide. Verify that input still reaches the recorded application.
4. For image or export changes, check the image pixels, portable paths, and offline output. Keep archive and image limits intact.
5. Submit a focused pull request with what changed and how you tested it. Include light and dark screenshots for visible UI changes.

Avoid adding a browser runtime, cloud dependency, background service, telemetry, or startup task without discussing the need first. Use Windows accessibility and input APIs responsibly; never retrieve text-field values or password contents.

See [building](docs/BUILDING.md) and [verification](docs/VERIFICATION.md) for the release workflow. Contributions are licensed under the project's MIT license.
