## [2026.10.0-port.133] - 2026-11-11

### Gemini project-number reliability
- Accept numeric or string `projectNumber` values from Resource Manager and fall back to the canonical `projects/{number}` resource name.
- Improve retry guidance when Google has created a project but has not yet exposed its numeric identifier.

### AI workspace and rendering
- Change the AI sidebar from a right-side overlay into a true second layout column that reflows the Schoology content; retain the floating popup presentation.
- Add independent, bounded vertical scrolling to the Normal and Advanced settings tabs.
- Render assistant messages with native rich text, inline code, fenced code blocks, clickable links, headings, lists, and horizontally scrollable Markdown tables without changing the Schoology submission HTML converter.
- Improve thinking and tool-progress messages to show preparation, individual tool calls, and result summarization stages.

## [2026.10.0-port.132] - 2026-11-11

### Google Gemini setup and AI settings layout
- Fix Google Cloud project discovery by using Resource Manager `projects:search`, which lists projects the signed-in user can access without requiring an organization or folder parent.
- Move AI provider selection to normal AI settings and place the active model picker directly below the chat composer.
- Make normal Google sign-in run the complete automated setup: connect the account, create or reuse the account's dedicated ClassWire AI Cloud project, enable required APIs, and create/save a Gemini-restricted key.
- Move Cloud project selection and manual Gemini key creation into Advanced settings.
- Keep provider-specific model discovery and manual model/API-key configuration in Advanced settings, with the composer model picker synchronized to saved settings.

## [2026.10.0-port.131] - 2026-11-11

### WinUI AI panel and CI compiler fix
- Fix the account sign-in navigation method-group compile error that blocked both x64 and ARM64 publishing.
- Add an in-assistant presentation switch between a floating popup and a right-docked sidebar layout.
- Refine the sidebar surface with edge docking and reduced chrome while retaining the floating popup as the default.
- Add WinUI entrance and reposition transitions when system animations are enabled.

## [2026.10.0-port.130] - 2026-11-11

### AI composer and Google connection
- Place the provider/model selector, plus-icon attachment action, and send-icon action inside one rounded native AI message composer.
- Move custom model IDs, model discovery, and provider API-key overrides into Advanced AI settings; keep provider selection and Google account setup in the normal flow.
- Fix the Google connection crash by closing the settings ContentDialog before opening the Google OAuth/setup dialog, then restore settings after setup.
- Clarify that Google sign-in can automatically create and securely save the Gemini API key; preserve the optional manual key override in Advanced settings.

## [2026.10.0-port.129] - 2026-11-11

### Gemini setup and school sign-in fixes
- Use the numeric Google Cloud project number for API Keys API resource paths while retaining project IDs for Resource Manager, Service Usage, and IAM.
- Add automatic Gemini setup that creates a dedicated Cloud project without an invalid parent field, enables required APIs, provisions the service account, and saves a Gemini-restricted key in Windows Credential Manager.
- Preserve manual project selection and project-ID entry alongside automatic setup.
- Route schools with non-OAuth login types through the native account form and retain the selected school ID during credential sign-in.
- Add the `app.classwire.com` test-school alias and map its OAuth flow to the official Schoology app host.

## [2026.10.0-port.128] - 2026-11-11

### AI account and credential management parity
- Add Puter account status, sign-in, and disconnect controls directly to WinUI AI settings, matching the account-session workflow in Tauri and SwiftUI.
- Keep Puter authentication inside its dedicated WebView2 website session; do not request, persist, or expose a Puter API key.
- Add explicit removal controls for saved Gemini, OpenAI, and Anthropic provider keys in Windows Credential Manager.
- Add Google account disconnect support that removes the stored refresh token without deleting the separately saved Gemini API key.
- Improve provider-aware settings visibility and status messaging, and document temporary Puter attachment upload/deletion behavior in advanced privacy information.
- Preserve provider model discovery, custom model IDs, Gemini generation controls, Ollama endpoint settings, and confirmation-gated Schoology writes.

## [2026.10.0-port.127] - 2026-11-11

### ClassWire AI provider parity
- Add live model discovery for Gemini, OpenAI, Anthropic, and Ollama using each provider's model-list API; filter out non-chat OpenAI models and Gemini models without generateContent support.
- Add Puter model discovery through the existing authenticated WebView session, matching the Tauri model picker without storing Puter credentials.
- Let users refresh discovered models, select a returned model, or continue entering a custom model ID; use the configured Ollama server URL for discovery instead of assuming localhost.
- Improve the native WinUI AI settings layout with clear provider-aware controls, accessible model discovery status, and visibility rules for Gemini setup and Ollama endpoint settings.
- Keep provider credentials in Windows Credential Manager and retain the existing Gemini temperature/output-token controls and confirmation-gated Schoology actions.

