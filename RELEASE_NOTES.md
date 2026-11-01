# Release notes

## [2026.10.0-port.38] - 2026-10-31

- Trust the per-run self-signed MSIX certificate in the ephemeral GitHub Actions runner's CurrentUser root store before SignTool verification, while continuing to verify package signatures and exporting the public certificate for end users.

## [2026.10.0-port.37] - 2026-10-31

- Fix MSIX capability ordering by declaring the required `runFullTrust` restricted capability before the webcam device capability, as required by the package manifest schema.
- Target the Windows desktop device family for this packaged WinUI desktop application.

## [2026.10.0-port.36] - 2026-10-31

- Correct MSIX manifest logo asset paths to use valid single-backslash package-relative separators, resolving MakeAppx manifest schema validation failure.

## [2026.10.0-port.35] - 2026-10-31

- Enable the Windows SDK's MSIX tooling explicitly in the WinUI project so WinApp CLI project packaging generates the package manifest and MSIX payload for x64 and ARM64.

## [2026.10.0-port.34] - 2026-10-31

- Use the WinApp CLI's supported project-packaging command to generate signed x64 and ARM64 MSIX packages; the previous MSBuild Build target only compiled the app and did not create packages.
- Include both the self-contained .NET runtime and Windows App SDK runtime in the MSIX deployment.
- Resolve the remaining CA1826 and CA1859 warnings in assignment grade lookup and attachment parsing.
