# Security

Recordings can contain private information in screenshots and window/control labels. Review every retained image and description before sharing. Redaction changes the saved pixels, but cannot erase copies you previously exported or shared.

StepVela uses local files only. It has no telemetry, upload endpoint, account system, or update service. Printable keyboard characters are not stored as text. Screenshots can still show typed text.

Guides are ZIP archives with a versioned JSON document. The loader reads only expected image names, limits archive and image sizes, and rejects path traversal. Open files only from sources you trust.

For a security issue, use the repository's **Security → Report a vulnerability** when available. Otherwise open an issue containing a minimal description and request a private reporting channel; do not post secrets, personal recordings, or a working exploit with sensitive data.

Version 0.1.x is the currently maintained series. Release binaries are unsigned; SHA-256 checksums verify file integrity, and do not establish publisher identity. Enterprise application-control policies may block execution.