## [2026.10.0-port.126] - 2026-11-11

### Build repair
- Fix the CS0136 publish failure in the parent request-response AI tool by renaming its local request label so it no longer shadows the `ExecuteAsync` method parameter.
- Keep request identity validation and confirmation-gated accept/decline behavior unchanged.
- Bump the WinUI version to `2026.10.0-port.126`.

## [2026.10.0-port.123] - 2026-11-10

### Teacher grading
- Add a teacher-only assignment Grading view with active student enrollments, grade status, submission status, revision history, and attachment review.
- Paginate enrollment, grade, and submission roster requests instead of silently stopping at the first 200 records.
- Add rubric criteria, preset rating choices, criterion comments, overall comments, comment visibility, and optional total-grade overrides.
- Preserve numeric and grading-scale grade editing for assignments without rubrics.

### Parent accounts
- Discover linked students through the authenticated parent's `child_uids` profile field.
- Add a Switch Student navigation section while retaining the parent's OAuth session.
- Scope course/group lists, upcoming events, calendar, grades, and assignment submissions to the selected linked student.

### ClassWire AI and Puter
- Replace the toolbar robot glyph with the native ChatSparkle icon.
- Add contextual welcome prompts and a native thinking/loading indicator during Schoology tool calls.
- Serve the Puter SDK bridge from a loopback-only `http://localhost` origin rather than the originless `NavigateToString` document; validate web messages against that origin.
- Dispose the Puter listener and resources with the main window.

### Build and warnings
- Resolve WinUI analyzer warnings CA1001 and CA1859.
- Update artifact actions to Node 24-compatible versions and use Inno Setup's preferred x64 architecture identifier.

## [2026.10.0-port.122] - 2026-11-10

- Fix the x64 and ARM64 publish error by creating the dedicated persistent Puter WebView2 environment with the same `CoreWebView2Environment.CreateWithOptionsAsync` API already used by the shared Schoology WebView.
- Initialize the Puter control with that environment through `EnsureCoreWebView2Async(environment)`; do not use the unavailable `WebView2.CreationProperties` or `CoreWebView2Environment.CreateAsync` APIs.
- Keep the Puter sign-in profile isolated under the ClassWire application data folder. Leave the existing shared WebView, PDF viewer, and Schoology cookie/session handling unchanged.

## [2026.10.0-port.121] - 2026-11-10

- Fix the remaining x64 and ARM64 publish error by configuring the Puter WebView2 user-data folder through the WinUI control's `CreationProperties` before initialization, rather than calling an unavailable `CoreWebView2Environment.CreateAsync` overload.
- Preserve the dedicated persistent Puter sign-in profile and leave the existing PDF viewer unchanged.

## [2026.10.0-port.120] - 2026-11-10

- Fix the first x64 and ARM64 AI publish errors by declaring the Puter overlay field and relying on child insertion order instead of the unavailable `Panel.SetZIndex` API. (The WebView2 initialization correction is included in 2026.10.0-port.121.)
- Keep the AI popout and Puter sign-in overlay above their respective content without changing the PDF viewer or existing Schoology navigation.

## [2026.10.0-port.119] - 2026-11-10

- Move ClassWire AI out of the NavigationView and into a right-side acrylic assistant popout controlled by a permanent title-bar button.
- Add Puter as an AI provider using the Puter.js account sign-in flow in a dedicated persistent WebView2 profile; no Puter API key is requested or stored.
- Add Google OAuth authorization-code flow with PKCE, secure refresh-token storage in Windows Credential Manager, Google Cloud project discovery, API enablement, and automatic Gemini-only API-key provisioning.
- Keep manual Gemini API-key entry available as a fallback and inject the configured Google OAuth client settings during GitHub Actions builds.
- Preserve the authenticated Schoology AI tools and confirmation-first write proposals across all supported providers.
- Keep the native PDF viewer implementation unchanged.

