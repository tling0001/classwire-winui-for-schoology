# ClassWire for Windows (WinUI 3)

A native Windows desktop client branded ClassWire, built with WinUI 3 and the Windows App SDK. This is a separate native application; it does not wrap Electron or Tauri and installs alongside the classic Schoology desktop app.

## Features

### Native Windows experience

- Native WinUI 3 shell with a left NavigationView integrated into the Windows title bar.
- Light and dark themes, high-contrast resources, Windows Mica/Acrylic surfaces, and a reduced-motion setting.
- Native Windows dialogs, flyouts, menus, notifications, and keyboard-accessible controls.
- Responsive navigation and content layouts, including narrow-window navigation behavior.

### Sign-in and account

- Schoology OAuth 1.0a sign-in with locally protected session storage; passwords are not retained.
- QR sign-in support where the required camera hardware is available.
- Parent-account support with a navigation student switcher when Schoology exposes linked student accounts.
- Session-aware authenticated Schoology WebView2 environment, with Schoology session cookies installed before navigation.

### Schoology learning and communication

- Home and Upcoming dashboards, course and group spaces, school profiles, members, materials, assignments, gradebook, discussions, resources, and events.
- Course-material folders with recursive inline expansion and lazy loading.
- Resources browsing for personal, shared, group, and supported resource collections; nested folders, creation, and permission-checked editing.
- Recent activity and course/group updates with shared post cards, comments, likes, image/file attachments, and polls.
- Inbox, unread, sent, and message-thread views with profile resolution, participant avatars, attachments, and message composition.
- Native assignment details, comments, student submission history, text/file submissions, and supported attachment viewing/downloading.
- Teacher grading for authorized section instructors, including student submission review, grade and comment editing, comment visibility, rubric criteria scores/comments, and supported total-grade overrides.
- Calendar and Upcoming event browsing, with supported native creation flows.

### ClassWire AI

- Optional AI assistant with Google Gemini, OpenAI, Anthropic, Ollama, and Puter provider choices.
- Schoology-aware tools for courses, groups, upcoming work, assignments, grades, materials, updates, message search, and message threads.
- Page-aware context and navigation to supported Schoology destinations.
- Proposed actions for supported assignment submissions, messages, course updates, and teacher grade changes require the user to review and explicitly confirm before execution.
- Provider/model availability and attachment support vary. Prompts and relevant Schoology context are sent to the selected provider; review provider terms before sharing education information.

### Attachments and embedded content

- Shared attachment actions for supported file types, with native document/image viewing and downloads.
- Authenticated WebView2 for supported Schoology pages, LTI content, assessments, and external tools.
- Schoology session cookies are installed into the shared WebView2 environment before authenticated navigation.

Some Schoology features depend on account permissions, school configuration, and server-side API support. Certain specialized workflows and file formats may still open in Schoology web content or may not yet have a native WinUI equivalent. Availability can vary by account and school.

## Build and installation

GitHub Actions publishes self-contained x64 and ARM64 builds and packages each as a **per-user Inno Setup installer EXE**. No MSIX package or publisher-certificate import is required. The installer does not request administrator privileges and installs to:

`%LOCALAPPDATA%\\Programs\\Schoology`

The WinUI executable and shortcut are installed alongside the existing Tauri app; the installer does not replace the Tauri executable. The uninstaller removes the WinUI files it installed without intentionally removing unrelated Tauri-owned files. The Windows App SDK runtime is bundled with the application. Build, publish, installer creation, and release publishing are performed by GitHub Actions; local builds and installer tests are intentionally not part of this repository workflow.

The installer supports unattended per-user installation and updates with Inno Setup's `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-` switches. The app checks GitHub Releases at launch (after the startup UI is ready) and every 12 hours, and Settings includes a manual **Check for updates** action. When an update is available, the app asks for confirmation, downloads the architecture-matched installer, verifies its size and SHA-256 digest when provided by GitHub, then launches it with silent-update switches and restarts ClassWire after installation. No administrator elevation or certificate import is requested.

The installer is currently unsigned. Windows SmartScreen may therefore show an unknown-publisher warning; this is separate from the MSIX publisher-trust error and does not require importing the old MSIX certificate.

## Development notes

- Project: `Schoology.WinUI/Schoology.WinUI.csproj`
- Target: WinUI 3 / Windows App SDK, .NET 10, x64 and ARM64.
- CI: `.github/workflows/build-windows.yml`
- Installer: `installer/Schoology-WinUI.iss`
- Release history: `RELEASE_NOTES.md`

The official Schoology Android 2026.10.0 source is the reference for Schoology API and authentication behavior. The supplied classic Tauri desktop port.28 source is the reference for desktop navigation, content organization, and expected workflows. The WinUI interface is adapted to native Windows controls rather than copying Android or web UI.
