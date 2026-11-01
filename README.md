# Schoology for Windows (WinUI 3)

A native Windows desktop client for Schoology, built with WinUI 3 and the Windows App SDK. This is a separate native application; it does not wrap Electron or Tauri and installs alongside the classic Schoology desktop app.

## Features

- Native WinUI 3 shell with a left NavigationView integrated into the Windows title bar.
- Light and dark themes, high-contrast resources, Windows Mica/Acrylic surfaces, and a reduced-motion setting.
- Schoology OAuth 1.0a sign-in with locally protected session storage; passwords are not retained.
- Home and Upcoming dashboards, course and group spaces, school profiles, members, materials, assignments, grades, discussions, resources, and events.
- Recent activity and course/group updates with shared post cards, comments, likes, image/file attachments, and polls.
- Inbox, unread, sent, and message-thread views with profile resolution and participant avatars.
- Native assignment details, comments, and submission information; LTI and assessment content can be opened in the shared WebView2 surface.
- Shared authenticated WebView2 environment for supported Schoology web content, with Schoology session cookies installed before navigation.
- Windows notifications, optional notification sound, startup/background operation, notification-area controls, and first-launch release notes.
- QR sign-in support where the required camera hardware is available.

## Build and installation

GitHub Actions publishes self-contained x64 and ARM64 builds and packages each as a **per-user Inno Setup installer EXE**. No MSIX package or publisher-certificate import is required. The installer does not request administrator privileges and installs to:

`%LOCALAPPDATA%\\Programs\\Schoology`

The WinUI executable and shortcut are installed alongside the existing Tauri app; the installer does not replace the Tauri executable. The uninstaller removes the WinUI files it installed without intentionally removing unrelated Tauri-owned files. The Windows App SDK runtime is bundled with the application. Build, publish, installer creation, and release publishing are performed by GitHub Actions; local builds and installer tests are intentionally not part of this repository workflow.

The installer supports unattended per-user installation and updates with Inno Setup's `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-` switches. The app checks GitHub Releases at launch (after the startup UI is ready) and every 12 hours, and Settings includes a manual **Check for updates** action. When an update is available, the app asks for confirmation, downloads the architecture-matched installer, verifies its size and SHA-256 digest when provided by GitHub, then launches it with silent-update switches and restarts Schoology after installation. No administrator elevation or certificate import is requested.

The installer is currently unsigned. Windows SmartScreen may therefore show an unknown-publisher warning; this is separate from the MSIX publisher-trust error and does not require importing the old MSIX certificate.

## Development notes

- Project: `Schoology.WinUI/Schoology.WinUI.csproj`
- Target: WinUI 3 / Windows App SDK, .NET 10, x64 and ARM64.
- CI: `.github/workflows/build-windows.yml`
- Installer: `installer/Schoology-WinUI.iss`
- Release history: `RELEASE_NOTES.md`

The official Schoology Android 2026.10.0 source is the reference for Schoology API and authentication behavior. The supplied classic Tauri desktop port.28 source is the reference for desktop navigation, content organization, and expected workflows. The WinUI interface is adapted to native Windows controls rather than copying Android or web UI.

## Current limitations

Some Schoology features depend on account permissions, school configuration, and server-side API support. Certain specialized workflows (including file-based assignment submission and some embedded third-party tools) may still open in Schoology web content or may not yet have a native WinUI equivalent. Availability can vary by account and school.
