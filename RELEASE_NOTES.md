# Release notes

## [2026.10.0-port.92] - 2026-11-05

- Fix the Windows x64 and ARM64 GitHub Actions publish failure by initializing course/group/school profile tab caches before registering event handlers that capture them. This resolves CS0165 definite-assignment errors without changing the navigation-cache behavior.

## [2026.10.0-port.91] - 2026-11-05

- Show each course's overall grade alongside its course name on the standalone Grades page, resolving the signed-in user's final-period grade and preserving teacher letter/percentage overrides and grading-scale formatting.
- Keep standalone course grade expansion usable when Schoology denies one of the grade endpoints; load available assignment data and explain when the grade-item list itself returns HTTP 403 instead of failing the whole course.
- Restore Upcoming row activation with direct, accessible ListViewItem tap handlers, including course/group Upcoming context.
- Label one- and two-comment post actions as Comments (1) and Comments (2).
- Retain loaded Home selector views and course/group/school profile tabs while switching between them; keep independent wide and compact tab views and their loaded state.
- Temporarily cache message detail data and rendered message threads across mailbox switching, and invalidate Sent message detail cache after sending a new message.

- Route Upcoming and Calendar event clicks using the event ID and owning realm (course, group, school, or user), and open native event details with Info and Comments tabs.
- Render event rich text and attachments in Info, preserve the originating Calendar/Upcoming route when navigating back, and hide comments when the event disables them.
- Make the homepage create menu permission-aware for updates, discussions, and events. Recheck destination POST permissions before each composer opens, including the user's own event destination only when permitted.
- Bring event composer settings closer to the official Android/Tauri behavior with all-day, optional end date, RSVP, comments, and attachment controls.
- Accept singleton object and array API collection responses; restore course overall grades when final-grade/period wrappers are objects, preserve teacher string/numeric overrides, and apply letter scales only for letter-grade scale type.
- Align assignment scores to a dedicated trailing column in account and course Grades views.


## [2026.10.0-port.89] - 2026-11-04

- Restore animated expand/collapse behavior in course Grades, matching the Materials folder chevron rotation and nested-row fade/slide transitions while honoring reduced-motion settings.
- Remove the redundant course-name expander from course Grades. Show Overall Grade and grading periods directly in the tab content, with grading periods expanded by default.
- Fix the File, Resources, and remove icons in the assignment submission dialog by using actual Fluent glyph characters instead of literal backslash-u text.
- Add section grade, active enrollment, grading-scale, and section-detail requests. Match final-grade data to the signed-in user's active enrollment and display teacher string overrides or numeric overrides with the configured letter scale and hide-numeric behavior, following the supplied Tauri and Android implementations.
- Keep the account-wide Grades page layout unchanged.

## [2026.10.0-port.88] - 2026-11-04

- Fix the missing course Grades renderer by reusing the account Grades hierarchy and API-loading implementation, filtered to the course whose Grades tab is open.
- Replace the unsupported WinUI `Symbol.OpenInNewWindow` enum member with its native Fluent glyph for embedded-content actions.
- Synchronize Version, AssemblyVersion, FileVersion, and InformationalVersion at port.88.

## [2026.10.0-port.87] - 2026-11-04

- Fix the x64 and ARM64 publish failure by removing the obsolete duplicate assignment-resource picker; retain the multi-select picker used by staged submissions.
- Restore embedded content in posts, comments, and messages: preserve native rich-text rendering and add secure embedded-content actions for iframe/embed/object URLs.
- Replace the rejected `data:text/html` attachment navigation with authenticated HTTPS navigation in the persistent shared WebView2 profile, including Schoology session cookies.
- Extract only HTTPS embed targets from untrusted markup; do not execute arbitrary embedded scripts or accept unsafe URL schemes.



- Fix the malformed assignment-submission refresh JSON expression that caused the x64 and ARM64 GitHub Actions publishes to fail at compile time.
- Replace the Grades TreeView with a virtualized ListView and inline expandable grading-period/category rows, following the course Materials hierarchy pattern.
- Load official course grade items, grading periods, categories, and user grades; right-align assignment scores and round numeric grade values to at most two decimal places.
- Start the Schoology Pages WebView2 HTML navigation only after the control has entered the active visual tree to address the blank viewer.



- Replace immediate single-file assignment uploads with a staged multi-attachment submission dialog. Select multiple local files and multiple Schoology resources, review/remove staged items, see per-item upload progress, and submit all attachments together in one revision using Android's mixed attachment payload model.
- Add an optional assignment comment to the submission dialog and post it through Schoology's separate assignment-comments endpoint; clearly distinguish it from revision attachments because Android's revision model has no comment field.
- Consolidate post comment actions: show Comment, (1), (2), or More Comments (N), open the comments view when comments exist, and remove the separate View more comments control.
- Improve dark-mode rich text contrast by lightening authored foreground colors that would otherwise be unreadable on dark post surfaces, while leaving Light theme and High Contrast colors alone.