- Keep the native PDF WebView2 visible to the compositor at near-zero opacity while the acrylic loading surface covers it, allowing PDF.js to load and render behind the cover.
- Reveal the PDF viewer only after PDF.js sends its document-ready message. Replace the premature 12-second reveal with a 45-second loading error state; the fallback no longer exposes an unready viewer.
- Port the authenticated ClassWire AI Schoology tool layer to WinUI using the existing OAuth session and typed API client.
- Add AI tools for courses, groups, upcoming events, assignment search/details/submissions, course grades, materials, updates, inbox/sent message search and message details, plus in-app navigation.
- Add provider-native function calling for Gemini, OpenAI, Anthropic, and Ollama, with bounded multi-step tool execution and untrusted-tool-result handling.
- Add confirmation-first proposals for text assignment submission, exact-recipient message sending, course/group update posting, and teacher grade changes. No write request is sent until the user confirms; Schoology remains the final permission authority.
- Add the Android-aligned grade update payload shape through the section grades endpoint.
- Bump the WinUI release version to 2026.10.0-port.118.

## [2026.10.0-port.115] - 2026-11-08

- Fix the x64 and ARM64 PDF.js verification failure: validate that the standard-font directory contains files instead of requiring a hard-coded `FoxitSans.pfb` filename that is not shipped under that name by the pinned PDF.js 4.10.38 package.
- Continue validating the viewer HTML, PDF.js module/worker, CMaps, standard-font contents, and total runtime asset count before installer packaging.

## [2026.10.0-port.114] - 2026-11-08

- Fix the PDF.js publish failure by explicitly removing staged PDF.js files from WinUI Content/None defaults, then re-including them as ordinary runtime files with deterministic `PdfJs\\...` output and publish paths.
- Keep HTML, ES modules, worker, CMaps, standard fonts, and license outside PRI resource processing and outside single-file bundling.
- Retain CI checks that verify the PDF viewer and its runtime assets are present for both x64 and ARM64 before installer packaging.

## [2026.10.0-port.113] - 2026-11-08

- Fix PDF.js runtime asset deployment by explicitly setting the publish-relative `PdfJs\\...` target paths and copy-to-publish metadata.
- Verify the viewer page, PDF.js module/worker, CMaps, and standard fonts are present in both architecture publish outputs before packaging installers.
- Keep PDF.js resources outside WinUI PRI resource processing.

- Fix x64 and ARM64 publish failures in WinUI PRI generation by treating bundled PDF.js files as ordinary copied runtime assets rather than WinUI content resources.
- Preserve the PDF.js viewer, selectable text, PDF hyperlinks, page navigation, zoom, and the shared ClassWire icon.

- Replace the raster-only PDF preview with a bundled PDF.js viewer hosted inside a dedicated WebView2 surface, with selectable/copyable text, clickable external and internal PDF links, page navigation, and zoom.
- Keep the native ClassWire viewer toolbar and original-file download action; do not expose the built-in Chromium PDF viewer or its download controls.
- Regenerate the Windows app icon from the exact ClassWire vector mark used by the SwiftUI app, keeping the white canvas, blue ring, and original C proportions consistent across platforms.

## [2026.10.0-port.110] - 2026-11-08

- Correct user-facing loading, download, attachment, message, resource, and API-response wording throughout the app so Schoology content and data are attributed to Schoology rather than the ClassWire desktop app.
- Preserve ClassWire branding for the application itself, its sign-in experience, settings, notifications, and update workflow.

## [2026.10.0-port.109] - 2026-11-08

- Fix the message deep-link mailbox initialization compiler error that failed both x64 and ARM64 publish jobs.
- Implement the native People directory using Schoology's paginated users endpoint, with enrollment-based fallback when directory access is forbidden.
- Route course and global assessment links to a native assessment details screen with available timing/attempt metadata and a validated external launch action; assessment links no longer open the authenticated WebView.
- Update the Windows app icon artwork so the C mark sits on a white circle interior while the area outside the blue ring remains transparent.

## [2026.10.0-port.108] - 2026-11-08

- Fix the duplicate Schoology-host helper that blocked both x64 and ARM64 publish jobs.
- Route incoming Schoology deep links to native WinUI course, group, school, profile, grades, messages, assignment, discussion, event, and page views.
- Replace the course-page WebView with native rich-content rendering; external course apps and learning tools launch outside the authenticated WebView.
- Keep unimplemented destinations on an explicit native ClassWire route screen rather than silently falling back to authenticated web content.

## [2026.10.0-port.107] - 2026-11-08

