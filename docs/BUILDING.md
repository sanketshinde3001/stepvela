# Build and package

## Application

Requirements: Windows 10/11, .NET Framework 4.8 or newer, and PowerShell. The build uses the framework's C# compiler and Windows Forms, Drawing, Compression, and UI Automation assemblies. No NuGet packages or browser runtime are required.

```powershell
git clone https://github.com/sanketshinde3001/stepvela.git
cd stepvela
./scripts/build.ps1 -Test
./build/StepVela.exe
```

The build is optimized, AnyCPU, and requests normal user privileges. `-Test` runs synthetic checks without recording the desktop and writes results to `test-output/`.

## Installer

Download the official [WiX 3.14.1 portable binaries](https://github.com/wixtoolset/wix3/releases/tag/wix3141rtm) and extract them to a tools directory. Run packaging where Windows Installer validation is available:

```powershell
./scripts/package.ps1 -WixDirectory C:\tools\wix314
```

Outputs in `dist/`:

- `StepVela-Setup.msi`: current-user installer, Start Menu shortcut, repair/uninstall support.
- `StepVela-Portable.zip`: executable, license, and quick start.
- `SHA256SUMS.txt`: SHA-256 hashes for those two downloads.

WiX ICE validation remains enabled. ICE91 warns that these files always belong to the current user's profile; this is intentional for this per-user package. Packaging requires access to the Windows Installer service.

## Release checklist

1. Update the three-part version in `installer/StepVela.wxs` and the four-part assembly version. Update quick start and changelog.
2. Generate a new Product ID for a new product version; retain the UpgradeCode and existing component GUIDs. The Product ID is fixed within one version to avoid duplicate installations.
3. Build and run self-tests. Test a live workflow and inspect every public screenshot.
4. Package, test fresh installation, repair, repeated install, and uninstall. Verify saved guides remain untouched. Test upgrades separately before claiming upgrade support.
5. Confirm the packaged executable matches the tested build. Publish a tag and the MSI, portable ZIP, and checksums as GitHub Release assets.

The optional `scripts/make-assets.py` regenerates original artwork with Python and Pillow. Generated artwork is already checked in, so Python is unnecessary for ordinary builds.