## [2026.10.0-port.84] - 2026-11-04

- Fix the WinUI 3 calendar build by using CalendarViewDayItem.SetDensityColors without the unsupported Density property and avoiding a sender/discard variable collision in the asynchronous month loader.
- Preserve authored text colors from Schoology rich text, including CSS color declarations, RGB/RGBA values, hexadecimal colors, and legacy font color attributes.

## [2026.10.0-port.83] - 2026-11-04

- Match Android SchoologyCookieProvider session-start behavior by requesting `sessionstart?domain=app.schoology.com` and preserving explicit parent-domain cookie scope for authenticated WebView2 content.
- Load calendar events month-by-month using the Android `start_date` / `end_date` API and 20-item pagination, including past dates; add a calendar density indicator to dates containing events.


## [2026.10.0-port.82] - 2026-11-04

- Fix x64 and ARM64 publishing by removing a tracking-prevention setting unavailable in the project's WebView2 SDK; preserve the authenticated page WebView and explicit Schoology cookie forwarding.


## [2026.10.0-port.81] - 2026-11-04

- Keep Schoology Page HTML out of course-material row subtitles so page entries display only their names.
- Render page bodies in an authenticated WebView2 using the original HTML, JavaScript, iframe/embed support, and the persistent Schoology browser profile, matching the Android page viewer instead of flattening content into native text.

## [2026.10.0-port.80] - 2026-11-04

- Open course-material Schoology Pages in a native WinUI detail view, loading page content from the section pages endpoint and rendering rich HTML and attachments.
- Resolve course-material links from document metadata and nested link attachments, allow HTTP and HTTPS destinations, and show the native WinUI link glyph in both Course Materials and Resources.
- Fix Course Materials scrolling by using a single viewport-sized scroll container instead of a nested fixed-height list.

## [2026.10.0-port.79] - 2026-11-04

- Share one native Resources browser between standalone Resources and group Resources, retaining the standalone page's categorized collections and resource-app entry points.
- Expand collections and nested resource folders recursively inline with lazy loading, nested indentation, cached contents, animated chevrons, and reduced-motion support.
- Open group resources through native attachment, link, and shared WebView paths while returning to the originating group.

## [2026.10.0-port.78] - 2026-11-04

- Fix WinUI x64 and ARM64 publishing by explicitly aliasing the Windows text-formatting types used by the native rich-text renderer.

## [2026.10.0-port.77] - 2026-11-04

- Preserve clickable inline links and nested rich-text formatting in posts, comments, messages, and assignment information.
- Support Schoology HTML font-family and font-size metadata, CSS font styling, headings, lists, and encoded rich-text fragments in the native WinUI renderer.
- Request rich-text message bodies from Schoology for inbox, sent, and message-thread views.


## [2026.10.0-port.76] - 2026-11-03

- Apply the app's active Light or Dark theme to all native ContentDialogs so dialogs match the rest of Schoology instead of following the Windows system theme.
- Keep Windows High Contrast in control by allowing dialogs to use the system theme when High Contrast is enabled.


## [2026.10.0-port.75] - 2026-11-03

- Align comment layout with the Tauri app: place the author avatar beside the name, with comment text, timestamp, and actions below at the full available width.

## [2026.10.0-port.74] - 2026-11-03

- Animate nested course-material folder expansion and collapse with a subtle fade/slide that respects reduced-motion settings.
- Keep each folder's clickable row separate from its nested contents so hovering a child item no longer also highlights the parent row.

## [2026.10.0-port.73] - 2026-11-03

- Fix WinUI publish failures on x64 and ARM64 by explicitly qualifying the Windows.Foundation.Point type used for course-material folder arrow transforms.

## [2026.10.0-port.72] - 2026-11-03

- Replace course-material folder page navigation with an inline expandable hierarchy.
- Load folder contents on demand, retain loaded nested content when collapsing and reopening, and support recursively nested folders.
- Add indentation for nested levels and animated down/up chevrons that respect the app's reduced-motion setting.

## [2026.10.0-port.71] - 2026-11-03

- Fix Windows update handoff by launching the PowerShell helper through the Windows shell so it can outlive Schoology WinUI.
- Wait for the exact WinUI process to exit cleanly before replacing installed files; only force-stop that PID after a 90-second timeout.
- Add staged handoff logging under the user's temporary folder and preserve the optional WinUI uninstall step when switching to classic Schoology.

## [2026.10.0-port.70] - 2026-11-03

- Add native File and Resource attachment controls to Event, Discussion, and Home/Course/Group Update composers, matching the classic Tauri composer behavior.
- Upload selected files before posting, enforce the 60 MB file limit, support multiple attachments and removal, and prevent duplicate Schoology resources.
- Send file and resource attachments using Schoology's expected `file-attachment` and `attachments` request payloads while preserving existing no-attachment submission paths.


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

