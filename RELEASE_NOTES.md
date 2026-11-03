# Release notes

## [2026.10.0-port.69] - 2026-11-02

- Fix the remaining analyzer warnings in URL-prefix detection and inline post-image URL extraction.

## [2026.10.0-port.68] - 2026-11-02

- Put File and Resource actions side by side in the assignment upload submission dialog.
- Add a native Messages composer with recipient search, subject and message fields, file/resource attachments, and Send; refresh Sent after successful delivery.
- Release shell page content when navigating away so returning from another user's profile safely restores the previous page.


## [2026.10.0-port.67] - 2026-11-02

- Run `taskkill /F /IM Schoology.WinUI.exe` in the installer handoff before starting the update installer, then verify the app process has exited.
- Fix likes-dialog people rows so pointer and keyboard activation open the selected user's profile, following the working Members list item interaction pattern.


## [2026.10.0-port.66] - 2026-11-02

- Show startup release notes only for the exact version installed locally, rather than the newest published release; users now see a release's notes after updating to that release.


## [2026.10.0-port.65] - 2026-11-02

- Restore the native WinUI NavigationView appearance by removing custom pane, foreground, hover, selection, and background resource overrides.
- Make the PDF/image viewer canvas, scroll viewport, and PDF page surrounds transparent so the underlying shell surface remains visible instead of applying a separate acrylic tint.


## [2026.10.0-port.64] - 2026-11-02

- Fix the WinUI 3 build by applying the NavigationView acrylic pane through its supported control-template resource keys instead of the unavailable PaneBackground property.


## [2026.10.0-port.63] - 2026-11-02

- Set the NavigationView pane's actual PaneBackground to translucent acrylic rather than relying on theme resource keys that left the default gray pane visible.
- Make the shell content Page transparent so Mica and acrylic can show through message panes, cards, and the native PDF/image viewer.
- Reduce acrylic tint on message sections, shared cards, and viewer surroundings; keep rendered document pages readable.
- Keep the Resources back control visible and enable it whenever folder history exists.
- Add a contextual Back button to other users' profiles, restoring the exact previous page when available.
- Replace likes-dialog card stacks with virtualized WinUI ListViews; activating a person closes the dialog and opens their profile.


## [2026.10.0-port.62] - 2026-11-02

- Reduce the NavigationView pane's acrylic tint so the native Mica backdrop is visible instead of a nearly opaque gray surface.
- Apply restrained acrylic to shared custom cards, course dashboard cards, mailbox panes, and message loading surfaces, with an opaque system-colored High Contrast fallback.
- Give the PDF/image viewer a translucent acrylic canvas while keeping rendered PDF pages themselves readable and opaque.
- Add a clearly labeled, accessible Back to parent folder control to nested Resources folders and keep it synchronized with folder history.


## [2026.10.0-port.61] - 2026-11-02

- Let the native window Mica backdrop show through transparent application-shell surfaces while preserving an opaque High Contrast fallback.
- Preserve the existing NavigationView selection when opening another user's profile; only the signed-in user's own profile selects the Profile destination.
- Cache and deduplicate authenticated post/profile image downloads, decode each image once, and limit concurrent image requests to reduce repeated network and decoding work. Render recent-activity cards before resolving author profiles so images start loading immediately.
- Replace the PDF/image viewer's Download original text with a native Segoe Fluent download glyph and an accessible name/tooltip.


## [2026.10.0-port.60] - 2026-11-02

- Make notification choices, assignment attachment rows, and resource entries activate directly from their ListViewItem, matching the working in-app ListView interaction pattern.
- Harden mailbox loading and SelectorBar switching with cancellation, request generations, timeout feedback, and guarded message-detail navigation.
- Keep the mailbox list directly hosted in its layout instead of navigating a nested Frame for the static list surface.
- Constrain the collapsed NavigationView profile item and avatar to the standard navigation-row size.
- Load course application logos from Schoology's logoUrl/logo_url/logo fields and add a Course Apps tab with a launchable ListView in portrait mode.
- Add clickable post author names and group-context links with the chevron shown by the classic Tauri renderer.

## [2026.10.0-port.59] - 2026-11-02

- Render mailbox messages immediately and resolve participant profile details in the background so slow profile lookups cannot leave the ListView stuck behind its loading overlay.
- Place assignment due dates above the description in the assignment Info view.
- Treat Schoology's missing profile-update component response as an empty updates feed, matching the classic Tauri fallback rather than displaying a misleading API error.
- Add a Calendar / Upcoming SelectorBar and reuse the existing upcoming list in its own tab.
- Open assignment attachment View or Download actions immediately after an attachment is selected, without requiring a second Open confirmation.
- Replace the Resources TreeView with a categorized ListView and course-material-style folder navigation.
- Defer notification target navigation until the chooser ContentDialog has closed; render notification choices as glyph-bearing ListView rows.
- Use standard WinUI Button styling for post Comment, Like, and count actions.
- Replace the discussion comments bottom composer with a title-bar Create action and modal comment/reply composer.


## [2026.10.0-port.58] - 2026-11-02