- Add a reusable native WinUI rich-text composer to discussion and event descriptions, both update composers, and outgoing Messages; send serialized HTML while retaining plain-text validation and existing attachments.
- Register the `schoology://` protocol per user, handle cold launches, and forward incoming URLs to the running single instance.
- Route OAuth callbacks, external links, external documents, LTI assignment errors, LTI, SCORM, and external-tool links through validated native handlers or the authenticated Schoology WebView.

## [2026.10.0-port.106] - 2026-11-07

- Restore the native ClassWire lockup on the login screen and use transparent C-mark icon artwork so the Windows taskbar and tile icons have no white square backdrop.
- Select only the current ClassWire classic x64 installer in migration updates while retaining Schoology compatibility installer aliases in classic releases.
- Publish Schoology-named WinUI installer aliases alongside the ClassWire x64 and ARM64 release assets for existing clients.

## [2026.10.0-port.105] - 2026-11-07

- Replace the missing WebView2 text submission input with a native WinUI rich editor and preserve submission, draft, and cancel behavior.
- Keep bold, italic, underline, hyperlink, bulleted-list, numbered-list, and clear-formatting controls; export only supported semantic HTML and safe HTTP(S) links.
- Match the canonical SwiftUI ClassWire C mark in the native login and splash lockup, app icons, and installer icon assets.
- Point update and release-note checks at the renamed ClassWire repository and match the current ClassWire installer asset name.
- Resolve the reported CA1822, CA1826, CA1853, CA1859, and CA1865 build warnings.

## [2026.10.0-port.104] - 2026-11-07

- Add a rich-text editor to Create Text Submission with bold, italic, underline, hyperlinks, bulleted and numbered lists, and clear formatting.
- Preserve the existing Submit, Save draft, and Cancel actions; submit sanitized HTML while keeping plain-text validation and the existing dialog scroll sizing.
- Match the active light or dark theme, respect Windows high contrast, provide named keyboard-accessible toolbar controls, and block external navigation and rich HTML paste into the editor.
- Fix the CI executable check to match the renamed ClassWire.exe output so packaging can continue after publish verification.

## [2026.10.0-port.103] - 2026-11-06

- Rebrand the Windows desktop product as ClassWire across app titles, login and splash screens, tray, notifications, settings, dialogs, and user-facing error messages.
- Replace the circular S identity with a matching blue-ring C app icon and native C login/splash lockup; use a light ClassWire wordmark with a blue Wire suffix.
- Replace the account action's old S logo with a neutral person icon.
- Rename the executable/product metadata to ClassWire and update installer, shortcuts, uninstaller, update-asset naming, and CI release artifacts.
- Keep the existing install/data directories, stable installer AppId, singleton mutex, Schoology OAuth/API hosts, callback scheme, and service identifiers so upgrades, saved sessions, and Schoology connectivity remain compatible.

## [2026.10.0-port.102] - 2026-11-06

- Fix the x64 and ARM64 publish failures in the login brand lockup by using supported WinUI shape, path-geometry, document-run, and accessibility APIs.
- Preserve the SwiftUI-matched circular Schoology mark and exact S glyph path using native WinUI Bezier and line segments.

## [2026.10.0-port.101] - 2026-11-06

- Replace the login screen's raster logo with the same native Schoology brand lockup as SwiftUI: circular S mark, light-weight Schoology wordmark, and matching blue suffix.
- Apply a 15.5pt minimum to rich-text runs across posts, comments, assignment details, discussion details, and event details, including inline Schoology HTML font-size styling.
- Enable Windows text-scale support for rich-text blocks so content responds to the system's accessibility text-size setting while retaining links, emphasis, lists, and paragraph formatting.

## [2026.10.0-port.100] - 2026-11-06

- Keep audio and video attachments on their original media URL instead of using Schoology's PDF conversion, including in View or Download flows.
- Add a native WinUI media viewer with playback controls for common video and audio formats, including MP4, M4V, MOV, AVI, WMV, MKV, WebM, MP3, M4A, AAC, WAV, WMA, FLAC, OGG, and related formats supported by the installed Windows codecs.
- Preserve original media downloads from the viewer and release the media player and cached preview when navigating back.
- Match the working SwiftUI Resource App flow by opening Schoology's launch URL unchanged; retrieve imported resources from the authenticated `/content_app/import?mobile=1` callback without requiring a RelayState query parameter.
- Redraw the Windows application icon using the same Schoology ring-and-S artwork and colors generated by the SwiftUI app icon; retain the Schoology wordmark on the login screen.

## [2026.10.0-port.99] - 2026-11-05

