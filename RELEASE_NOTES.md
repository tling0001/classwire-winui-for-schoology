# Release notes

## [2026.10.0-port.51] - 2026-10-31

- Add a UI-thread-independent dispatcher watchdog that records when the WinUI message queue stops responding and captures a full-memory process dump with native thread information.
- Preserve startup logging while capturing the stalled process before an abrupt termination can erase the evidence; dumps are written under the user's Schoology Logs\\CrashDumps folder.


## [2026.10.0-port.50] - 2026-10-31

- Restore the original WinUI system-color-based High Contrast theme resources instead of resolving system brush aliases through StaticResource.
- Defer theme surface rebuilding until after native Windows theme/accessibility messages have returned, avoiding XAML tree mutations inside the window procedure.
- Preserve the CLR's native exception filter while retaining per-user Windows Error Reporting LocalDumps configuration.
- Add precise login-page and window-destruction breadcrumbs to distinguish dispatcher/XAML failures from an actual native window close.


## [2026.10.0-port.49] - 2026-10-31

- Add native crash diagnostics and per-user Windows Error Reporting full-memory dumps for startup failures that bypass managed exception handlers.
- Guard the native window-procedure callback against managed exceptions crossing the unmanaged boundary, and capture splash-timer and embedded-image startup failures.


## [2026.10.0-port.48] - 2026-10-31

- Replace the unsupported WinRT High Contrast change-event subscription with native Windows accessibility/theme notifications, preventing the COM event-source failure during startup while preserving live High Contrast theme updates.


## [2026.10.0-port.47] - 2026-10-31

- Fix unpackaged startup by constructing the empty main-window and shell-page roots without loading missing standalone XAML resources.
- Add launch-time and 12-hour automatic update checks plus a Settings update action, using the same confirmation dialog for both.
- Download architecture-matched GitHub installers, verify size and SHA-256 metadata, and perform silent per-user updates with automatic app restart.

## [2026.10.0-port.46] - 2026-10-31

- Restore the per-user Inno Setup installer EXE for x64 and ARM64, and return the WinUI project to unpackaged self-contained deployment. Remove the MSIX signing and certificate-trust requirement; GitHub Actions now publishes architecture-specific installer EXEs.