- Replace assignment action dialogs with a native WinUI MenuFlyout; expose Create Comment, Upload Submission, and Create Text Submission where permitted, and show File/Resource choices after selecting Upload Submission.
- Keep the assignment Create action synchronized with the active Info, Comments, or Submissions selector when the responsive layout and initial shell are created.
- Format assignment due dates using the localized Schoology date/time presentation used by the Tauri and Android apps.
- Send post and comment like actions as JSON booleans, matching the official Android ROLike request and classic Tauri API calls.
- Use the native WinUI AccentButtonStyle for Check for updates.
- Run a fresh update check shortly after every app launch and dispatch the update confirmation on the WinUI UI thread.
- Resolve the current x64 classic installer from the latest stable GitHub release metadata instead of a hard-coded old versioned asset URL.

## [2026.10.0-port.57] - 2026-11-01

- Add a Settings action to download the x64 classic desktop installer and switch from WinUI through a separate PowerShell handoff process. The helper waits for WinUI to exit, runs its per-user uninstaller silently, and then launches the classic installer with /S.
- Fix the WinUI self-update handoff so Inno Setup starts only after the application process has exited, preventing locked files and incomplete replacement during updates.
- Override WinUI accent resources with Schoology blue in Light and Dark themes, keep ordinary text and surfaces neutral, and extend semantic control-fill/layer brushes for darker dark-mode surfaces.
- Use a neutral document glyph for PowerPoint files instead of the misleading slideshow/play glyph.
- Implement assignment text, file, and Schoology-resource submissions using the official Android 2026.10.0 and classic Tauri endpoint shapes, including FileService multipart upload with the legacy metadata-plus-binary fallback and draft text submissions.
- Resolve assignment grade-item metadata before loading submission history or creating text submissions, and include nested resource folders in the Schoology-resource picker.
- Add an assignment attachments action when attachment data exists, with a native ListView dialog for opening each attachment.
- Preserve assignment description markup through the shared rich-content renderer instead of flattening it to plain text.
- Route notification BodyArg targets to assignment, discussion, event, assessment, course, group, school, profile, page, and document views, including notification target selection based on notification type.
- Make all attachment types use the same shared View or Download dialog, with a consistent file identity row and actions.
- Release PDF page image sources and detach the PDF visual tree before navigating back, then defer the originating page restoration to avoid message-thread PDF teardown crashes.

## [2026.10.0-port.56] - 2026-11-02

- Use the native WinUI AccentButtonStyle for the title-bar Create action instead of custom button colors and padding.
- Switch the main NavigationView to responsive Auto mode so its normal left pane becomes a compact overlay on narrow windows.
- Center the course dashboard's wrapping course cards and synchronize dashboard course selection with the matching expanded item in the left navigation.
- Apply WinUI 3 semantic text, card-stroke, control-stroke, divider, and surface brushes in Light, Dark, and High Contrast themes to remove device-accent tint from ordinary text and card outlines.
- Render PDF pages as a continuous vertically scrollable document, retain previous/next page jump buttons, and size each page to the viewer width at initial zoom.
- Add distinct native Fluent glyphs for Word, PowerPoint, and Excel document attachments.
- Format notification body placeholders using positional and sequential BodyArg values, and map notification icons to the official Schoology notification types.
- Render poll options supplied directly on recent-activity update objects, and recognize additional image/photo attachment collections.
- Submit poll votes as JSON, matching the official Android ROPollVote request body and the classic Tauri port's JSON request mode.

## [2026.10.0-port.55] - 2026-11-01

- Use neutral black/gray and white/gray text, navigation selection, and card-outline colors in Light and Dark themes while keeping Schoology-blue buttons and icons.
- Request attachments and rich text for course, group, and school update feeds; render the complete update attachment payload, including separate file attachments, and keep non-image files visible even when they have thumbnails.
- Resolve course-material and post document API URLs to their actual file attachments so supported PDFs/images offer native View or Download actions and other files download directly.
- Normalize poll-option response shapes so official poll models render when options are wrapped or returned as a single object.
- Load PDF previews from an in-memory stream so the temporary file is not locked; serialize page rendering and release the stream before navigating back.
- Apply the current Schoology theme to title-bar Create actions.


## [2026.10.0-port.54] - 2026-11-01

- Restore the generated WinUI PRI resource index beside the unpackaged executable so WinUI can resolve its built-in theme dictionaries during application startup.
- Make the publish fail clearly if the WinUI resource index is missing, and verify it is present before producing either architecture's installer.

## [2026.10.0-port.53] - 2026-11-01

- Remove conflicting global overrides for built-in WinUI control resources while retaining the Schoology Light, Dark, and High Contrast palettes.
- Correct the theme-brush helper's concrete return type.


## [2026.10.0-port.52] - 2026-11-01

- Capture first-chance WinUI/XAML and WinRT exceptions during the first 20 seconds of startup, before they can be surfaced as an opaque Windows STATUS_STOWED_EXCEPTION crash.
- Add first-frame, splash visual-tree, and window activation breadcrumbs to identify whether startup fails while constructing, attaching, or laying out the initial WinUI surface.


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