- Rebuild the assignment attachment resource picker around the standalone Resources categories: My Resources, Shared, Groups, and Resource Apps.
- Preserve expandable collection and nested-folder navigation in the assignment picker, with selectable individual Schoology resources.
- Add the Android-style Resource App import path: launch with import_view=1 and acceptable file/link/embed types, intercept the Schoology /content_app/import callback, retrieve imported file and link IDs using the authenticated Schoology session, and submit them as imported_resource_id attachments.
- Keep ordinary Schoology resources submitted as schoology_resource_id attachments and uploaded local files submitted as file IDs.

## [2026.10.0-port.98] - 2026-11-05

- Use the Segoe Fluent Icons MobNotificationBell glyph for the Notifications navigation item.
- Match the main Grades navigation icon to the Grades icon used in the course sidebar.
- Use the Segoe Fluent Icons Slideshow glyph for PowerPoint presentations and Photo glyph for image attachments and resources.
- Render comment replies directly beneath their parent comments, following the parent-ID hierarchy used by the Android and SwiftUI implementations. Keep orphaned or malformed comments visible.

## [2026.10.0-port.97] - 2026-11-05

- Preserve each attachment's type when opening the Assignment Attachments list so Schoology links and video URLs open in the system browser instead of the file View/Download dialog.
- Reuse the same type-aware attachment opening path for notification document attachments and shared attachment controls; retain the existing native View/Download flow for actual files.

# Release notes

## [2026.10.0-port.125]

### ClassWire AI
- Forward image and extracted text attachments to Puter multimodal chat; PDF attachments now show a clear provider-specific limitation rather than being silently omitted.
- Add persisted Gemini temperature and maximum-output-token controls to Advanced settings.
- Expand first-run onboarding guidance for provider setup, attachments, and confirmation-gated Schoology actions.
- Scope AI course, group, calendar, grade, assignment, and submission reads to the currently selected learner in parent accounts.
- Include the active learner identity in AI page context and prevent parent-context grade changes or assignment submissions when the Schoology API does not expose a verified on-behalf-of-student write parameter.

### Version
- Increment WinUI app version to `2026.10.0-port.125`.


## [2026.10.0-port.124]

### ClassWire AI
- Add multimodal image/PDF attachments for Gemini and Anthropic, image input for OpenAI and Ollama, and local text extraction for supported text documents.
- Add Gemini image generation with generated-image previews in chat.
- Add AI-proposed assignment file submissions; files upload together and are submitted only after explicit confirmation.
- Add Normal and Advanced AI settings tabs for provider/model/key configuration and advanced endpoint/privacy guidance.
- Add Schoology recipient search returning exact IDs; message proposals require and revalidate the selected recipient ID.
- Preserve rich-text formatting when sending AI-composed messages.
- Keep attachment bytes and extracted text out of saved conversation history.

### Build
- Fix x64 and ARM64 publish errors caused by treating the NavigationView selection event sender as a discard, unsupported TabView enum usage, StorageFile metadata access, and Ollama message serialization.
- Increment the WinUI app version to `2026.10.0-port.124`.


## [2026.10.0-port.96] - 2026-11-05

- Align standalone overall-grade loading with Android FinalGradesApiHelper and Tauri: use section grades, unfiltered paged enrollment data, grading scales, and section options; match the final-grade record to the signed-in user's enrollment and read only the `final` period.
- Remove the unsupported extra user-grade lookup from the header hydration path, retain safe embedded final-grade fallback, and log grade-resolution diagnostics without logging grade values or personal data.

## [2026.10.0-port.95] - 2026-11-05

- Restore standalone course overall grades by falling back to the signed-in user's section-scoped grade response when the section-wide final-grade feed omits the student's record. Preserve enrollment matching, final-period selection, teacher overrides, and grading-scale formatting.
- Make standalone Grades course headers and their clickable rows stretch across the available viewport so the course name, overall grade, and expand arrow align in separate columns.

## [2026.10.0-port.94] - 2026-11-05

- Add a shared dialog viewport that sizes itself to the available window height and scrolls overflowing content. Apply it to event/discussion, update, message, assignment-submission, comment, release-note, and detail dialogs; keep native ListView scrolling for collection dialogs.

## [2026.10.0-port.93] - 2026-11-05

- Give the standalone Grades page a height-constrained, auto-resizing scroll viewport. Expanded course, grading-period, category, and assignment rows now scroll together within the available shell space instead of allowing the content stack to grow beyond the window.

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

