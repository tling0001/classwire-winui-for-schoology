using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Windows.Data.Pdf;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.ViewManagement;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Schoology.WinUI;

public sealed partial class MainWindow : Window, IAsyncDisposable
{
    private readonly Grid _root;
    private readonly DispatcherTimer _splashTimer;
    private string _page = "login";
    private string _lastRenderedPage = "splash";
    private string _section = "Recent Activity";
    private NavigationView? _appNavigation;
    private bool _syncingNavigationSelection;
    private Frame? _sectionFrame;
    private string _selectedEntity = string.Empty;
    private long _selectedEntityId;
    private string _selectedEntityTab = string.Empty;
    private long _activeProfileUserId;
    private string _profileReturnNavigationTag = "Home";
    private string _profileReturnSection = "Recent Activity";
    private Button? _titleBarCreateButton;
    private string _titleBarCreateContext = string.Empty;
    private Func<Task>? _titleBarCreateAction;
    private readonly SchoologyAuthService _auth = new(false);
    private bool _highContrastReadAvailable = true;
    private bool _highContrastEnabled;
    private readonly SchoologyApiClient _api;
    private readonly ObservableCollection<SchoologyEntity> _courses = new();
    private readonly ObservableCollection<SchoologyEntity> _groups = new();
    private readonly Dictionary<long, JsonElement> _userProfileCache = new();
    // Share authenticated post/profile image downloads and decoded bitmaps across the UI.
    private readonly ConcurrentDictionary<string, Lazy<Task<BitmapImage>>> _postImageCache = new();
    private readonly SemaphoreSlim _postImageDownloadGate = new(6, 6);
    private long _activeAssignmentSectionId;
    private long _activeAssignmentId;
    private long _activeAssignmentGradeItemId;
    private bool _activeAssignmentAllowsComments;
    private bool _activeAssignmentAllowsSubmission;
    private WebView2? _sharedWebView;
    private CoreWebView2Environment? _sharedWebViewEnvironment;
    private readonly SemaphoreSlim _sharedWebViewGate = new(1, 1);
    private bool _sharedWebViewInitialized;
    private Grid? _activeNativeFileViewerRoot;
    private Action? _refreshNativeFileViewer;
    private DispatcherTimer? _notificationTimer;
    private bool _notificationPollInProgress;
    private bool _allowExit;
    private bool _isBackgroundHidden;
    private bool _releaseNotesPending;
    private bool _trayIconAdded;
    private IntPtr _previousWindowProc;
    private WindowProc? _windowProcDelegate;
    private const uint WmClose = 0x0010;
    private const uint WmEndSession = 0x0016;
    private const uint WmSysColorChange = 0x0015;
    private const uint WmSettingChange = 0x001A;
    private const uint WmThemeChanged = 0x031A;
    private const uint SpiGetHighContrast = 0x0042;
    private const uint HighContrastOn = 0x00000001;
    private const uint WmTrayCallback = 0x8000 + 41;
    private const uint WmActivateExisting = 0x8000 + 42;
    private const uint WmCopyData = 0x004A;
    [StructLayout(LayoutKind.Sequential)]
    private struct CopyDataStruct
    {
        public IntPtr Data;
        public int ByteCount;
        public IntPtr Buffer;
    }
    private const int GwlWndProc = -4;
    private const uint WmLButtonDoubleClick = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private long _sharedWebViewSessionUserId;
    private SchoologyAuthService.Session? _session;
    private QrCameraScanner? _qrScanner;
    private MediaPlayer? _qrPlayer;
    private static readonly string WindowStatePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Schoology", "window-state.json");
    private static readonly string ReleaseNotesStatePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Schoology", "release-notes-version.txt");
    private static readonly string AppSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Schoology", "settings.json");
    private static readonly string NotificationSeenStatePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Schoology", "notification-seen.json");
    private static readonly string UpdateStatePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Schoology", "update-state.json");
    private static readonly HttpClient ReleaseNotesClient = new();
    private static readonly HttpClient UpdateMetadataClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly HttpClient UpdateDownloadClient = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(12);
    private static readonly TimeSpan UpdateRetryInterval = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _updateCheckGate = new(1, 1);
    private bool _automaticUpdateChecksStarted;
    private bool _updateDialogOpen;
    private UpdateReleaseInfo? _pendingUpdateInfo;
    private bool _restoringWindowState;
    private readonly ConcurrentQueue<string> _pendingProtocolUrls = new();
    private bool _protocolDispatchReady;
    private bool _processingProtocolUrls;
    private long _pendingProtocolMessageId;
    private bool _pendingProtocolMessageSent;
    private string? _pendingProtocolMailbox;

    private sealed record UpdateReleaseInfo(string Tag, string Version, string AssetName, Uri DownloadUri, long Size, string? Digest);
    private sealed record UpdateCheckState(long LastSuccessfulCheckUnixMilliseconds, string LastRemoteTag);
    private sealed class PendingAssignmentSubmissionItem
    {
        internal required string Name { get; init; }
        internal required string Kind { get; init; }
        internal IStorageFile? File { get; init; }
        internal long ResourceId { get; init; }
        internal string ResourceField { get; init; } = "schoology_resource_id";
        internal string? UploadedFileId { get; set; }
        internal required ProgressBar Progress { get; init; }
        internal required TextBlock Status { get; init; }
        internal required ListViewItem Row { get; init; }
    }

    public MainWindow(string? initialProtocolUrl = null)
    {
        App.WriteStartupLog("MainWindow constructor entered.");
        if (IsValidSchoologyProtocolUrl(initialProtocolUrl))
            _pendingProtocolUrls.Enqueue(initialProtocolUrl!);
        // The unpackaged publish can omit this empty window's standalone ms-appx
        // XAML resource. Construct the root directly so startup does not depend
        // on a resource that is not present in the installed EXE layout.
        _root = new Grid();
        Content = _root;
        _root.Loaded += (_, _) => App.WriteStartupLog("MainWindow root Loaded event received; initial XAML root is attached.");
        App.WriteStartupLog("MainWindow visual root created programmatically.");
        _api = new SchoologyApiClient(_auth);
        Activated += (_, _) => App.WriteStartupLog("MainWindow Activated event received.");
        _windowProcDelegate = HandleWindowMessage;
        var windowHandle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        _previousWindowProc = SetWindowLongPtr(windowHandle, GwlWndProc, _windowProcDelegate);
        ConfigureStartupRegistration();
        Closed += async (_, _) =>
        {
            App.WriteStartupLog("MainWindow.Closed event received; beginning window disposal.");
            await DisposeAsync();
            App.WriteStartupLog("MainWindow disposal completed.");
            DestroyTrayIcon();
            try { AppNotificationManager.Default.Unregister(); } catch (Exception ex) { App.WriteStartupLog("Could not unregister app notifications during shutdown.", ex); }
        };
        _root.RequestedTheme = ReadAppSetting("AppTheme", "System default") switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        _highContrastEnabled = ReadHighContrastSetting();
        _root.ActualThemeChanged += (_, _) => RefreshThemeSurfaces();
        Title = "ClassWire";
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        try { AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall; } catch { }
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        try { SystemBackdrop = new MicaBackdrop(); } catch { }

        RestoreWindowState();
        AppWindow.Changed += (_, _) => SaveWindowState();
        _ = SetWindowIconAsync();
        App.WriteStartupLog("Previous window bounds and native window icon configured.");

        App.WriteStartupLog("Creating splash screen.");
        ShowSplash();
        App.WriteStartupLog("Splash screen created.");

        _splashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _splashTimer.Tick += async (_, _) =>
        {
            App.WriteStartupLog("Splash timer Tick event entered.");
            try
            {
                _splashTimer.Stop();
                App.WriteStartupLog("Splash timer stopped; switching to login.");
                ShowLogin();
                App.WriteStartupLog("Login screen created; beginning saved-session restoration.");
                await RestoreSavedSessionAsync();
                _protocolDispatchReady = true;
                await ProcessPendingProtocolUrlsAsync();
                if (_isBackgroundHidden) _releaseNotesPending = true;
                else await CheckAndShowReleaseNotesAsync();
                StartAutomaticUpdateChecks();
                App.WriteStartupLog("Startup session restoration, release-notes, and update-check sequence completed.");
            }
            catch (Exception ex)
            {
                App.WriteStartupLog("Unhandled exception in splash/login/session transition.", ex);
                try
                {
                    if (_page != "login") ShowLogin();
                }
                catch (Exception fallbackException)
                {
                    App.WriteStartupLog("Could not restore the login screen after startup failure.", fallbackException);
                }
            }
        };
        _splashTimer.Start();
        App.WriteStartupLog("Splash timer started.");
    }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct HighContrastSettings
    {
        public uint Size;
        public uint Flags;
        public IntPtr DefaultScheme;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint action, uint parameter, ref HighContrastSettings settings, uint updateProfile);

    private delegate IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, WindowProc newProc);
    [DllImport("user32.dll", EntryPoint = "CallWindowProcW", SetLastError = true)]
    private static extern IntPtr CallWindowProc(IntPtr previousProc, IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "ShowWindow")]
    private static extern bool ShowNativeWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "LoadIconW", CharSet = CharSet.Unicode)] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", EntryPoint = "CreatePopupMenu")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize; public IntPtr hWnd; public uint uID; public uint uFlags; public uint uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState; public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    private static bool BackgroundModeEnabled => string.Equals(ReadAppSetting("RunInBackgroundAtStartup", "true"), "true", StringComparison.OrdinalIgnoreCase);
    private static void ConfigureStartupRegistration()
    {
        try
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (runKey is null) return;
            if (BackgroundModeEnabled)
            {
                var executable = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(executable)) runKey.SetValue("ClassWire", $"\"{executable}\" --background");
            }
            else runKey.DeleteValue("ClassWire", throwOnMissingValue: false);
            runKey.DeleteValue("Schoology", throwOnMissingValue: false);
        }
        catch (Exception ex) { App.WriteStartupLog("Could not configure the current-user Windows startup entry.", ex); }
    }
    private IntPtr HandleWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            return HandleWindowMessageCore(hwnd, message, wParam, lParam);
        }
        catch (Exception ex)
        {
            // Never allow a managed exception to cross the unmanaged WndProc boundary.
            App.WriteStartupLog($"Exception in native window-message callback (message=0x{message:X4}).", ex);
            try
            {
                return _previousWindowProc == IntPtr.Zero
                    ? IntPtr.Zero
                    : CallWindowProc(_previousWindowProc, hwnd, message, wParam, lParam);
            }
            catch (Exception fallbackException)
            {
                App.WriteStartupLog("The previous native window procedure also failed.", fallbackException);
                return IntPtr.Zero;
            }
        }
    }

    private IntPtr HandleWindowMessageCore(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message is 0x0002 or 0x0082)
            App.WriteStartupLog($"Native window destruction message received: 0x{message:X4}.");
        if (message == WmClose)
            App.WriteStartupLog($"Native WM_CLOSE received; background-mode={BackgroundModeEnabled}, allow-exit={_allowExit}.");
        if (message is WmSettingChange or WmThemeChanged or WmSysColorChange)
        {
            var result = _previousWindowProc == IntPtr.Zero
                ? IntPtr.Zero
                : CallWindowProc(_previousWindowProc, hwnd, message, wParam, lParam);
            var settingName = message == WmSettingChange && lParam != IntPtr.Zero
                ? Marshal.PtrToStringUni(lParam)
                : null;
            if (message != WmSettingChange ||
                string.Equals(settingName, "Accessibility", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(settingName, "HighContrast", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _highContrastEnabled = ReadHighContrastSetting();
                    // Defer XAML tree changes until Windows has completed native message dispatch.
                    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
                    {
                        try { RefreshThemeSurfaces(); }
                        catch (Exception refreshException)
                        {
                            App.WriteStartupLog("Deferred theme refresh failed.", refreshException);
                        }
                    });
                }
                catch (Exception ex)
                {
                    App.WriteStartupLog("Could not refresh theme surfaces after a Windows accessibility/theme change.", ex);
                }
            }
            return result;
        }

        if (message == WmEndSession && wParam != IntPtr.Zero)
        {
            _allowExit = true;
            DestroyTrayIcon();
        }
        if (message == WmClose && BackgroundModeEnabled && !_allowExit)
        {
            EnsureTrayIcon();
            _isBackgroundHidden = true;
            ShowNativeWindow(hwnd, 0);
            return IntPtr.Zero;
        }
        if (message == WmCopyData && lParam != IntPtr.Zero)
        {
            var data = Marshal.PtrToStructure<CopyDataStruct>(lParam);
            if (data.ByteCount is > 1 and <= 131072 && data.ByteCount % sizeof(char) == 0 &&
                data.Buffer != IntPtr.Zero)
            {
                var incoming = Marshal.PtrToStringUni(data.Buffer, data.ByteCount / sizeof(char))?.TrimEnd('\0');
                if (IsValidSchoologyProtocolUrl(incoming))
                {
                    _pendingProtocolUrls.Enqueue(incoming!);
                    ShowFromBackgroundNotification();
                    if (_protocolDispatchReady)
                        DispatcherQueue.TryEnqueue(() => _ = ProcessPendingProtocolUrlsAsync());
                    return new IntPtr(1);
                }
            }
            return IntPtr.Zero;
        }
        if (message == WmActivateExisting)
        {
            ShowFromBackgroundNotification();
            return IntPtr.Zero;
        }
        if (message == WmTrayCallback)
        {
            var trayMessage = unchecked((uint)lParam.ToInt64()) & 0xFFFF;
            if (trayMessage == WmLButtonDoubleClick) ShowFromBackgroundNotification();
            else if (trayMessage == WmRButtonUp) ShowTrayMenu(hwnd);
            return IntPtr.Zero;
        }
        return _previousWindowProc == IntPtr.Zero ? IntPtr.Zero : CallWindowProc(_previousWindowProc, hwnd, message, wParam, lParam);
    }
    private void EnsureTrayIcon()
    {
        if (_trayIconAdded) return;
        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var data = new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(), hWnd = hwnd, uID = 1,
            uFlags = 1 | 2 | 4, uCallbackMessage = WmTrayCallback,
            hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512)), szTip = "ClassWire", szInfo = string.Empty, szInfoTitle = string.Empty
        };
        _trayIconAdded = ShellNotifyIcon(0, ref data);
    }
    private void DestroyTrayIcon()
    {
        if (!_trayIconAdded) return;
        var data = new NotifyIconData { cbSize = (uint)Marshal.SizeOf<NotifyIconData>(), hWnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id), uID = 1, szTip = string.Empty, szInfo = string.Empty, szInfoTitle = string.Empty };
        ShellNotifyIcon(2, ref data);
        _trayIconAdded = false;
    }
    private void ShowTrayMenu(IntPtr hwnd)
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenu(menu, 0, new UIntPtr(1), "Open ClassWire");
            AppendMenu(menu, 0, new UIntPtr(2), "Exit");
            GetCursorPos(out var point);
            SetForegroundWindow(hwnd);
            var command = TrackPopupMenu(menu, 0x0100 | 0x0002, point.X, point.Y, 0, hwnd, IntPtr.Zero);
            if (command == 1) ShowFromBackgroundNotification();
            else if (command == 2)
            {
                _allowExit = true;
                DestroyTrayIcon();
                SendMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
            }
        }
        finally { DestroyMenu(menu); }
    }
    internal void HideToBackground()
    {
        if (!BackgroundModeEnabled) return;
        EnsureTrayIcon();
        _isBackgroundHidden = true;
        ShowNativeWindow(Win32Interop.GetWindowFromWindowId(AppWindow.Id), 0);
    }
    private static bool IsValidSchoologyProtocolUrl(string? rawUrl) =>
        Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsed) &&
        string.Equals(parsed.Scheme, "schoology", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetSafeExternalUri(string? rawUrl, out Uri? destination)
    {
        destination = null;
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var candidate) ||
            candidate.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(candidate.UserInfo) || string.IsNullOrWhiteSpace(candidate.Host))
            return false;
        destination = candidate;
        return true;
    }

    private static bool TryGetSafeDocumentUri(string? rawUrl, out Uri? destination)
    {
        destination = null;
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var candidate) ||
            candidate.Scheme is not ("http" or "https" or "ms-word" or "ms-excel" or "ms-powerpoint" or "file") ||
            !string.IsNullOrEmpty(candidate.UserInfo))
            return false;
        if (candidate.Scheme is "http" or "https" && string.IsNullOrWhiteSpace(candidate.Host))
            return false;
        destination = candidate;
        return true;
    }

    private async Task ProcessPendingProtocolUrlsAsync()
    {
        if (!_protocolDispatchReady || _processingProtocolUrls) return;
        _processingProtocolUrls = true;
        try
        {
            while (_pendingProtocolUrls.TryPeek(out var rawUrl))
            {
                if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) ||
                    !string.Equals(uri.Scheme, "schoology", StringComparison.OrdinalIgnoreCase))
                {
                    _pendingProtocolUrls.TryDequeue(out _);
                    continue;
                }
                var host = uri.Host.ToLowerInvariant();
                if (_session is null && host is not ("external_login_callback" or "link" or "external-document" or "lti-assignment"))
                    break;
                _pendingProtocolUrls.TryDequeue(out _);
                try { await HandleSchoologyProtocolUrlAsync(uri); }
                catch (Exception ex)
                {
                    App.WriteStartupLog($"Could not route Schoology protocol URL host '{host}'.", ex);
                    await ShowApiErrorAsync("Unable to open Schoology link", ex.Message);
                }
            }
        }
        finally { _processingProtocolUrls = false; }
    }

    private async Task HandleSchoologyProtocolUrlAsync(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        var query = HttpUtility.ParseQueryString(uri.Query);
        if (host == "external_login_callback")
        {
            var requestToken = query.Get("request_token") ?? query.Get("oauth_token");
            if (string.IsNullOrWhiteSpace(requestToken))
                throw new InvalidOperationException("The Schoology sign-in callback did not include a request token.");
            _session = await _auth.CompleteSchoolBrowserSignInAsync(requestToken);
            await LoadAccountCollectionsAsync();
            ShowHome();
            return;
        }
        if (host == "lti-assignment")
        {
            await ShowApiErrorAsync("Unable to open LTI assignment",
                query.Get("error-message") ?? "Schoology could not open this LTI assignment.");
            return;
        }
        if (host == "link")
        {
            if (!TryGetSafeExternalUri(query.Get("url"), out var destination))
                throw new InvalidOperationException("The link does not contain a valid HTTP or HTTPS destination.");
            if (IsSchoologyWebHost(destination!.Host) && _session is not null)
            {
                if (!await TryRouteNativeSchoologyWebUrlAsync(destination))
                    ShowNativeSchoologyRoutePage("Schoology link", destination.AbsolutePath);
            }
            else if (!await Launcher.LaunchUriAsync(destination!))
                throw new InvalidOperationException("Windows could not open the requested link.");
            return;
        }
        if (host == "external-document")
        {
            foreach (var candidate in new[] { query.Get("uri"), query.Get("fallback-url") })
                if (TryGetSafeDocumentUri(candidate, out var destination) &&
                    await Launcher.LaunchUriAsync(destination!)) return;
            throw new InvalidOperationException("Neither the document link nor its fallback could be opened.");
        }

        var path = "/" + host + uri.AbsolutePath;
        var target = new UriBuilder("https", "app.schoology.com")
        {
            Path = path,
            Query = uri.Query.TrimStart('?')
        }.Uri;
        if (!await TryRouteNativeSchoologyPathAsync(path, target))
            ShowNativeSchoologyRoutePage("Schoology link", path);
    }

    private static SchoologyApiRecord DeepLinkRecord(string title, long id)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { id }));
        return new SchoologyApiRecord(title, string.Empty, document.RootElement.Clone());
    }

    private static JsonElement UnwrapEntity(JsonElement root, string property)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(property, out var nested) && nested.ValueKind == JsonValueKind.Object)
            return nested;
        return root;
    }

    private static SchoologyEntity CreateDeepLinkEntity(long id, JsonElement data, string fallbackName)
    {
        var name = ProfileValue(data, "title", "name", "section_title", "sectionTitle",
            "course_title", "courseTitle", "group_title", "groupTitle") ?? fallbackName;
        var subtitle = ProfileValue(data, "course_code", "courseCode", "section_code",
            "sectionCode", "school_title", "schoolTitle", "description") ?? string.Empty;
        var image = ProfileValue(data, "picture_url", "pictureUrl", "image_url", "imageUrl",
            "course_image", "courseImage", "photo_url", "photoUrl");
        return new SchoologyEntity(id, name, subtitle, image, data.Clone());
    }

    private async Task<SchoologyEntity> ResolveDeepLinkEntityAsync(string kind, long id)
    {
        if (id <= 0) throw new InvalidOperationException("The Schoology link contains an invalid destination ID.");
        var existing = kind switch
        {
            "Courses" => _courses.FirstOrDefault(item => item.Id == id),
            "Groups" => _groups.FirstOrDefault(item => item.Id == id),
            _ => null
        };
        if (existing is not null) return existing;
        var session = _session ?? throw new InvalidOperationException("Sign in to open this Schoology destination.");
        if (kind == "Courses")
        {
            using var document = await _api.GetSectionDetailsAsync(session, id);
            var section = UnwrapEntity(document.RootElement, "section");
            return CreateDeepLinkEntity(id, section, $"Course {id}");
        }
        if (kind == "Groups")
        {
            var groups = await _api.GetGroupsAsync(session);
            var group = groups.FirstOrDefault(item => item.Id == id);
            if (group is not null) return group;
            throw new InvalidOperationException("This group is not available to the signed-in account.");
        }
        if (kind == "School Profile")
        {
            using var document = await _api.GetEntityTabAsync(session, kind, id, "Info");
            var school = UnwrapEntity(document.RootElement, "school");
            return CreateDeepLinkEntity(id, school, $"School {id}");
        }
        throw new InvalidOperationException("This Schoology destination type is not supported.");
    }

    private void ShowNativeSchoologyRoutePage(string title, string path)
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(24), MaxWidth = 760 };
        panel.Children.Add(Text(title, 22, true));
        panel.Children.Add(Text("This link was received and opened in ClassWire, but this destination does not have a native WinUI screen yet.", 14));
        var route = Text(path, 12);
        route.Foreground = ThemeBrush("SchoologyMutedTextBrush");
        route.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(route);
        panel.Children.Add(Action("Back to Home", (_, _) => ShowHome("Recent Activity")));
        if (_sectionFrame is not null) NavigateMainSection(panel, title);
        else Reset(BuildApplicationShell(title, panel));
    }

    private void ShowNativeExternalToolPage(string title, string route)
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(24), MaxWidth = 720 };
        panel.Children.Add(Text(title, 22, true));
        panel.Children.Add(Text("This activity is provided by an external learning tool. ClassWire keeps the destination page native and opens the tool in your default browser when you continue.", 14));
        var launchUri = new Uri("https://app.schoology.com/" + route.TrimStart('/'));
        var launch = new Button { Content = "Open external activity", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        AutomationProperties.SetName(launch, "Open external learning activity in browser");
        launch.Click += async (_, _) =>
        {
            if (!await Launcher.LaunchUriAsync(launchUri))
                await ShowApiErrorAsync("Unable to open external activity", "Windows could not open the external activity.");
        };
        panel.Children.Add(launch);
        panel.Children.Add(Action("Back to Home", (_, _) => ShowHome("Recent Activity")));
        if (_sectionFrame is not null) NavigateMainSection(panel, title);
        else Reset(BuildApplicationShell(title, panel));
    }

    private async Task<bool> TryRouteNativeSchoologyWebUrlAsync(Uri destination)
    {
        if (!IsSchoologyWebHost(destination.Host)) return false;
        return await TryRouteNativeSchoologyPathAsync(destination.AbsolutePath, destination);
    }

    private async Task<bool> TryRouteNativeSchoologyPathAsync(string path, Uri source)
    {
        path = "/" + path.Trim('/');
        var session = _session;
        if (session is null) return false;

        var courseTab = Regex.Match(path, @"^/courses?/(\d+)/(student_grades|updates|members)/?$", RegexOptions.IgnoreCase);
        if (courseTab.Success)
        {
            var id = long.Parse(courseTab.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var tab = courseTab.Groups[2].Value.ToLowerInvariant() switch
            {
                "student_grades" => "Grades",
                "updates" => "Updates",
                _ => "Members"
            };
            ShowThreePane("Courses", await ResolveDeepLinkEntityAsync("Courses", id), tab);
            return true;
        }

        var course = Regex.Match(path, @"^/(?:course|courses|section|sections)/(\d+)(?:/materials(?:/gp/(\d+))?)?/?$", RegexOptions.IgnoreCase);
        if (course.Success)
        {
            var id = long.Parse(course.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            ShowThreePane("Courses", await ResolveDeepLinkEntityAsync("Courses", id), "Materials");
            return true;
        }

        var groupMembers = Regex.Match(path, @"^/groups?/(\d+)/members/?$", RegexOptions.IgnoreCase);
        var group = Regex.Match(path, @"^/groups?/(\d+)(?:/.*)?$", RegexOptions.IgnoreCase);
        if (groupMembers.Success || group.Success)
        {
            var id = long.Parse((groupMembers.Success ? groupMembers : group).Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            ShowThreePane("Groups", await ResolveDeepLinkEntityAsync("Groups", id),
                groupMembers.Success ? "Members" : "Updates");
            return true;
        }

        var school = Regex.Match(path, @"^/schools?/(\d+)(?:/.*)?$", RegexOptions.IgnoreCase);
        if (school.Success)
        {
            var id = long.Parse(school.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            ShowThreePane("School Profile", await ResolveDeepLinkEntityAsync("School Profile", id), "Updates");
            return true;
        }

        var profile = Regex.Match(path, @"^/(?:user|users|profile|profiles)/(?:view/)?(\d+)(?:/.*)?$", RegexOptions.IgnoreCase);
        if (profile.Success)
        {
            ShowUserProfile(long.Parse(profile.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            return true;
        }

        if (Regex.IsMatch(path, @"^/messages(?:/sent)?/?$", RegexOptions.IgnoreCase))
        {
            _pendingProtocolMailbox = path.Contains("/sent", StringComparison.OrdinalIgnoreCase) ? "Sent" : "Inbox";
            _pendingProtocolMessageId = 0;
            ShowHome("Messages");
            return true;
        }
        var message = Regex.Match(path, @"^/messages/view/(\d+)/?$", RegexOptions.IgnoreCase);
        if (message.Success)
        {
            _pendingProtocolMessageId = long.Parse(message.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            _pendingProtocolMessageSent = false;
            _pendingProtocolMailbox = "Inbox";
            ShowHome("Messages");
            return true;
        }

        if (Regex.IsMatch(path, @"^/home/course-dashboard(?:/.*)?$", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(path, @"^/home/recent-activity/?$", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(path, @"^/home/?$", RegexOptions.IgnoreCase))
        {
            ShowHome("Recent Activity");
            return true;
        }
        if (Regex.IsMatch(path, @"^/grades/grades/?$", RegexOptions.IgnoreCase))
        {
            ShowHome("Grades");
            return true;
        }
        if (Regex.IsMatch(path, @"^/resources(?:/.*)?$", RegexOptions.IgnoreCase))
        {
            ShowHome("Resources");
            return true;
        }
        if (Regex.IsMatch(path, @"^/settings/account/?$", RegexOptions.IgnoreCase))
        {
            ShowHome("Settings");
            return true;
        }
        if (Regex.IsMatch(path, @"^/people/?$", RegexOptions.IgnoreCase))
        {
            ShowNativeSchoologyRoutePage("People", path);
            return true;
        }

        var assignment = Regex.Match(path, @"^/(?:sections?|courses?)/(\d+)/assignments?/(\d+)(?:/.*)?$", RegexOptions.IgnoreCase);
        var parentAssignment = Regex.Match(path, @"^/parents?/\d+/sections?/(\d+)/assignments?/(\d+)(?:/.*)?$", RegexOptions.IgnoreCase);
        if (assignment.Success || parentAssignment.Success)
        {
            var match = assignment.Success ? assignment : parentAssignment;
            var sectionId = long.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var assignmentId = long.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            await ShowAssignmentDetailsAsync(sectionId, DeepLinkRecord("Assignment", assignmentId));
            return true;
        }
        var discussion = Regex.Match(path, @"^/(sections?)/(\d+)/discussions?/(\d+)(?:/.*)?$", RegexOptions.IgnoreCase);
        var groupDiscussion = Regex.Match(path, @"^/groups?/(\d+)/discussions?/(\d+)(?:/.*)?$", RegexOptions.IgnoreCase);
        if (discussion.Success || groupDiscussion.Success)
        {
            var match = discussion.Success ? discussion : groupDiscussion;
            var kind = discussion.Success ? "Courses" : "Groups";
            var entityId = long.Parse(match.Groups[2 - (discussion.Success ? 0 : 1)].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            var itemId = long.Parse(match.Groups[3 - (discussion.Success ? 0 : 1)].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            await ShowDiscussionDetailsAsync(kind, entityId, DeepLinkRecord("Discussion", itemId));
            return true;
        }
        var eventRoute = Regex.Match(path, @"^/(sections?)/(\d+)/events?/(\d+)(?:/.*)?$", RegexOptions.IgnoreCase);
        var groupEvent = Regex.Match(path, @"^/groups?/(\d+)/events?/(\d+)(?:/.*)?$", RegexOptions.IgnoreCase);
        if (eventRoute.Success || groupEvent.Success)
        {
            var match = eventRoute.Success ? eventRoute : groupEvent;
            var kind = eventRoute.Success ? "Courses" : "Groups";
            var entityId = long.Parse(match.Groups[2 - (eventRoute.Success ? 0 : 1)].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            var itemId = long.Parse(match.Groups[3 - (eventRoute.Success ? 0 : 1)].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            await ShowEventDetailsAsync(kind, entityId, DeepLinkRecord("Event", itemId));
            return true;
        }
        var page = Regex.Match(path, @"^/(?:sections?|courses?)/(\d+)/pages?/(\d+)(?:/.*)?$", RegexOptions.IgnoreCase);
        if (page.Success)
        {
            var sectionId = long.Parse(page.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var pageId = long.Parse(page.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            await ShowCoursePageAsync(sectionId, DeepLinkRecord("Page", pageId));
            return true;
        }
        var courseApp = Regex.Match(path, @"^/(?:sections?|courses?)/(\d+)/(?:applications?|application)/(\d+)/?$", RegexOptions.IgnoreCase);
        if (courseApp.Success)
        {
            var sectionId = long.Parse(courseApp.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var appId = long.Parse(courseApp.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            var entity = await ResolveDeepLinkEntityAsync("Courses", sectionId);
            await LaunchCourseAppAsync(sectionId, appId, "Course app", entity);
            return true;
        }
        var tool = Regex.Match(path, @"^/(?:course|courses|section|sections)/(\d+)/(external-tool|lti|scorm)/(\d+)/?$", RegexOptions.IgnoreCase);
        if (tool.Success)
        {
            var title = tool.Groups[2].Value.ToLowerInvariant() switch
            {
                "lti" => "LTI assignment",
                "scorm" => "SCORM activity",
                _ => "External tool"
            };
            ShowNativeExternalToolPage(title, $"section/{tool.Groups[1].Value}/{tool.Groups[2].Value}/{tool.Groups[3].Value}");
            return true;
        }
        var assessment = Regex.Match(path, @"^/(?:course|courses)/(\d+)/assessments?/(\d+)(?:/.*)?$", RegexOptions.IgnoreCase);
        if (assessment.Success)
        {
            ShowNativeExternalToolPage("Assessment", $"assignment/{assessment.Groups[2].Value}");
            return true;
        }
        var globalAssignment = Regex.Match(path, @"^/(?:assignment|assignments)/(\d+)(?:/info)?$", RegexOptions.IgnoreCase);
        if (globalAssignment.Success)
        {
            var assignmentId = long.Parse(globalAssignment.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            foreach (var candidate in _courses)
            {
                try
                {
                    using var assignments = await _api.GetAssignmentsAsync(session, candidate.Id);
                    var match = SchoologyApiClient.ReadRecords(assignments.RootElement, "assignment", "assignments")
                        .FirstOrDefault(item => JsonLong(item.Data, "id", "assignment_id", "assignmentId") == assignmentId);
                    if (match is not null)
                    {
                        await ShowAssignmentDetailsAsync(candidate.Id, match);
                        return true;
                    }
                }
                catch (Exception ex) { App.WriteStartupLog($"Could not resolve assignment {assignmentId} in section {candidate.Id}.", ex); }
            }
            ShowNativeSchoologyRoutePage("Assignment", path);
            return true;
        }

        ShowNativeSchoologyRoutePage("Schoology link", path);
        return true;
    }

    internal void ShowFromBackgroundNotification()
    {
        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        DestroyTrayIcon();
        _isBackgroundHidden = false;
        ShowNativeWindow(hwnd, 9);
        SetForegroundWindow(hwnd);
        if (_releaseNotesPending)
        {
            _releaseNotesPending = false;
            _ = ShowPendingStartupDialogsAsync();
        }
        else if (_pendingUpdateInfo is { } pendingUpdate)
        {
            _pendingUpdateInfo = null;
            _ = ShowUpdateAvailableDialogAsync(pendingUpdate);
        }
    }

    private async Task ShowPendingStartupDialogsAsync()
    {
        await CheckAndShowReleaseNotesAsync();
        if (_pendingUpdateInfo is { } pendingUpdate)
        {
            _pendingUpdateInfo = null;
            await ShowUpdateAvailableDialogAsync(pendingUpdate);
        }
    }

    private void SetWindowSize(int widthDip, int heightDip)
    {
        try
        {
            var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
            var scale = GetDpiForWindow(hwnd) / 96.0;
            AppWindow.Resize(new SizeInt32((int)(widthDip * scale), (int)(heightDip * scale)));
        }
        catch { }
    }


    private sealed record SavedWindowState(int Width, int Height, bool Maximized);
    private SizeInt32 _lastNormalWindowSize;

    private void RestoreWindowState()
    {
        try
        {
            _restoringWindowState = true;
            SavedWindowState? saved = File.Exists(WindowStatePath)
                ? JsonSerializer.Deserialize<SavedWindowState>(File.ReadAllText(WindowStatePath)) : null;
            if (saved is { Width: >= 640, Height: >= 480 })
            {
                _lastNormalWindowSize = new SizeInt32(saved.Width, saved.Height);
                AppWindow.Resize(_lastNormalWindowSize);
                if (saved.Maximized && AppWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
                App.WriteStartupLog($"Restored previous window size {saved.Width}x{saved.Height}px; maximized={saved.Maximized}.");
            }
            else
            {
                SetWindowSize(1260, 820);
                var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
                var scale = GetDpiForWindow(hwnd) / 96.0;
                _lastNormalWindowSize = new SizeInt32((int)(1260 * scale), (int)(820 * scale));
            }
        }
        catch (Exception ex)
        {
            App.WriteStartupLog("Could not restore previous window state.", ex);
            SetWindowSize(1260, 820);
        }
        finally { _restoringWindowState = false; }
    }

    private void SaveWindowState()
    {
        if (_restoringWindowState) return;
        try
        {
            var maximized = AppWindow.Presenter is OverlappedPresenter presenter &&
                presenter.State == OverlappedPresenterState.Maximized;
            var size = AppWindow.Size;
            if (!maximized && size.Width >= 640 && size.Height >= 480) _lastNormalWindowSize = size;
            if (_lastNormalWindowSize.Width < 640 || _lastNormalWindowSize.Height < 480) return;
            Directory.CreateDirectory(Path.GetDirectoryName(WindowStatePath)!);
            File.WriteAllText(WindowStatePath, JsonSerializer.Serialize(
                new SavedWindowState(_lastNormalWindowSize.Width, _lastNormalWindowSize.Height, maximized)));
        }
        catch (Exception ex) { App.WriteStartupLog("Could not persist window state.", ex); }
    }

    private static SolidColorBrush Brush(string color)
    {
        // ThemeBrush supplies #AARRGGBB values, while a few call sites use
        // #RRGGBB. Parse both formats correctly; treating the alpha byte as
        // red made all opaque themed colors render with a bright-red channel.
        var hex = color.TrimStart('#');
        return hex.Length switch
        {
            6 => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(
                255,
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16))),
            8 => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16),
                Convert.ToByte(hex.Substring(6, 2), 16))),
            _ => throw new FormatException($"Expected #RRGGBB or #AARRGGBB color, received '{color}'.")
        };
    }
    private TextBlock Text(string value, double size = 14, bool bold = false) => new()
    {
        Text = value, FontSize = size, FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
        Foreground = ThemeBrush("SchoologyTextBrush")
    };

    private Grid LoadingStatus(string message) => LoadingStatus(message, out var ignoredStatus);

    private Grid LoadingStatus(string message, out TextBlock status, double ringSize = 18)
    {
        var host = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var animationsEnabled = AnimationsEnabled;
        var ring = new ProgressRing
        {
            Width = ringSize, Height = ringSize, IsActive = animationsEnabled,
            Visibility = animationsEnabled ? Visibility.Visible : Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        // Reduced-motion mode keeps a static indicator instead of animating the ring.
        var staticIndicator = new FontIcon
        {
            Glyph = "", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16,
            Foreground = ThemeBrush("SchoologyBlueBrush"),
            Visibility = animationsEnabled ? Visibility.Collapsed : Visibility.Visible,
            VerticalAlignment = VerticalAlignment.Center
        };
        status = Text(message, 13);
        status.Tag = (ring, staticIndicator);
        host.Children.Add(ring);
        host.Children.Add(staticIndicator);
        Grid.SetColumn(status, 1);
        host.Children.Add(status);
        return host;
    }

    private static void SetStatusText(TextBlock status, string value)
    {
        status.Text = value;
        if (status.Tag is not ValueTuple<ProgressRing, FontIcon> indicators) return;
        var active = value.StartsWith("Loading", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("Refreshing", StringComparison.OrdinalIgnoreCase);
        var animate = active && AnimationsEnabled;
        indicators.Item1.IsActive = animate;
        indicators.Item1.Visibility = animate ? Visibility.Visible : Visibility.Collapsed;
        indicators.Item2.Visibility = active && !AnimationsEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void RemoveLoadingStatus(TextBlock status)
    {
        if (status.Tag is ValueTuple<ProgressRing, FontIcon> indicators &&
            indicators.Item1.Parent is Grid host && host.Parent is Panel parent)
            parent.Children.Remove(host);
        else if (status.Parent is Panel directParent)
            directParent.Children.Remove(status);
    }
    private Border EmptyState(string title, string? description = null)
    {
        var content = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 520,
            Margin = new Thickness(20)
        };
        content.Children.Add(Text(title, 18, true));
        if (!string.IsNullOrWhiteSpace(description))
        {
            var detail = Text(description, 13);
            detail.Foreground = ThemeBrush("SchoologyMutedTextBrush");
            content.Children.Add(detail);
        }
        return Surface(content, new Thickness(20));
    }

    private static Button Action(string label, RoutedEventHandler handler, bool primary = false)
    {
        var b = new Button { Content = label, Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Stretch };
        if (primary) { b.Background = Brush("#2E66A3"); b.Foreground = Brush("#FFFFFF"); }
        b.Click += handler; return b;
    }
    private bool ReadHighContrastSetting()
    {
        if (!_highContrastReadAvailable) return false;

        var settings = new HighContrastSettings
        {
            Size = (uint)Marshal.SizeOf<HighContrastSettings>()
        };
        if (SystemParametersInfo(SpiGetHighContrast, settings.Size, ref settings, 0))
        {
            var enabled = (settings.Flags & HighContrastOn) != 0;
            if (enabled)
            {
                var ui = new UISettings();
                var foreground = ui.GetColorValue(UIColorType.Foreground);
                var background = ui.GetColorValue(UIColorType.Background);
                App.WriteStartupLog($"Windows High Contrast is enabled; using system-selected foreground #{foreground.R:X2}{foreground.G:X2}{foreground.B:X2} and background #{background.R:X2}{background.G:X2}{background.B:X2}.");
            }
            return enabled;
        }

        _highContrastReadAvailable = false;
        App.WriteStartupLog(
            $"Could not read the current High Contrast setting through SystemParametersInfo (Win32 error {Marshal.GetLastWin32Error()}); using the app theme.");
        return false;
    }

    private SolidColorBrush ThemeBrush(string key)
    {
        // Resolve the small set of code-behind theme surfaces directly. Accessing
        // ThemeDictionaries by the "Light" key throws a WinRT E_FAIL in the
        // unpackaged startup path even though the XAML dictionary declares it.
        // Avoid that fragile lookup so the splash screen never triggers a
        // stowed XAML exception while its brushes are being created.
        if (_highContrastEnabled)
        {
            var systemColors = new UISettings();
            var accent = systemColors.GetColorValue(UIColorType.Accent);
            var foreground = systemColors.GetColorValue(UIColorType.Foreground);
            var background = systemColors.GetColorValue(UIColorType.Background);
            var color = key switch
            {
                "SchoologyBlueBrush" or "SchoologyBlueDarkBrush" => accent,
                "SchoologySurfaceBrush" or "SchoologySubtleBrush" => background,
                "SchoologyDividerBrush" or "SchoologyTextBrush" or "SchoologyMutedTextBrush" => foreground,
                _ => accent
            };
            return new SolidColorBrush(color);
        }

        var dark = _root.ActualTheme == ElementTheme.Dark;
        var colorHex = key switch
        {
            "SchoologyBlueBrush" => dark ? "#FF5B9BD5" : "#FF2E66A3",
            "SchoologyBlueDarkBrush" => dark ? "#FF183A5B" : "#FF204D7B",
            "SchoologySurfaceBrush" => dark ? "#FF202428" : "#FFFFFFFF",
            "SchoologySubtleBrush" => dark ? "#FF292F34" : "#FFF2F5F7",
            "SchoologyDividerBrush" => dark ? "#FF454545" : "#FFD0D0D0",
            "SchoologyTextBrush" => dark ? "#FFF5F5F5" : "#FF1A1A1A",
            "SchoologyMutedTextBrush" => dark ? "#FFB8B8B8" : "#FF666666",
            _ => dark ? "#FF5B9BD5" : "#FF2E66A3"
        };
        return Brush(colorHex);
    }

    private Brush InAppAcrylicBrush(double lightTintOpacity = 0.34, double darkTintOpacity = 0.42)
    {
        // Keep system High Contrast fully opaque and system-colored. In normal
        // themes, a restrained in-app acrylic tint lets the window's Mica
        // backdrop remain perceptible through cards and secondary surfaces.
        if (_highContrastEnabled) return ThemeBrush("SchoologySurfaceBrush");
        var dark = _root.ActualTheme == ElementTheme.Dark;
        return new AcrylicBrush
        {
            TintColor = dark
                ? Microsoft.UI.ColorHelper.FromArgb(255, 30, 35, 40)
                : Microsoft.UI.ColorHelper.FromArgb(255, 248, 250, 252),
            TintOpacity = dark ? darkTintOpacity : lightTintOpacity,
            FallbackColor = dark
                ? Microsoft.UI.ColorHelper.FromArgb(255, 32, 36, 40)
                : Microsoft.UI.ColorHelper.FromArgb(255, 255, 255, 255)
        };
    }

    private Border Surface(UIElement child, Thickness? padding = null)
    {
        return new Border
        {
            Background = InAppAcrylicBrush(),
            BorderBrush = ThemeBrush("SchoologyDividerBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = padding ?? new Thickness(16),
            Child = child,
            Margin = new Thickness(0, 0, 0, 12)
        };
    }
    private void Reset(UIElement content)
    {
        var previousPage = _lastRenderedPage;
        var destinationPage = _page;
        _root.Children.Clear();

        // The initial splash is the first visual attached to the native Window.
        // Keep that first attachment transition-free: applying an EntranceThemeTransition
        // before the root has completed its initial XAML load can trigger a stowed
        // XAML exception in the unpackaged startup path. Normal page navigation keeps
        // the native transition, and all transitions still respect reduced motion.
        if (destinationPage == "splash" || !new UISettings().AnimationsEnabled)
        {
            content.Transitions = null;
        }
        else
        {
            // Returning to the login root moves in from the left; opening a child
            // screen moves in from the right; sibling/home navigation moves forward.
            var returningToRoot = destinationPage == "login" &&
                previousPage is not "splash" and not "login";
            var movingBackToHome = destinationPage == "home" &&
                previousPage is not "login" and not "splash" &&
                previousPage != "home";
            var horizontalOffset = returningToRoot || movingBackToHome ? -36 : 36;
            content.Transitions = new TransitionCollection
            {
                new EntranceThemeTransition
                {
                    FromHorizontalOffset = horizontalOffset,
                    FromVerticalOffset = 8
                }
            };
        }

        _root.Children.Add(content);
        _lastRenderedPage = destinationPage;
    }

    private async Task SetWindowIconAsync()
    {
        try
        {
            var assembly = typeof(MainWindow).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("Schoology.WinUI.Assets.ClassWire.ico", StringComparison.Ordinal));
            if (resourceName is null)
            {
                App.WriteStartupLog("Embedded ClassWire window icon resource was not found.");
                return;
            }

            await using var source = assembly.GetManifestResourceStream(resourceName);
            if (source is null) return;
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer);
            var iconDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Schoology", "Assets");
            Directory.CreateDirectory(iconDirectory);
            var iconPath = Path.Combine(iconDirectory, "ClassWire.ico");
            await File.WriteAllBytesAsync(iconPath, buffer.ToArray());
            AppWindow.SetIcon(iconPath);
            App.WriteStartupLog("Native ClassWire window icon applied.");
        }
        catch (Exception ex)
        {
            App.WriteStartupLog("Could not apply the native ClassWire window icon.", ex);
        }
    }

    private StackPanel CreateClassWireBrandLockup(double markSize, double fontSize, double spacing)
    {
        var brandBlue = Microsoft.UI.ColorHelper.FromArgb(255, 72, 175, 236);
        var logo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var brandMark = new Grid { Width = markSize, Height = markSize };
        brandMark.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Margin = new Thickness(markSize * 0.042), Stroke = new SolidColorBrush(brandBlue),
            StrokeThickness = markSize * 0.083
        });
        // The ClassWire C is native vector geometry, crisp at every scale.
        Windows.Foundation.Point MarkPoint(double x, double y) => new(
            markSize / 2 + (x - 9.1) * markSize / 20 * 0.70,
            markSize / 2 + (y - 10) * markSize / 20 * 0.70);
        var glyphFigure = new Microsoft.UI.Xaml.Media.PathFigure
        {
            StartPoint = MarkPoint(15.7, 4.6), IsClosed = true
        };
        glyphFigure.Segments.Add(new Microsoft.UI.Xaml.Media.BezierSegment { Point1 = MarkPoint(13.5, 2.9), Point2 = MarkPoint(11.8, 2), Point3 = MarkPoint(9.8, 2) });
        glyphFigure.Segments.Add(new Microsoft.UI.Xaml.Media.BezierSegment { Point1 = MarkPoint(5, 2), Point2 = MarkPoint(2, 5.3), Point3 = MarkPoint(2, 10) });
        glyphFigure.Segments.Add(new Microsoft.UI.Xaml.Media.BezierSegment { Point1 = MarkPoint(2, 14.7), Point2 = MarkPoint(5, 18), Point3 = MarkPoint(9.8, 18) });
        glyphFigure.Segments.Add(new Microsoft.UI.Xaml.Media.BezierSegment { Point1 = MarkPoint(11.8, 18), Point2 = MarkPoint(13.5, 17.1), Point3 = MarkPoint(15.7, 15.4) });
        glyphFigure.Segments.Add(new Microsoft.UI.Xaml.Media.LineSegment { Point = MarkPoint(13.5, 13.2) });
        glyphFigure.Segments.Add(new Microsoft.UI.Xaml.Media.BezierSegment { Point1 = MarkPoint(12.5, 14.5), Point2 = MarkPoint(11.2, 14.8), Point3 = MarkPoint(9.8, 14.8) });
        glyphFigure.Segments.Add(new Microsoft.UI.Xaml.Media.BezierSegment { Point1 = MarkPoint(6.8, 14.8), Point2 = MarkPoint(5.1, 12.8), Point3 = MarkPoint(5.1, 10) });
        glyphFigure.Segments.Add(new Microsoft.UI.Xaml.Media.BezierSegment { Point1 = MarkPoint(5.1, 7.2), Point2 = MarkPoint(6.8, 5.2), Point3 = MarkPoint(9.8, 5.2) });
        glyphFigure.Segments.Add(new Microsoft.UI.Xaml.Media.BezierSegment { Point1 = MarkPoint(11.2, 5.2), Point2 = MarkPoint(12.5, 5.5), Point3 = MarkPoint(13.5, 6.8) });
        glyphFigure.Segments.Add(new Microsoft.UI.Xaml.Media.LineSegment { Point = MarkPoint(15.7, 4.6) });
        brandMark.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = new Microsoft.UI.Xaml.Media.PathGeometry { Figures = { glyphFigure } },
            Width = markSize, Height = markSize,
            Fill = ThemeBrush("SchoologyTextBrush"), Stretch = Microsoft.UI.Xaml.Media.Stretch.None,
            Margin = new Thickness(0)
        });
        logo.Children.Add(brandMark);
        var wordmark = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = fontSize,
            FontWeight = Microsoft.UI.Text.FontWeights.Light, VerticalAlignment = VerticalAlignment.Center };
        wordmark.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "Class", Foreground = ThemeBrush("SchoologyTextBrush") });
        wordmark.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "Wire", Foreground = new SolidColorBrush(brandBlue) });
        logo.Children.Add(wordmark);
        AutomationProperties.SetName(logo, "ClassWire");
        AutomationProperties.SetAccessibilityView(logo, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content);
        return logo;
    }

    private void ShowSplash()
    {
        _page = "splash";
        // Use a native ClassWire lockup over the Window's Mica backdrop.
        var grid = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
        App.WriteStartupLog("Splash root Grid created.");
        grid.Loaded += (_, _) => App.WriteStartupLog("Splash root Grid Loaded event received.");
        var firstLayoutLogged = false;
        grid.LayoutUpdated += (_, _) =>
        {
            if (firstLayoutLogged) return;
            firstLayoutLogged = true;
            App.WriteStartupLog($"Splash root completed its first layout ({grid.ActualWidth:0.##}x{grid.ActualHeight:0.##}).");
        };
        var stack = new StackPanel { Width = 360, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Spacing = 18 };
        var logo = CreateClassWireBrandLockup(54, 46, 12);
        logo.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(logo);
        var ring = new ProgressRing { IsActive = new UISettings().AnimationsEnabled, Width = 28, Height = 28,
            Foreground = ThemeBrush("SchoologyBlueBrush"), HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(ring);
        var caption = Text("Getting ClassWire ready…", 13);
        caption.Foreground = ThemeBrush("SchoologyMutedTextBrush");
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(caption);
        grid.Children.Add(stack);
        Reset(grid);
    }

    private void ShowLogin()
    {
        App.WriteStartupLog("ShowLogin entered.");
        _notificationTimer?.Stop();
        _page = "login";
        _sectionFrame = null;
        _ = StopQrScannerAsync();
        App.WriteStartupLog("Login page QR scanner shutdown requested.");
        var dark = _root.ActualTheme == ElementTheme.Dark;
        App.WriteStartupLog($"Building login page; actual theme={_root.ActualTheme}, high contrast={_highContrastEnabled}.");
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = new SolidColorBrush(Colors.Transparent)
        };
        var stack = new StackPanel
        {
            Width = 440,
            MaxWidth = 460,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 14,
            Margin = new Thickness(16, 12, 16, 12)
        };
        // Native ClassWire lockup: circular C mark and light-weight wordmark.
        var logo = CreateClassWireBrandLockup(38, 34, 10);
        logo.Margin = new Thickness(0, 0, 0, 20);
        stack.Children.Add(logo);
        stack.Children.Add(LoginActionButton("Sign in through my school", "ic_school.svg", ShowSchoolSignInPage));
        App.WriteStartupLog("Login school sign-in button created.");
        stack.Children.Add(LoginActionButton("Sign in with my account", "ic_person.svg", ShowAccountSignInPage));
        App.WriteStartupLog("Login account sign-in button created.");
        stack.Children.Add(LoginActionButton("Sign in with a QR code", "ic_qr_code.svg", ShowQrSignInPage));
        App.WriteStartupLog("Login QR sign-in button created.");
        var help = new Button
        {
            Content = "I need help signing in",
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Foreground = ThemeBrush("SchoologyTextBrush")
        };
        help.Click += (_, _) => ShowSignInHelpPage();
        stack.Children.Add(help);
        // The window remains transparent so Mica is visible. Acrylic is only
        // used on this compact, vertically centered card.
        scroll.Content = stack;
        scroll.MaxHeight = 720;
        var card = new Border
        {
            Width = 520,
            MaxWidth = 520,
            MaxHeight = 760,
            Background = new AcrylicBrush
            {
                TintColor = dark
                    ? Microsoft.UI.ColorHelper.FromArgb(255, 32, 36, 40)
                    : Microsoft.UI.ColorHelper.FromArgb(255, 248, 250, 252),
                TintOpacity = 0.22,
                FallbackColor = dark
                    ? Microsoft.UI.ColorHelper.FromArgb(255, 32, 36, 40)
                    : Microsoft.UI.ColorHelper.FromArgb(255, 248, 250, 252)
            },
            BorderBrush = ThemeBrush("SchoologyDividerBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(0),
            Child = scroll,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var page = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
        page.Children.Add(card);
        page.SizeChanged += (_, args) =>
        {
            card.Width = Math.Min(520, Math.Max(220, args.NewSize.Width - 48));
            scroll.MaxHeight = Math.Max(240, args.NewSize.Height - 48);
            // Keep horizontal insets fixed; do not increase them with the window
            // size. The previous wide-window margin calculation squeezed the
            // buttons inside their card and clipped their outer edges.
            stack.Margin = new Thickness(16, args.NewSize.Height < 560 ? 8 : 16, 16, args.NewSize.Height < 560 ? 8 : 16);
            stack.Width = Math.Max(180, card.Width - stack.Margin.Left - stack.Margin.Right);
            logo.MaxWidth = Math.Max(180, stack.Width - 40);
        };
        Reset(page);
        App.WriteStartupLog("Login visual tree attached to the window.");
        AutomationProperties.SetName(logo, "ClassWire");
        AutomationProperties.SetAccessibilityView(logo, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content);
    }

    private Button LoginActionButton(string label, string iconResource, Action navigate)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var image = new Image
        {
            Width = 26,
            Height = 26,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center
        };
        var text = new TextBlock
        {
            Text = label,
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        AutomationProperties.SetName(image, label + " icon");
        row.Children.Add(image);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var button = new Button
        {
            Content = row,
            Padding = new Thickness(18, 14, 18, 14),
            MinHeight = 58,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetHelpText(button, label);
        button.Click += (_, _) => navigate();
        _ = SetEmbeddedImageAsync(image, $"Schoology.WinUI.Assets.{iconResource}");
        return button;
    }

    private Grid CreateLoginSubpage(string title, string subtitle, out StackPanel content)
    {
        _page = title;
        var dark = _root.ActualTheme == ElementTheme.Dark;
        var grid = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid { Margin = new Thickness(20, 14, 20, 14), ColumnSpacing = 14 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var back = new Button { Content = "← Back", Padding = new Thickness(12, 8, 12, 8), VerticalAlignment = VerticalAlignment.Center };
        back.Click += (_, _) => ShowLogin();
        header.Children.Add(back);
        var heading = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(Text(title, 21, true));
        heading.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Foreground = Brush(dark ? "#FFC5CBD1" : "#FF52616D"), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(heading, 1);
        header.Children.Add(heading);
        Grid.SetRow(header, 0);
        grid.Children.Add(header);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        content = new StackPanel
        {
            Spacing = 14,
            Width = 620,
            MaxWidth = 620,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0)
        };
        // Only the form card uses Acrylic; the surrounding page shows Mica.
        scroll.Content = new Border
        {
            Background = new AcrylicBrush
            {
                TintColor = dark
                    ? Microsoft.UI.ColorHelper.FromArgb(255, 32, 36, 40)
                    : Microsoft.UI.ColorHelper.FromArgb(255, 248, 250, 252),
                TintOpacity = 0.24,
                FallbackColor = dark
                    ? Microsoft.UI.ColorHelper.FromArgb(255, 32, 36, 40)
                    : Microsoft.UI.ColorHelper.FromArgb(255, 248, 250, 252)
            },
            BorderBrush = ThemeBrush("SchoologyDividerBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24),
            Child = content,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);
        // Copy the out parameter to a local before capturing it in the event handler.
        // C# does not allow ref/out parameters to be captured by lambdas.
        var responsiveContent = content;
        grid.SizeChanged += (_, args) => responsiveContent.Width = Math.Min(620, Math.Max(180, args.NewSize.Width - 144));
        Reset(grid);
        return grid;
    }

    private async Task SetEmbeddedImageAsync(Image image, string resourceName)
    {
        try
        {
            await SetEmbeddedImageCoreAsync(image, resourceName);
        }
        catch (Exception ex)
        {
            // Page builders intentionally fire-and-forget image loads; contain
            // failures here instead of leaving unobserved startup task faults.
            App.WriteStartupLog($"Could not load embedded image resource {resourceName}.", ex);
        }
    }

    private async Task SetEmbeddedImageCoreAsync(Image image, string resourceName)
    {
        var assembly = typeof(MainWindow).Assembly;
        var resolvedName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.Equals(resourceName, StringComparison.Ordinal) ||
                                    name.EndsWith(resourceName, StringComparison.Ordinal));

        if (resolvedName is null)
        {
            App.WriteStartupLog($"Embedded image resource was not found: {resourceName}");
            return;
        }

        await using var source = assembly.GetManifestResourceStream(resolvedName);
        if (source is null)
        {
            App.WriteStartupLog($"Embedded image resource could not be opened: {resolvedName}");
            return;
        }

        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer);
        var imageBytes = buffer.ToArray();

        if (resourceName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            // The supplied Schoology school/QR glyphs are white artwork intended
            // for the old blue login background. Recolor their white paths for
            // contrast on native light-mode buttons; retain white in dark mode.
            if (_root.ActualTheme != ElementTheme.Dark)
            {
                var markup = Encoding.UTF8.GetString(imageBytes)
                    .Replace("#ffffff", "#204D7B", StringComparison.OrdinalIgnoreCase);
                imageBytes = Encoding.UTF8.GetBytes(markup);
            }
        }

        using var randomAccessStream = new InMemoryRandomAccessStream();
        using (var output = randomAccessStream.GetOutputStreamAt(0))
        using (var writer = new DataWriter(output))
        {
            writer.WriteBytes(imageBytes);
            await writer.StoreAsync();
            writer.DetachStream();
        }

        randomAccessStream.Seek(0);
        if (resourceName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            var vector = new SvgImageSource();
            await vector.SetSourceAsync(randomAccessStream);
            image.Source = vector;
        }
        else
        {
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(randomAccessStream);
            image.Source = bitmap;
        }
    }

    private void ShowSchoolSignInPage()
    {
        CreateLoginSubpage("Sign in through my school", "Find your school, then continue through its secure sign-in page.", out var content);
        var search = new AutoSuggestBox
        {
            Header = "School or organization",
            PlaceholderText = "Search by school name or domain",
            UpdateTextOnSelect = false
        };
        var status = Text("Select your school to continue.", 13);
        var continueButton = new Button { Content = "Continue to school sign-in", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
        var browser = new WebView2 { MinHeight = 420, Visibility = Visibility.Collapsed };
        SchoologyAuthService.School? selectedSchool = null;
        search.TextChanged += async (_, args) =>
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            selectedSchool = null;
            continueButton.IsEnabled = false;
            if (search.Text.Trim().Length < 2) return;
            try
            {
                var matches = await _auth.SearchSchoolsAsync(search.Text);
                search.ItemsSource = matches;
                search.IsSuggestionListOpen = matches.Count > 0;
                SetStatusText(status, matches.Count == 0 ? "No matching schools found. Try another name or domain." : "Choose your school from the suggestions.");
            }
            catch (Exception ex) { SetStatusText(status, ex.Message); }
        };
        search.SuggestionChosen += (_, args) =>
        {
            if (args.SelectedItem is SchoologyAuthService.School school)
            {
                selectedSchool = school;
                search.Text = school.Name;
                continueButton.IsEnabled = true;
                SetStatusText(status, $"Selected {school.Name}. Continue to open its secure sign-in page.");
            }
        };
        continueButton.Click += async (_, _) =>
        {
            if (selectedSchool is null) return;
            continueButton.IsEnabled = false;
            SetStatusText(status, "Preparing secure school sign-in…");
            try
            {
                search.Visibility = Visibility.Collapsed;
                continueButton.Visibility = Visibility.Collapsed;
                await OpenRemoteSchoolLoginAsync(selectedSchool, browser, status, search);
            }
            catch (Exception ex)
            {
                SetStatusText(status, ex.Message);
                continueButton.IsEnabled = true;
            }
        };
        content.Children.Add(search);
        content.Children.Add(status);
        content.Children.Add(continueButton);
        content.Children.Add(browser);
    }

    private void ShowAccountSignInPage()
    {
        CreateLoginSubpage("Sign in with my account", "Use your ClassWire username or email and password.", out var content);
        var username = new TextBox { Header = "Username or email", PlaceholderText = "Enter your username or email" };
        var password = new PasswordBox { Header = "Password", PlaceholderText = "Enter your password" };
        var status = Text("", 13);
        var browser = new WebView2 { MinHeight = 420, Visibility = Visibility.Collapsed };
        var credentialForm = new StackPanel { Spacing = 14 };
        var signIn = new Button { Content = "Sign in", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch };
        signIn.Click += async (_, _) =>
        {
            var enteredUsername = username.Text.Trim();
            var at = enteredUsername.LastIndexOf('@');
            var emailDomain = at > 0 && at < enteredUsername.Length - 1
                ? enteredUsername[(at + 1)..].Trim().ToLowerInvariant()
                : string.Empty;
            var isLausdDomain = emailDomain == "lausd.net" ||
                emailDomain.EndsWith(".lausd.net", StringComparison.Ordinal) ||
                emailDomain == "lauds.net" ||
                emailDomain.EndsWith(".lauds.net", StringComparison.Ordinal);

            if (isLausdDomain)
            {
                var suggestedUrl = "https://lms.lausd.net";
                var dialog = new ContentDialog
                {
                    Title = "Your school uses SSO/Remote Authentication",
                    Content = "Your school is currently using SSO/Remote Authentication. Did you mean to log in from " + suggestedUrl + "?",
                    PrimaryButtonText = "Continue",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = _root.XamlRoot,
                    RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
                };
                var choice = await dialog.ShowAsync();
                if (choice == ContentDialogResult.Primary)
                {
                    var remoteSchool = new SchoologyAuthService.School(
                        "Los Angeles Unified School District", string.Empty,
                        "remote_auth", suggestedUrl, "lausd.net");
                    await OpenRemoteSchoolLoginAsync(remoteSchool, browser, status, credentialForm);
                }
                return;
            }

            signIn.IsEnabled = false;
            SetStatusText(status, "Signing in and verifying your ClassWire profile…");
            try
            {
                _session = await _auth.SignInAsync(username.Text, password.Password, null);
                password.Password = string.Empty;
                ShowHome();
                _ = LoadAccountCollectionsAsync();
            }
            catch (Exception ex) { SetStatusText(status, ex.Message); signIn.IsEnabled = true; }
        };
        credentialForm.Children.Add(username);
        credentialForm.Children.Add(password);
        credentialForm.Children.Add(signIn);
        content.Children.Add(credentialForm);
        content.Children.Add(status);
        content.Children.Add(browser);
    }

    private async Task OpenRemoteSchoolLoginAsync(
        SchoologyAuthService.School school,
        WebView2 browser,
        TextBlock status,
        UIElement credentialForm)
    {
        SetStatusText(status, "Preparing secure school sign-in…");
        try
        {
            var request = await _auth.BeginSchoolBrowserLoginAsync(school);
            await browser.EnsureCoreWebView2Async();
            browser.CoreWebView2.NavigationStarting += async (_, navigation) =>
            {
                if (!Uri.TryCreate(navigation.Uri, UriKind.Absolute, out var callback) ||
                    callback.Scheme != "schoology" ||
                    !callback.Host.Equals("external_login_callback", StringComparison.OrdinalIgnoreCase))
                    return;

                navigation.Cancel = true;
                var requestToken = HttpUtility.ParseQueryString(callback.Query).Get("request_token");
                if (string.IsNullOrWhiteSpace(requestToken))
                {
                    SetStatusText(status, "ClassWire returned an invalid sign-in callback. Please try again.");
                    return;
                }

                try
                {
                    SetStatusText(status, "Verifying your ClassWire account…");
                    _session = await _auth.CompleteSchoolBrowserSignInAsync(requestToken);
                    ShowHome();
                    await LoadAccountCollectionsAsync();
                }
                catch (Exception ex)
                {
                    SetStatusText(status, ex.Message);
                    credentialForm.Visibility = Visibility.Visible;
                    browser.Visibility = Visibility.Collapsed;
                }
            };

            credentialForm.Visibility = Visibility.Collapsed;
            browser.Visibility = Visibility.Visible;
            SetStatusText(status, "Complete sign-in with your school. Your password is entered only on the school sign-in page.");
            browser.Source = new Uri(request.AuthorizationUrl);
        }
        catch (Exception ex)
        {
            SetStatusText(status, ex.Message);
            credentialForm.Visibility = Visibility.Visible;
            browser.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowQrSignInPage()
    {
        CreateLoginSubpage("Sign in with a QR code", "Allow camera access and hold your ClassWire QR code in front of the camera.", out var content);
        var preview = new MediaPlayerElement
        {
            MinHeight = 320,
            AreTransportControlsEnabled = false,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
        };
        var status = Text("Camera has not started.", 13);
        var start = new Button { Content = "Start camera", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch };
        start.Click += async (_, _) =>
        {
            start.IsEnabled = false;
            SetStatusText(status, "Requesting camera access…");
            try
            {
                await StopQrScannerAsync();
                _qrScanner = new QrCameraScanner();
                _qrScanner.QrCodeDetected += (_, qr) =>
                {
                    DispatcherQueue.TryEnqueue(() => _ = CompleteQrSignInAsync(qr, status, start));
                };
                _qrPlayer = await _qrScanner.StartAsync();
                preview.SetMediaPlayer(_qrPlayer);
                _qrPlayer.Play();
                SetStatusText(status, "Camera is active. Hold the QR code steady in the frame.");
            }
            catch (Exception ex)
            {
                SetStatusText(status, $"Could not start the camera: {ex.Message}");
                start.IsEnabled = true;
            }
        };
        content.Children.Add(preview);
        content.Children.Add(status);
        content.Children.Add(start);
    }

    private async Task CompleteQrSignInAsync(string qr, TextBlock status, Button start)
    {
        start.IsEnabled = false;
        SetStatusText(status, "QR code detected. Verifying your ClassWire sign-in…");
        try
        {
            await StopQrScannerAsync();
            _session = await _auth.SignInWithQrAsync(qr);
            ShowHome();
            _ = LoadAccountCollectionsAsync();
            _ = LoadAccountCollectionsAsync();
        }
        catch (Exception ex)
        {
            SetStatusText(status, ex.Message);
            start.IsEnabled = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopQrScannerAsync();
    }

    private async Task StopQrScannerAsync()
    {
        if (_qrPlayer is not null)
        {
            try { _qrPlayer.Pause(); _qrPlayer.Dispose(); } catch { }
            _qrPlayer = null;
        }
        if (_qrScanner is not null)
        {
            try { await _qrScanner.DisposeAsync(); } catch { }
            _qrScanner = null;
        }
    }

    private async void ShowSignInHelpPage()
    {
        // Match classic port.28: this action opens the official Schoology
        // student mobile sign-in help article in the system browser.
        var helpUri = new Uri("https://docs.powerschool.com/SGYH/en/students/schoology-mobile-apps/logging-into-the-schoology-mobile-app-students");
        try
        {
            var launched = await Launcher.LaunchUriAsync(helpUri);
            if (!launched)
            {
                var dialog = new ContentDialog
                {
                    Title = "Unable to open sign-in help",
                    Content = "Windows could not open the ClassWire sign-in help page.",
                    CloseButtonText = "OK",
                    XamlRoot = _root.XamlRoot,
                    RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
                };
                await dialog.ShowAsync();
            }
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "Unable to open sign-in help",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = _root.XamlRoot,
                RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
            };
            await dialog.ShowAsync();
        }
    }

    private async Task RestoreSavedSessionAsync()
    {
        App.WriteStartupLog("RestoreSavedSessionAsync entered.");
        try
        {
            App.WriteStartupLog("Requesting saved ClassWire session from Windows Credential Locker.");
            var restored = await _auth.RestoreSessionAsync();
            App.WriteStartupLog(restored is null
                ? "No usable saved ClassWire session was restored; remaining on login."
                : $"Saved ClassWire session restored for user ID {restored.UserId}.");

            if (restored is null || _page != "login")
            {
                App.WriteStartupLog($"Skipping homepage transition; restored={restored is not null}, current page={_page}.");
                return;
            }

            _session = restored;
            App.WriteStartupLog("Building logged-in homepage from restored session.");
            ShowHome();
            App.WriteStartupLog("Logged-in homepage visual tree created.");
            await LoadAccountCollectionsAsync();
            App.WriteStartupLog("Initial account collections request finished.");
        }
        catch (Exception ex)
        {
            App.WriteStartupLog("Saved ClassWire session restoration or homepage initialization failed.", ex);
            _session = null;
            try
            {
                ShowLogin();
                App.WriteStartupLog("Returned to login after saved-session startup failure.");
            }
            catch (Exception fallbackException)
            {
                App.WriteStartupLog("Failed to return to login after homepage initialization failure.", fallbackException);
            }
        }
    }

    private async Task CheckAndShowReleaseNotesAsync()
    {
        try
        {
            // Release notes belong to the version that is actually installed.
            // Looking up /releases/latest here incorrectly shows notes for a
            // newer release before its installer has been applied.
            var installedVersion = typeof(MainWindow).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(MainWindow).Assembly.GetName().Version?.ToString()
                ?? string.Empty;
            installedVersion = installedVersion.Trim().Split('+', 2)[0];
            if (string.IsNullOrWhiteSpace(installedVersion)) return;

            var releaseTag = Uri.EscapeDataString(installedVersion);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/tling0001/classwire-winui-for-schoology/releases/tags/{releaseTag}");
            request.Headers.TryAddWithoutValidation("User-Agent", "ClassWire-WinUI");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await ReleaseNotesClient.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                App.WriteStartupLog($"No published release notes exist yet for installed version '{installedVersion}'.");
                return;
            }
            if (!response.IsSuccessStatusCode) return;

            using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = release.RootElement;
            var tag = ProfileValue(root, "tag_name");
            var body = ProfileValue(root, "body");
            if (string.IsNullOrWhiteSpace(tag) ||
                !string.Equals(tag.Trim().TrimStart('v', 'V'), installedVersion.Trim().TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(body)) return;

            var previouslyShown = File.Exists(ReleaseNotesStatePath)
                ? await File.ReadAllTextAsync(ReleaseNotesStatePath) : string.Empty;
            if (string.Equals(previouslyShown.Trim(), tag, StringComparison.Ordinal)) return;

            var notes = new StackPanel { Spacing = 8 };
            foreach (var line in body.Replace("\r", string.Empty).Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("## [", StringComparison.Ordinal)) continue;
                TextBlock paragraph;
                if (line.StartsWith("### ", StringComparison.Ordinal))
                    paragraph = Text(line[4..].TrimEnd(':'), 16, true);
                else if (line.StartsWith("- ", StringComparison.Ordinal))
                    paragraph = Text("• " + line[2..], 13);
                else
                    paragraph = Text(line, 13);
                paragraph.Foreground = ThemeBrush("SchoologyTextBrush");
                notes.Children.Add(paragraph);
            }

            var dialog = new ContentDialog
            {
                Title = $"What’s new in ClassWire {tag}",
                Content = new ScrollViewer
                {
                    Content = CreateDialogScrollViewport(notes, 560),
                    MaxHeight = 520,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                },
                CloseButtonText = "Got it",
                XamlRoot = _root.XamlRoot,
                RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
            };
            await dialog.ShowAsync();

            Directory.CreateDirectory(Path.GetDirectoryName(ReleaseNotesStatePath)!);
            await File.WriteAllTextAsync(ReleaseNotesStatePath, tag);
        }
        catch (Exception ex)
        {
            // Release notes are best-effort: offline startup must remain unaffected.
            App.WriteStartupLog("Could not retrieve or display the latest ClassWire release notes.", ex);
        }
    }

    private async void OpenDistrictSignInAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "District single sign-on",
            Content = "District SSO requires a secure embedded authorization window and ClassWire session-cookie exchange. That flow is not enabled in this phase; no district password is collected or saved.",
            CloseButtonText = "OK",
            XamlRoot = _root.XamlRoot,
            RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
        };
        await dialog.ShowAsync();
    }

    private Grid BuildApplicationShell(string section, UIElement content)
    {
        // Let the window's Mica backdrop show through the shell; retain an opaque
        // semantic surface when High Contrast is active for reliable legibility.
        var shell = new Grid { Background = _highContrastEnabled
            ? ThemeBrush("SchoologySubtleBrush")
            : new SolidColorBrush(Colors.Transparent) };
        // NavigationView fills the window so its acrylic pane continues behind the custom title bar.
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var titleBar = new TitleBar
        {
            Height = 48,
            Title = "ClassWire",
            Subtitle = section == "Recent Activity" ? "Home" : section,
            IsBackButtonVisible = false,
            IsPaneToggleButtonVisible = true
        };
        var commands = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        var createButton = new Button
        {
            Content = "＋ Create",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            VerticalAlignment = VerticalAlignment.Center
        };
        createButton.Click += (_, _) => HandleTitleBarCreateClick();
        AutomationProperties.SetAutomationId(createButton, "TitleBarCreateButton");
        _titleBarCreateButton = createButton;
        commands.Children.Add(createButton);
        titleBar.RightHeader = commands;
        titleBar.PaneToggleRequested += (_, _) =>
        {
            if (_appNavigation is not null) _appNavigation.IsPaneOpen = !_appNavigation.IsPaneOpen;
        };
        UpdateTitleBarCreateButton(section, null);

        var nav = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Auto,
            IsPaneOpen = true,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            IsPaneToggleButtonVisible = false,
            OpenPaneLength = 280,
            CompactPaneLength = 64,
            CompactModeThresholdWidth = 760,
            ExpandedModeThresholdWidth = 1120,
            Content = null
        };
        var animationsEnabled = AnimationsEnabled;
        var initialTransition = new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight };
        var sectionFrame = new Frame
        {
            Background = new SolidColorBrush(Colors.Transparent),
            Margin = new Thickness(0),
            ContentTransitions = animationsEnabled
                ? new TransitionCollection { new NavigationThemeTransition { DefaultNavigationTransitionInfo = initialTransition } }
                : null
        };
        App.WriteStartupLog($"Creating compiled shell page for initial section '{section}'.");
        sectionFrame.Navigate(typeof(SchoologyShellContentPage), content,
            animationsEnabled ? initialTransition : null);
        App.WriteStartupLog($"Initial Frame.Navigate completed for section '{section}'.");
        _sectionFrame = sectionFrame;
        nav.Content = sectionFrame;
        var profileName = _session is null ? "Profile" :
            ProfileValue(_session.User, "name_display", "display_name", "name") ?? "My Profile";
        IconElement profileIcon = new SymbolIcon(Symbol.Contact);
        var profilePicture = _session is null ? null : ProfileValue(_session.User,
            "picture_url", "pictureUrl", "picture", "photo_url", "avatar_url");
        if (!string.IsNullOrWhiteSpace(profilePicture))
            try { profileIcon = new ImageIcon { Source = new BitmapImage(new Uri(NormalizeSchoologyImageUrl(profilePicture))) }; } catch { }
        var profileLabel = Text(profileName, 13, true);
        profileLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        profileIcon.Width = 16;
        profileIcon.Height = 16;
        profileLabel.VerticalAlignment = VerticalAlignment.Center;
        profileLabel.MaxHeight = 20;
        var profileItem = new NavigationViewItem
        {
            Content = profileLabel,
            Icon = profileIcon,
            Tag = "Profile",
            Height = 44,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(profileItem, profileName + " profile");
        nav.MenuItems.Add(profileItem);
        if (section == "Profile" && _activeProfileUserId == (_session?.UserId ?? 0))
            nav.SelectedItem = profileItem;
        nav.MenuItems.Add(new NavigationViewItemSeparator());

        var items = new (string Name, Symbol Icon)[]
        {
            ("Messages", Symbol.Mail), ("Notifications", Symbol.Mail), ("Requests", Symbol.Message),
            ("Home", Symbol.Home), ("Courses", Symbol.Library), ("Groups", Symbol.People),
            ("Resources", Symbol.Folder), ("Grades", Symbol.Page), ("Upcoming", Symbol.Calendar),
            ("Settings", Symbol.Setting)
        };
        foreach (var entry in items)
        {
            if (entry.Name == "Home")
                nav.MenuItems.Add(new NavigationViewItemSeparator());
            var item = new NavigationViewItem { Content = entry.Name == "Upcoming" ? "Calendar" : entry.Name,
                Tag = entry.Name == "Upcoming" ? "Calendar" : entry.Name,
                Icon = entry.Name == "Notifications"
                    ? new FontIcon { Glyph = "\uF2A3", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 18 }
                    : entry.Name == "Grades"
                        ? new FontIcon { Glyph = "\uE9D5", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 18 }
                        : new SymbolIcon(entry.Icon) };
            // Parent rows only expand/collapse. Selecting a specific child performs navigation.
            item.SelectsOnInvoked = entry.Name is not ("Courses" or "Groups");
            if (entry.Name is "Courses" or "Groups") item.IsExpanded = false;
            nav.MenuItems.Add(item);
            var selectedSection = section == "Profile" ? _profileReturnNavigationTag : section;
            if (entry.Name == (selectedSection == "Recent Activity" ? "Home" : selectedSection) ||
                (entry.Name == "Upcoming" && selectedSection == "Calendar")) nav.SelectedItem = item;
        }
        nav.FooterMenuItems.Add(new NavigationViewItem
        {
            Content = _session is null ? "Profile and settings" : "Sign out",
            Tag = "Sign out",
            Icon = new SymbolIcon(Symbol.Contact)
        });
        nav.SelectionChanged += (_, args) =>
        {
            if (_syncingNavigationSelection || args.SelectedItem is not NavigationViewItem item) return;
            if (item.Tag is string target && target.Contains(':'))
            {
                var parts = target.Split(':', 2);
                if (long.TryParse(parts[1], out var id))
                {
                    var kind = parts[0] == "group" ? "Groups" : "Courses";
                    var entity = (kind == "Groups" ? _groups : _courses).FirstOrDefault(candidate => candidate.Id == id);
                    if (entity is not null) ShowThreePane(kind, entity);
                }
                return;
            }
            if (item.Tag is not string rootTarget) return;
            if (rootTarget == "Sign out") { SchoologyAuthService.SignOut(); _session = null; _postImageCache.Clear(); _activeProfileUserId = 0; _sharedWebViewInitialized = false; _sharedWebViewSessionUserId = 0; _sectionFrame = null; ShowLogin(); }
            else
            {
                if (rootTarget == "Profile") _activeProfileUserId = _session?.UserId ?? 0;
                ShowHome(rootTarget == "Home" ? "Recent Activity" : rootTarget);
            }
        };
        _appNavigation = nav;
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(nav, 1);
        shell.Children.Add(titleBar);
        shell.Children.Add(nav);
        SetTitleBar(titleBar);
        return shell;
    }

    private void HandleTitleBarCreateClick()
    {
        if (_titleBarCreateContext == "assignment-comment")
        {
            _ = ShowAssignmentCommentDialogAsync();
            return;
        }
        if (_titleBarCreateContext == "assignment-actions")
        {
            ShowAssignmentActionsMenu();
            return;
        }
        if (_titleBarCreateContext == "content-comment" && _titleBarCreateAction is not null)
        {
            _ = _titleBarCreateAction();
            return;
        }
        ShowCreateMenu();
    }

    private void SetTitleBarCreateContext(string context, Func<Task>? action = null)
    {
        _titleBarCreateContext = context;
        _titleBarCreateAction = action;
        if (_titleBarCreateButton is null) return;
        _titleBarCreateButton.Visibility = string.IsNullOrEmpty(context) ? Visibility.Collapsed : Visibility.Visible;
        _titleBarCreateButton.Content = context switch
        {
            "assignment-comment" or "content-comment" => "＋ Comment",
            "assignment-actions" => "＋ Create",
            "course-update" or "group-update" => "＋ Post update",
            _ => "＋ Create"
        };
        AutomationProperties.SetName(_titleBarCreateButton, context switch
        {
            "assignment-comment" => "Create assignment comment",
            "content-comment" => "Create comment",
            "assignment-actions" => "Assignment actions",
            "course-update" => "Post a course update",
            "group-update" => "Post a group update",
            "home" => "Create ClassWire content",
            _ => "Create"
        });
    }

    private async Task ShowAssignmentCommentDialogAsync()
    {
        var session = _session;
        if (session is null || _activeAssignmentSectionId <= 0 || _activeAssignmentId <= 0) return;
        var body = new TextBox { Header = "Comment", PlaceholderText = "Post a comment…",
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 110 };
        var dialog = new ContentDialog
        {
            Title = "Create comment", Content = body, PrimaryButtonText = "Post",
            CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary, XamlRoot = _root.XamlRoot, RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(body.Text)) return;
        try
        {
            using var result = await _api.AddAssignmentCommentAsync(session, _activeAssignmentSectionId,
                _activeAssignmentId, body.Text.Trim());
            await ShowAssignmentDetailsAsync(_activeAssignmentSectionId,
                new SchoologyApiRecord("Assignment", string.Empty,
                    JsonDocument.Parse("{\"id\":" + _activeAssignmentId + "}").RootElement.Clone()));
        }
        catch (Exception ex) { await ShowApiErrorAsync("Unable to post assignment comment", ex.Message); }
    }

    private void ShowAssignmentActionsMenu()
    {
        var target = _titleBarCreateButton;
        if (target is null || _session is null || !_activeAssignmentAllowsSubmission) return;

        var menu = new MenuFlyout();
        if (_activeAssignmentAllowsComments)
        {
            var comment = new MenuFlyoutItem { Text = "Create Comment" };
            AutomationProperties.SetAutomationId(comment, "AssignmentCreateCommentMenuItem");
            comment.Click += async (_, _) => await ShowAssignmentCommentDialogAsync();
            menu.Items.Add(comment);
        }

        var upload = new MenuFlyoutItem { Text = "Upload Submission" };
        AutomationProperties.SetAutomationId(upload, "AssignmentUploadSubmissionMenuItem");
        upload.Click += async (_, _) => await ShowAssignmentUploadOptionsDialogAsync();
        menu.Items.Add(upload);

        var text = new MenuFlyoutItem { Text = "Create Text Submission" };
        AutomationProperties.SetAutomationId(text, "AssignmentTextSubmissionMenuItem");
        text.Click += async (_, _) => await ShowTextAssignmentSubmissionDialogAsync();
        menu.Items.Add(text);

        menu.ShowAt(target);
    }

    private async Task ShowAssignmentUploadOptionsDialogAsync()
    {
        var session = _session;
        var sectionId = _activeAssignmentSectionId;
        var assignmentId = _activeAssignmentId;
        if (session is null || !_activeAssignmentAllowsSubmission || sectionId <= 0 || assignmentId <= 0) return;

        var pending = new List<PendingAssignmentSubmissionItem>();
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MaxHeight = 300,
            Background = new SolidColorBrush(Colors.Transparent)
        };
        AutomationProperties.SetAutomationId(list, "AssignmentSubmissionPendingFilesList");

        void AddPendingItem(string name, string kind, IStorageFile? file, long resourceId,
            string resourceField = "schoology_resource_id")
        {
            if (kind == "File" && file is not null &&
                pending.Any(item => item.Kind == "File" &&
                    string.Equals(item.File?.Path, file.Path, StringComparison.OrdinalIgnoreCase)))
                return;
            if (kind == "Resource" && pending.Any(item => item.Kind == "Resource" &&
                item.ResourceId == resourceId && item.ResourceField == resourceField))
                return;

            var status = Text(kind == "File" ? "Ready to upload" : "Ready to submit", 11);
            status.Foreground = ThemeBrush("SchoologyMutedTextBrush");
            var progress = new ProgressBar
            {
                Minimum = 0, Maximum = 100, Value = 0,
                IsIndeterminate = false,
                Visibility = Visibility.Collapsed,
                Height = 4
            };
            var labels = new StackPanel { Spacing = 4 };
            labels.Children.Add(Text(name, 13, true));
            labels.Children.Add(status);
            labels.Children.Add(progress);
            var remove = new Button
            {
                Content = new FontIcon { Glyph = "\uE711", FontSize = 12 },
                Style = (Style)Application.Current.Resources["DefaultButtonStyle"],
                Padding = new Thickness(8),
                MinWidth = 34,
                VerticalAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(remove, $"Remove {name}");
            var rowGrid = new Grid { ColumnSpacing = 10, Padding = new Thickness(8, 6, 8, 6) };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.Children.Add(labels);
            Grid.SetColumn(remove, 1);
            rowGrid.Children.Add(remove);
            var row = new ListViewItem
            {
                Content = rowGrid,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            var item = new PendingAssignmentSubmissionItem
            {
                Name = name, Kind = kind, File = file, ResourceId = resourceId, ResourceField = resourceField,
                Progress = progress, Status = status, Row = row
            };
            remove.Click += (_, _) =>
            {
                pending.Remove(item);
                list.Items.Remove(row);
            };
            pending.Add(item);
            list.Items.Add(row);
        }

        var fileButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { new FontIcon { Glyph = "\uE8A5" }, Text("File", 13, true) }
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(12, 10, 12, 10)
        };
        var resourceButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { new FontIcon { Glyph = "\uE8B7" }, Text("Resources", 13, true) }
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(12, 10, 12, 10)
        };
        AutomationProperties.SetAutomationId(fileButton, "AssignmentSubmissionAddFiles");
        AutomationProperties.SetAutomationId(resourceButton, "AssignmentSubmissionAddResources");
        var buttons = new Grid { ColumnSpacing = 10 };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.Children.Add(fileButton);
        Grid.SetColumn(resourceButton, 1);
        buttons.Children.Add(resourceButton);

        var comment = new TextBox
        {
            Header = "Assignment comment (optional)",
            PlaceholderText = "Add a comment about this submission…",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 76,
            MaxHeight = 140
        };
        AutomationProperties.SetAutomationId(comment, "AssignmentSubmissionComment");
        var content = new StackPanel { Spacing = 12, MinWidth = 380 };
        content.Children.Add(buttons);
        content.Children.Add(Text("Selected files and resources", 12, true));
        content.Children.Add(list);
        content.Children.Add(EmptyState("Add one or more files or ClassWire resources. They will be submitted together as one revision."));
        content.Children.Add(comment);
        content.Children.Add(Text("The optional comment is posted to the assignment's comments, separately from the file revision.", 11));

        string? requestedAction = null;
        ContentDialog? activeDialog = null;
        fileButton.Click += async (_, _) =>
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(AppWindow.Id));
            var files = await picker.PickMultipleFilesAsync();
            foreach (var file in files)
            {
                try
                {
                    var info = new FileInfo(file.Path);
                    if (!info.Exists || info.Length <= 0)
                    {
                        await ShowApiErrorAsync("Unable to add file", $"{file.Name} is empty or no longer available.");
                        continue;
                    }
                    if (info.Length > 60L * 1024 * 1024)
                    {
                        await ShowApiErrorAsync("Unable to add file", $"{file.Name} exceeds the 60 MB file limit.");
                        continue;
                    }
                    AddPendingItem(file.Name, "File", file, 0);
                }
                catch (Exception ex) { await ShowApiErrorAsync("Unable to add file", ex.Message); }
            }
        };
        resourceButton.Click += (_, _) =>
        {
            requestedAction = "resource";
            // Close this dialog before opening the resource picker; WinUI permits
            // only one ContentDialog at a time.
            activeDialog?.Hide();
        };

        while (true)
        {
            activeDialog = new ContentDialog
            {
                Title = "Upload Submission",
                Content = CreateDialogScrollViewport(content),
                PrimaryButtonText = "Submit",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = _root.XamlRoot,
                RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
            };
            var result = await activeDialog.ShowAsync();
            if (requestedAction == "resource")
            {
                requestedAction = null;
                var resources = await ShowAssignmentResourcePickerAsync();
                foreach (var resource in resources)
                    AddPendingItem(resource.Title, "Resource", null, resource.Id,
                        resource.IsImported ? "imported_resource_id" : "schoology_resource_id");
                continue;
            }
            if (result != ContentDialogResult.Primary) return;
            if (pending.Count == 0)
            {
                await ShowApiErrorAsync("No submission attachments", "Add at least one file or ClassWire resource before submitting.");
                continue;
            }

            try
            {
                var files = new List<Dictionary<string, string>>();
                foreach (var item in pending)
                {
                    if (item.Kind == "File")
                    {
                        if (item.File is null) throw new InvalidOperationException($"The file {item.Name} is no longer available.");
                        if (string.IsNullOrWhiteSpace(item.UploadedFileId))
                        {
                            item.Progress.Visibility = Visibility.Visible;
                            item.Progress.IsIndeterminate = true;
                            item.Status.Text = "Uploading…";
                            var bytes = await File.ReadAllBytesAsync(item.File.Path);
                            item.UploadedFileId = await _api.UploadAssignmentAttachmentAsync(
                                session, item.Name, GetSubmissionMimeType(item.Name), bytes);
                        }
                        item.Progress.IsIndeterminate = false;
                        item.Progress.Value = 100;
                        item.Status.Text = "Uploaded";
                        files.Add(new Dictionary<string, string> { ["id"] = item.UploadedFileId! });
                    }
                    else
                    {
                        files.Add(new Dictionary<string, string>
                        {
                            [item.ResourceField] = item.ResourceId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        });
                        item.Progress.Visibility = Visibility.Visible;
                        item.Progress.Value = 100;
                        item.Status.Text = "Ready";
                    }
                }

                using var response = await _api.SubmitAssignmentAttachmentsAsync(
                    session, sectionId, assignmentId, files);
                if (!string.IsNullOrWhiteSpace(comment.Text))
                {
                    try
                    {
                        using var commentResult = await _api.AddAssignmentCommentAsync(
                            session, sectionId, assignmentId, comment.Text.Trim());
                    }
                    catch (Exception commentException)
                    {
                        await ShowApiErrorAsync("Submission uploaded, but comment failed",
                            $"Your files were submitted successfully. The separate assignment comment could not be posted: {commentException.Message}");
                    }
                }
                await ShowAssignmentDetailsAsync(sectionId, new SchoologyApiRecord("Assignment", "",
                    JsonDocument.Parse(JsonSerializer.Serialize(new { id = assignmentId })).RootElement.Clone()));
                return;
            }
            catch (Exception ex)
            {
                App.WriteStartupLog("Assignment attachment submission failed.", ex);
                await ShowApiErrorAsync("Unable to submit assignment", ex.Message);
                // Keep already-uploaded file IDs in the staged list so retrying
                // submits the same files rather than uploading duplicates.
            }
        }
    }

    private async Task<IReadOnlyList<(long Id, string Title, bool IsImported)>> ShowAssignmentResourcePickerAsync()
    {
        var session = _session;
        if (session is null || !_activeAssignmentAllowsSubmission) return [];
        try
        {
            using var collectionsDocument = await _api.GetResourceCollectionsAsync(session);
            var collectionRecords = SchoologyApiClient.ReadRecords(
                collectionsDocument.RootElement, "collection", "collections", "collection_list", "items");
            var mine = new List<ResourceTreeEntry>();
            var shared = new List<ResourceTreeEntry>();
            var groups = new List<ResourceTreeEntry>();
            foreach (var collection in collectionRecords)
            {
                var owner = JsonLong(collection.Data, "collection_owner_id", "collectionOwnerId", "uid", "owner_id");
                var realm = (ProfileValue(collection.Data, "collection_realm", "collectionRealm", "realm") ?? "").ToLowerInvariant();
                var sharedCount = JsonLong(collection.Data, "shared_users_count", "sharedUsersCount", "shared_users");
                var title = ProfileValue(collection.Data, "collection_title", "title", "name") ?? collection.Title;
                var id = JsonLong(collection.Data, "collection_id", "collectionID", "id");
                if (id <= 0) continue;
                var entry = new ResourceTreeEntry(title, "collection", id, id, collection.Data.Clone());
                if (string.IsNullOrWhiteSpace(realm) && owner == session.UserId) mine.Add(entry);
                else if (sharedCount > 0) shared.Add(entry);
                else if (realm == "group") groups.Add(entry);
            }
            var appEntries = new List<ResourceTreeEntry>();
            try
            {
                using var appsDocument = await _api.GetResourceAppsAsync(session);
                appEntries.AddRange(SchoologyApiClient.ReadRecords(
                        appsDocument.RootElement, "resource_apps", "resourceApps", "collection", "collections", "items")
                    .Select(app => new ResourceTreeEntry(
                        ProfileValue(app.Data, "collection_title", "app_title", "appTitle", "title", "name") ?? app.Title,
                        "app", JsonLong(app.Data, "collection_id", "collectionID", "id", "app_id", "appId"),
                        0, app.Data.Clone()))
                    .Where(entry => entry.Id > 0));
            }
            catch (Exception ex) { App.WriteStartupLog("Resource Apps could not be loaded in the assignment picker.", ex); }

            var categories = new List<(string Title, IReadOnlyList<ResourceTreeEntry> Entries)>
            {
                ("My Resources", mine), ("Shared", shared), ("Groups", groups), ("Resource Apps", appEntries)
            };
            var selected = new Dictionary<(long Id, bool IsImported), string>();
            while (true)
            {
                ResourceTreeEntry? requestedApp = null;
                ContentDialog? pickerDialog = null;
                var selectedIds = selected.Keys.Where(key => !key.IsImported).Select(key => key.Id).ToHashSet();
                var selectedSummary = Text($"{selected.Count} selected", 12);
                selectedSummary.Foreground = ThemeBrush("SchoologyMutedTextBrush");
                var tree = BuildResourceEntriesView(categories, "Resources", 0, () => { },
                    entry =>
                    {
                        if (entry.Kind == "app")
                        {
                            requestedApp = entry;
                            pickerDialog?.Hide();
                            return;
                        }
                        if (entry.Id <= 0) return;
                        var key = (entry.Id, false);
                        if (selected.Remove(key))
                        {
                            selectedIds.Remove(entry.Id);
                        }
                        else
                        {
                            selected[key] = entry.Title;
                            selectedIds.Add(entry.Id);
                        }
                        selectedSummary.Text = $"{selected.Count} selected";
                    }, selectedIds);
                var content = new StackPanel { Spacing = 8, MinWidth = 440 };
                content.Children.Add(selectedSummary);
                content.Children.Add(new TextBlock
                {
                    Text = "Choose resources from the same categories and folders shown on the Resources page. Select individual items, or open a Resource App to import its files and links.",
                    TextWrapping = TextWrapping.Wrap,
                    Style = (Style)Application.Current.Resources["BodyTextBlockStyle"]
                });
                content.Children.Add(new ScrollViewer
                {
                    Content = tree, MaxHeight = 500,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                });
                pickerDialog = new ContentDialog
                {
                    Title = "Attach resources",
                    Content = CreateDialogScrollViewport(content, 650),
                    PrimaryButtonText = "Add selected",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = _root.XamlRoot,
                    RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
                };
                var result = await pickerDialog.ShowAsync();
                if (requestedApp is not null)
                {
                    await ImportResourceAppForAssignmentAsync(requestedApp, selected);
                    continue;
                }
                if (result != ContentDialogResult.Primary) return [];
                return selected.Select(item => (item.Key.Id, item.Value, item.Key.IsImported)).ToArray();
            }
        }
        catch (Exception ex)
        {
            App.WriteStartupLog("Assignment resource selection failed.", ex);
            await ShowApiErrorAsync("Unable to load resources", ex.Message);
            return [];
        }
    }

    private async Task ImportResourceAppForAssignmentAsync(
        ResourceTreeEntry app, Dictionary<(long Id, bool IsImported), string> selected)
    {
        var session = _session ?? throw new InvalidOperationException("Sign in to import a resource app.");
        if (app.Id <= 0) throw new InvalidOperationException("This resource app is missing its ID.");
        using var launch = await _api.LaunchResourceAppAsync(session, app.Id);
        var launchUrl = ProfileValue(launch.RootElement, "url", "launch_token_url", "launchTokenUrl");
        if (string.IsNullOrWhiteSpace(launchUrl) &&
            launch.RootElement.TryGetProperty("data", out var data))
            launchUrl = ProfileValue(data, "url", "launchTokenUrl", "launch_token_url");
        if (string.IsNullOrWhiteSpace(launchUrl))
            throw new InvalidOperationException("ClassWire did not return a resource-app launch URL.");
        // Match the working SwiftUI implementation: navigate to Schoology's
        // launch URL unchanged. The launch token may not expose a RelayState
        // query parameter; the import callback itself receives mobile=1 when
        // GetResourceAppImportsAsync fetches /content_app/import.
        var importUrl = launchUrl;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? importError = null;
        var importedCount = 0;
        void ReturnToPicker()
        {
            if (_sectionFrame?.CanGoBack == true) _sectionFrame.GoBack();
            else ShowHome("Assignments");
        }
        async Task<bool> HandleImportCallbackAsync(Uri destination)
        {
            if (!destination.AbsolutePath.Contains("/content_app/import", StringComparison.OrdinalIgnoreCase))
                return false;
            try
            {
                using var imports = await _api.GetResourceAppImportsAsync(session, destination.AbsoluteUri);
                var importRecords = SchoologyApiClient.ReadRecords(imports.RootElement, "imports", "import", "items");
                foreach (var importRecord in importRecords)
                {
                    if (!importRecord.Data.TryGetProperty("attachments", out var attachments) &&
                        !importRecord.Data.TryGetProperty("attachment", out attachments)) continue;
                    foreach (var file in SchoologyApiClient.ReadRecords(
                        attachments, "files", "file", "file_list", "fileList", "items"))
                    {
                        var id = JsonLong(file.Data, "id", "file_id", "fileID");
                        if (id <= 0) continue;
                        var title = ProfileValue(file.Data, "title", "file_title", "fileTitle", "filename", "file_name") ?? file.Title;
                        selected[(id, true)] = string.IsNullOrWhiteSpace(title) ? "Imported file" : title;
                        importedCount++;
                    }
                    foreach (var link in SchoologyApiClient.ReadRecords(
                        attachments, "links", "link", "link_list", "linkList", "items"))
                    {
                        var id = JsonLong(link.Data, "id", "link_id", "linkID");
                        if (id <= 0) continue;
                        var title = ProfileValue(link.Data, "title", "link_title", "linkTitle", "url") ?? link.Title;
                        selected[(id, true)] = string.IsNullOrWhiteSpace(title) ? "Imported link" : title;
                        importedCount++;
                    }
                }
                if (importedCount == 0)
                    importError = "The Resource App completed without returning attachable files or links.";
            }
            catch (Exception ex)
            {
                App.WriteStartupLog("Resource App import callback failed.", ex);
                importError = ex.Message;
            }
            ReturnToPicker();
            completion.TrySetResult(true);
            return true;
        }
        await OpenSharedWebViewAsync(importUrl, app.Title, allowLtiExternal: true,
            onBack: () =>
            {
                ReturnToPicker();
                completion.TrySetResult(false);
            }, navigationHandler: HandleImportCallbackAsync);
        var completed = await completion.Task.WaitAsync(TimeSpan.FromMinutes(15));
        if (completed && importError is not null)
            await ShowApiErrorAsync("Unable to import resources", importError);
    }

    private async Task ShowAssignmentAttachmentsDialogAsync(
        IReadOnlyList<(JsonElement Item, string Kind)> attachments, long sectionId)
    {
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MaxHeight = 480
        };
        AutomationProperties.SetAutomationId(list, "AssignmentAttachmentsList");
        JsonElement selectedAttachment = default;
        string? selectedAttachmentKind = null;
        ContentDialog dialog = null!;
        foreach (var attachment in attachments)
        {
            var name = ProfileValue(attachment.Item, "filename", "fileName", "file_title", "fileTitle",
                "title", "name", "link_title", "linkTitle") ?? "Attachment";
            var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(8, 7, 8, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new FontIcon { Glyph = AttachmentGlyph(attachment.Item), FontSize = 18,
                Foreground = ThemeBrush("SchoologyMutedTextBrush"), VerticalAlignment = VerticalAlignment.Center });
            var label = Text(name, 13, true);
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            var item = new ListViewItem { Content = row, Tag = attachment.Item,
                HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(item, $"Open attachment {name}");
            item.Tapped += (_, _) =>
            {
                selectedAttachment = attachment.Item.Clone();
                selectedAttachmentKind = attachment.Kind;
                dialog.Hide();
            };
            list.Items.Add(item);
        }
        dialog = new ContentDialog
        {
            Title = "Assignment attachments",
            Content = list,
            CloseButtonText = "Close",
            XamlRoot = _root.XamlRoot,
            RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
        };
        await dialog.ShowAsync();
        if (selectedAttachment.ValueKind != JsonValueKind.Undefined && selectedAttachmentKind is not null)
        {
            try
            {
                await OpenAttachmentAsync(selectedAttachment, selectedAttachmentKind,
                    () => ReturnFromAssignment(sectionId));
            }
            catch (Exception ex) { await ShowApiErrorAsync("Unable to open assignment attachment", ex.Message); }
        }
    }

    private async Task PickAndSubmitAssignmentFileAsync()
    {
        var session = _session;
        var sectionId = _activeAssignmentSectionId;
        var assignmentId = _activeAssignmentId;
        if (session is null || !_activeAssignmentAllowsSubmission || sectionId <= 0 || assignmentId <= 0) return;
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(AppWindow.Id));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        var fileInfo = new FileInfo(file.Path);
        if (!fileInfo.Exists || fileInfo.Length <= 0)
        {
            await ShowApiErrorAsync("Unable to upload submission", "The selected file is empty or no longer available.");
            return;
        }
        if (fileInfo.Length > 60L * 1024 * 1024)
        {
            await ShowApiErrorAsync("Unable to upload submission", "ClassWire attachments are limited to 60 MB in this app.");
            return;
        }
        var status = Text("Preparing upload…", 13);
        var progress = new ProgressBar { IsIndeterminate = true, Minimum = 0, Maximum = 100 };
        var content = new StackPanel { Spacing = 10, MinWidth = 340 };
        content.Children.Add(Text(file.Name, 13, true));
        content.Children.Add(status);
        content.Children.Add(progress);
        var dialog = new ContentDialog { Title = "Uploading submission", Content = content, XamlRoot = _root.XamlRoot, RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme };
        var operation = dialog.ShowAsync();
        try
        {
            var bytes = await File.ReadAllBytesAsync(file.Path);
            var mime = GetSubmissionMimeType(file.Name);
            using var result = await _api.SubmitAssignmentFileAsync(session, sectionId, assignmentId, file.Name, mime, bytes);
            dialog.Hide();
            await operation;
            await ShowAssignmentDetailsAsync(sectionId, new SchoologyApiRecord("Assignment", "",
                JsonDocument.Parse("{\"id\":" + assignmentId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}").RootElement.Clone()));
        }
        catch (Exception ex)
        {
            try { dialog.Hide(); } catch { }
            try { await operation; } catch { }
            App.WriteStartupLog("Assignment file submission failed.", ex);
            await ShowApiErrorAsync("Unable to submit file", ex.Message);
        }
    }

    private async Task ShowTextAssignmentSubmissionDialogAsync()
    {
        var session = _session;
        var sectionId = _activeAssignmentSectionId;
        var gradeItemId = _activeAssignmentGradeItemId;
        if (session is null || !_activeAssignmentAllowsSubmission || sectionId <= 0 || gradeItemId <= 0) return;
        var editor = new RichEditBox
        {
            MinHeight = 180,
            Height = 220,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsTabStop = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            IsSpellCheckEnabled = true
        };
        AutomationProperties.SetName(editor, "Text submission editor");
        AutomationProperties.SetAutomationId(editor, "TextSubmissionEditor");

        var toolbarScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
            ZoomMode = ZoomMode.Disabled,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 }
        };
        var toolbar = (StackPanel)toolbarScroll.Content;
        void ApplyInlineFormat(Action<Microsoft.UI.Text.ITextCharacterFormat> apply)
        {
            var selection = editor.Document.Selection;
            var format = selection.CharacterFormat;
            apply(format);
            selection.CharacterFormat = format;
            editor.Focus(FocusState.Programmatic);
        }
        Button AddFormattingButton(string name, string glyph, Action apply)
        {
            var button = new Button
            {
                Width = 40,
                Height = 40,
                Padding = new Thickness(8),
                Content = new FontIcon
                {
                    Glyph = glyph,
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    FontSize = 16
                }
            };
            AutomationProperties.SetName(button, name);
            AutomationProperties.SetAutomationId(button, "TextSubmissionFormat" + name.Replace(" ", string.Empty));
            ToolTipService.SetToolTip(button, name);
            button.Click += (_, _) => apply();
            toolbar.Children.Add(button);
            return button;
        }

        AddFormattingButton("Bold", "\uE8DD", () => ApplyInlineFormat(format =>
            format.Bold = format.Bold == Microsoft.UI.Text.FormatEffect.On
                ? Microsoft.UI.Text.FormatEffect.Off : Microsoft.UI.Text.FormatEffect.On));
        AddFormattingButton("Italic", "\uE8DB", () => ApplyInlineFormat(format =>
            format.Italic = format.Italic == Microsoft.UI.Text.FormatEffect.On
                ? Microsoft.UI.Text.FormatEffect.Off : Microsoft.UI.Text.FormatEffect.On));
        AddFormattingButton("Underline", "\uE8DC", () => ApplyInlineFormat(format =>
            format.Underline = format.Underline == Microsoft.UI.Text.UnderlineType.None
                ? Microsoft.UI.Text.UnderlineType.Single : Microsoft.UI.Text.UnderlineType.None));
        AddFormattingButton("Bulleted list", "\uE8FD", () =>
        {
            var paragraph = editor.Document.Selection.ParagraphFormat;
            paragraph.ListType = paragraph.ListType == Microsoft.UI.Text.MarkerType.Bullet
                ? Microsoft.UI.Text.MarkerType.None : Microsoft.UI.Text.MarkerType.Bullet;
            editor.Document.Selection.ParagraphFormat = paragraph;
            editor.Focus(FocusState.Programmatic);
        });
        AddFormattingButton("Numbered list", "\uE8F7", () =>
        {
            var paragraph = editor.Document.Selection.ParagraphFormat;
            paragraph.ListType = paragraph.ListType == Microsoft.UI.Text.MarkerType.Arabic
                ? Microsoft.UI.Text.MarkerType.None : Microsoft.UI.Text.MarkerType.Arabic;
            editor.Document.Selection.ParagraphFormat = paragraph;
            editor.Focus(FocusState.Programmatic);
        });
        AddFormattingButton("Clear formatting", "\uE75C", () =>
        {
            ApplyInlineFormat(format =>
            {
                format.Bold = Microsoft.UI.Text.FormatEffect.Off;
                format.Italic = Microsoft.UI.Text.FormatEffect.Off;
                format.Underline = Microsoft.UI.Text.UnderlineType.None;
            });
            editor.Document.Selection.Link = string.Empty;
            var paragraph = editor.Document.Selection.ParagraphFormat;
            paragraph.ListType = Microsoft.UI.Text.MarkerType.None;
            editor.Document.Selection.ParagraphFormat = paragraph;
        });

        var linkPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            Visibility = Visibility.Collapsed
        };
        var linkAddress = new TextBox
        {
            Header = "Link address",
            PlaceholderText = "https://example.com",
            MinWidth = 220,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var linkText = new TextBox
        {
            Header = "Link text (optional)",
            PlaceholderText = "Uses selected text, or the address",
            MinWidth = 220,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetAutomationId(linkText, "TextSubmissionLinkText");
        AutomationProperties.SetAutomationId(linkAddress, "TextSubmissionLinkAddress");
        var linkStatus = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        var insertLinkButton = new Button { Content = "Insert link", HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(insertLinkButton, "Insert hyperlink");
        AutomationProperties.SetAutomationId(insertLinkButton, "TextSubmissionInsertLink");
        insertLinkButton.Click += (_, _) =>
        {
            if (!Uri.TryCreate(linkAddress.Text.Trim(), UriKind.Absolute, out var linkUri) ||
                linkUri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(linkUri.UserInfo))
            {
                linkStatus.Text = "Enter a valid http or https address.";
                return;
            }

            try
            {
                var selection = editor.Document.Selection;
                if (selection.Length == 0)
                {
                    var inserted = string.IsNullOrWhiteSpace(linkText.Text) ? linkUri.AbsoluteUri : linkText.Text.Trim();
                    var start = selection.StartPosition;
                    selection.Text = inserted;
                    var insertedLink = editor.Document.GetRange(start, start + inserted.Length);
                    insertedLink.Link = linkUri.AbsoluteUri;
                    var format = insertedLink.CharacterFormat;
                    format.Underline = Microsoft.UI.Text.UnderlineType.Single;
                    insertedLink.CharacterFormat = format;
                }
                else
                {
                    selection.Link = linkUri.AbsoluteUri;
                    var format = selection.CharacterFormat;
                    format.Underline = Microsoft.UI.Text.UnderlineType.Single;
                    selection.CharacterFormat = format;
                }
                linkAddress.Text = string.Empty;
                linkText.Text = string.Empty;
                linkStatus.Text = string.Empty;
                linkPanel.Visibility = Visibility.Collapsed;
                editor.Focus(FocusState.Programmatic);
            }
            catch (Exception ex)
            {
                linkStatus.Text = "Unable to insert the link. Try again.";
                App.WriteStartupLog("Could not insert a text-submission hyperlink.", ex);
            }
        };
        var cancelLinkButton = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(cancelLinkButton, "Cancel hyperlink");
        AutomationProperties.SetAutomationId(cancelLinkButton, "TextSubmissionCancelLink");
        cancelLinkButton.Click += (_, _) =>
        {
            linkAddress.Text = string.Empty;
            linkText.Text = string.Empty;
            linkStatus.Text = string.Empty;
            linkPanel.Visibility = Visibility.Collapsed;
        };
        linkPanel.Children.Add(linkText);
        linkPanel.Children.Add(linkAddress);
        linkPanel.Children.Add(insertLinkButton);
        linkPanel.Children.Add(cancelLinkButton);
        var linkButton = new Button
        {
            Width = 40,
            Height = 40,
            Padding = new Thickness(8),
            Content = new FontIcon
            {
                Glyph = "\uE71B",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 16
            }
        };
        AutomationProperties.SetName(linkButton, "Insert hyperlink");
        AutomationProperties.SetAutomationId(linkButton, "TextSubmissionLinkToolbarButton");
        ToolTipService.SetToolTip(linkButton, "Insert hyperlink");
        linkButton.Click += (_, _) =>
        {
            linkPanel.Visibility = linkPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (linkPanel.Visibility == Visibility.Visible) linkAddress.Focus(FocusState.Programmatic);
        };
        toolbar.Children.Insert(3, linkButton);

        var content = new StackPanel { Spacing = 8, MinWidth = 360 };
        content.Children.Add(Text("Submission text", 13, true));
        content.Children.Add(toolbarScroll);
        content.Children.Add(linkPanel);
        content.Children.Add(linkStatus);
        content.Children.Add(editor);

        var dialog = new ContentDialog
        {
            Title = "Create text submission",
            Content = CreateDialogScrollViewport(content),
            PrimaryButtonText = "Submit",
            SecondaryButtonText = "Save draft",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _root.XamlRoot,
            RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
        };
        dialog.Opened += (_, _) => editor.Focus(FocusState.Programmatic);
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None) return;
        var body = SerializeTextSubmission(editor, out var plainText);
        if (string.IsNullOrWhiteSpace(plainText))
        {
            await ShowApiErrorAsync("Unable to submit text", "Enter a submission before posting.");
            return;
        }
        try
        {
            using var response = await _api.SubmitAssignmentTextAsync(session, sectionId, gradeItemId,
                body, draft: result == ContentDialogResult.Secondary);
            await ShowAssignmentDetailsAsync(sectionId, new SchoologyApiRecord("Assignment", "",
                JsonDocument.Parse("{\"id\":" + _activeAssignmentId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}").RootElement.Clone()));
        }
        catch (Exception ex) { await ShowApiErrorAsync("Unable to submit text", ex.Message); }
    }

    private (StackPanel Container, RichEditBox Editor) CreateRichTextComposer(
        string label, string placeholder, string automationPrefix, double minHeight, double maxHeight)
    {
        var container = new StackPanel { Spacing = 6 };
        container.Children.Add(Text(label, 13, true));
        var editor = new RichEditBox
        {
            PlaceholderText = placeholder,
            MinHeight = minHeight,
            MaxHeight = maxHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsTabStop = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            IsSpellCheckEnabled = true,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        AutomationProperties.SetName(editor, label);
        AutomationProperties.SetAutomationId(editor, automationPrefix + "Editor");
        var toolbarScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
            ZoomMode = ZoomMode.Disabled,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 }
        };
        var toolbar = (StackPanel)toolbarScroll.Content;
        void ApplyInlineFormat(Action<Microsoft.UI.Text.ITextCharacterFormat> apply)
        {
            var selection = editor.Document.Selection;
            var format = selection.CharacterFormat;
            apply(format);
            selection.CharacterFormat = format;
            editor.Focus(FocusState.Programmatic);
        }
        void AddFormatButton(string name, string glyph, Action action)
        {
            var button = new Button
            {
                Width = 40, Height = 40, Padding = new Thickness(8),
                Content = new FontIcon { Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16 }
            };
            AutomationProperties.SetName(button, name);
            AutomationProperties.SetAutomationId(button, automationPrefix + "Format" +
                Regex.Replace(name, "[^A-Za-z0-9]", string.Empty));
            ToolTipService.SetToolTip(button, name);
            button.Click += (_, _) => action();
            toolbar.Children.Add(button);
        }
        AddFormatButton("Bold", "\uE8DD", () => ApplyInlineFormat(format =>
            format.Bold = format.Bold == Microsoft.UI.Text.FormatEffect.On ? Microsoft.UI.Text.FormatEffect.Off : Microsoft.UI.Text.FormatEffect.On));
        AddFormatButton("Italic", "\uE8DB", () => ApplyInlineFormat(format =>
            format.Italic = format.Italic == Microsoft.UI.Text.FormatEffect.On ? Microsoft.UI.Text.FormatEffect.Off : Microsoft.UI.Text.FormatEffect.On));
        AddFormatButton("Underline", "\uE8DC", () => ApplyInlineFormat(format =>
            format.Underline = format.Underline == Microsoft.UI.Text.UnderlineType.None ? Microsoft.UI.Text.UnderlineType.Single : Microsoft.UI.Text.UnderlineType.None));
        AddFormatButton("Bulleted list", "\uE8FD", () =>
        {
            var paragraph = editor.Document.Selection.ParagraphFormat;
            paragraph.ListType = paragraph.ListType == Microsoft.UI.Text.MarkerType.Bullet ? Microsoft.UI.Text.MarkerType.None : Microsoft.UI.Text.MarkerType.Bullet;
            editor.Document.Selection.ParagraphFormat = paragraph;
            editor.Focus(FocusState.Programmatic);
        });
        AddFormatButton("Numbered list", "\uE8F7", () =>
        {
            var paragraph = editor.Document.Selection.ParagraphFormat;
            paragraph.ListType = paragraph.ListType == Microsoft.UI.Text.MarkerType.Arabic ? Microsoft.UI.Text.MarkerType.None : Microsoft.UI.Text.MarkerType.Arabic;
            editor.Document.Selection.ParagraphFormat = paragraph;
            editor.Focus(FocusState.Programmatic);
        });
        AddFormatButton("Clear formatting", "\uE75C", () =>
        {
            ApplyInlineFormat(format =>
            {
                format.Bold = Microsoft.UI.Text.FormatEffect.Off;
                format.Italic = Microsoft.UI.Text.FormatEffect.Off;
                format.Underline = Microsoft.UI.Text.UnderlineType.None;
            });
            editor.Document.Selection.Link = string.Empty;
            var paragraph = editor.Document.Selection.ParagraphFormat;
            paragraph.ListType = Microsoft.UI.Text.MarkerType.None;
            editor.Document.Selection.ParagraphFormat = paragraph;
        });
        AddFormatButton("Insert hyperlink", "\uE71B", () => _ = InsertComposerLinkAsync());
        async Task InsertComposerLinkAsync()
        {
            var address = new TextBox { Header = "Link address", PlaceholderText = "https://example.com" };
            var linkText = new TextBox { Header = "Link text (optional)", PlaceholderText = "Uses selected text, or the address" };
            var linkContent = new StackPanel { Spacing = 8, Children = { address, linkText } };
            var linkDialog = new ContentDialog
            {
                Title = "Insert hyperlink", Content = linkContent, PrimaryButtonText = "Insert", CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary, XamlRoot = _root.XamlRoot,
                RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
            };
            if (await linkDialog.ShowAsync() != ContentDialogResult.Primary) return;
            if (!Uri.TryCreate(address.Text.Trim(), UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            {
                await ShowApiErrorAsync("Unable to insert hyperlink", "Enter a valid http or https address.");
                return;
            }
            var selection = editor.Document.Selection;
            if (selection.Length > 0) selection.Link = uri.AbsoluteUri;
            else
            {
                var insertedText = string.IsNullOrWhiteSpace(linkText.Text) ? uri.AbsoluteUri : linkText.Text.Trim();
                var start = selection.StartPosition;
                selection.Text = insertedText;
                editor.Document.GetRange(start, start + insertedText.Length).Link = uri.AbsoluteUri;
            }
            editor.Focus(FocusState.Programmatic);
        }
        container.Children.Add(toolbarScroll);
        container.Children.Add(editor);
        return (container, editor);
    }

    private static string SerializeTextSubmission(RichEditBox editor, out string plainText)
    {
        var storyLength = editor.Document.GetRange(0, 0).StoryLength;
        plainText = editor.Document.GetRange(0, storyLength).Text;
        var text = plainText.Replace("\r\n", "\r", StringComparison.Ordinal).TrimEnd('\r');
        if (text.Length == 0) return string.Empty;
        var html = new StringBuilder();
        var position = 0;
        var activeList = Microsoft.UI.Text.MarkerType.None;
        foreach (var paragraph in text.Split('\r'))
        {
            var paragraphRange = editor.Document.GetRange(position, position + paragraph.Length);
            var listType = paragraphRange.ParagraphFormat.ListType;
            if (listType is not (Microsoft.UI.Text.MarkerType.Bullet or Microsoft.UI.Text.MarkerType.Arabic))
                listType = Microsoft.UI.Text.MarkerType.None;
            if (activeList != listType)
            {
                if (activeList != Microsoft.UI.Text.MarkerType.None)
                    html.Append(activeList == Microsoft.UI.Text.MarkerType.Bullet ? "</ul>" : "</ol>");
                if (listType != Microsoft.UI.Text.MarkerType.None)
                    html.Append(listType == Microsoft.UI.Text.MarkerType.Bullet ? "<ul>" : "<ol>");
                activeList = listType;
            }
            html.Append(listType == Microsoft.UI.Text.MarkerType.None ? "<p>" : "<li>");
            string? runSignature = null;
            var runText = new StringBuilder();
            void FlushRun()
            {
                if (runText.Length == 0 || runSignature is null) return;
                var parts = runSignature.Split('\u001f');
                var link = parts[3];
                if (link.Length > 0) html.Append("<a href=\"").Append(System.Net.WebUtility.HtmlEncode(link)).Append("\">");
                if (parts[0] == "1") html.Append("<strong>");
                if (parts[1] == "1") html.Append("<em>");
                if (parts[2] == "1") html.Append("<u>");
                html.Append(System.Net.WebUtility.HtmlEncode(runText.ToString()));
                if (parts[2] == "1") html.Append("</u>");
                if (parts[1] == "1") html.Append("</em>");
                if (parts[0] == "1") html.Append("</strong>");
                if (link.Length > 0) html.Append("</a>");
                runText.Clear();
            }
            for (var index = 0; index < paragraph.Length; index++)
            {
                var range = editor.Document.GetRange(position + index, position + index + 1);
                var format = range.CharacterFormat;
                var link = range.Link ?? string.Empty;
                if (!Uri.TryCreate(link, UriKind.Absolute, out var linkUri) ||
                    linkUri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(linkUri.UserInfo)) link = string.Empty;
                var signature = string.Join('\u001f',
                    format.Bold == Microsoft.UI.Text.FormatEffect.On ? "1" : "0",
                    format.Italic == Microsoft.UI.Text.FormatEffect.On ? "1" : "0",
                    format.Underline != Microsoft.UI.Text.UnderlineType.None ? "1" : "0", link);
                if (runSignature is not null && signature != runSignature) FlushRun();
                runSignature = signature;
                runText.Append(paragraph[index]);
            }
            FlushRun();
            html.Append(listType == Microsoft.UI.Text.MarkerType.None ? "</p>" : "</li>");
            position += paragraph.Length + 1;
        }
        if (activeList != Microsoft.UI.Text.MarkerType.None)
            html.Append(activeList == Microsoft.UI.Text.MarkerType.Bullet ? "</ul>" : "</ol>");
        return html.ToString();
    }

    private static string GetSubmissionMimeType(string filename) => Path.GetExtension(filename).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".ppt" => "application/vnd.ms-powerpoint",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".txt" => "text/plain",
        ".csv" => "text/csv",
        ".zip" => "application/zip",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        _ => "application/octet-stream"
    };

    private sealed class PostAttachmentState
    {
        internal List<(string Id, string Name)> Files { get; } = [];
        internal List<(long Id, string Name)> Resources { get; } = [];
        internal string[] FileIds => Files.Select(item => item.Id).ToArray();
        internal long[] ResourceIds => Resources.Select(item => item.Id).ToArray();
    }

    private PostAttachmentState AddPostAttachmentControls(
        StackPanel content, SchoologyAuthService.Session session, string automationPrefix)
    {
        var attachments = new PostAttachmentState();
        var actions = new Grid { ColumnSpacing = 10 };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var attachFile = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { new SymbolIcon(Symbol.Page), Text("File", 13) }
            },
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var attachResource = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { new SymbolIcon(Symbol.Folder), Text("Resource", 13) }
            },
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetAutomationId(attachFile, automationPrefix + "AttachFileButton");
        AutomationProperties.SetAutomationId(attachResource, automationPrefix + "AttachResourceButton");
        Grid.SetColumn(attachResource, 1);
        actions.Children.Add(attachFile);
        actions.Children.Add(attachResource);
        content.Children.Add(actions);

        var attachmentList = new StackPanel { Spacing = 5 };
        content.Children.Add(attachmentList);
        var status = Text(string.Empty, 12);
        status.Foreground = ThemeBrush("SchoologyMutedTextBrush");
        content.Children.Add(status);

        void RenderAttachments()
        {
            attachmentList.Children.Clear();
            foreach (var attachment in attachments.Files.ToArray())
            {
                var remove = new Button
                {
                    Content = $"File: {attachment.Name}  ×",
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(8, 4, 8, 4)
                };
                AutomationProperties.SetName(remove, $"Remove file attachment {attachment.Name}");
                remove.Click += (_, _) =>
                {
                    attachments.Files.RemoveAll(item => item.Id == attachment.Id);
                    RenderAttachments();
                };
                attachmentList.Children.Add(remove);
            }
            foreach (var attachment in attachments.Resources.ToArray())
            {
                var remove = new Button
                {
                    Content = $"Resource: {attachment.Name}  ×",
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(8, 4, 8, 4)
                };
                AutomationProperties.SetName(remove, $"Remove resource attachment {attachment.Name}");
                remove.Click += (_, _) =>
                {
                    attachments.Resources.RemoveAll(item => item.Id == attachment.Id);
                    RenderAttachments();
                };
                attachmentList.Children.Add(remove);
            }
        }

        attachFile.Click += async (_, _) =>
        {
            try
            {
                var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
                picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(AppWindow.Id));
                var file = await picker.PickSingleFileAsync();
                if (file is null) return;
                var info = new FileInfo(file.Path);
                if (!info.Exists || info.Length <= 0)
                    throw new InvalidOperationException("The selected file is empty or no longer available.");
                if (info.Length > 60L * 1024 * 1024)
                    throw new InvalidOperationException("ClassWire attachments are limited to 60 MB.");
                attachFile.IsEnabled = false;
                attachResource.IsEnabled = false;
                status.Text = $"Uploading {file.Name}…";
                var bytes = await File.ReadAllBytesAsync(file.Path);
                var id = await _api.UploadMessageFileAsync(session, file.Name, GetSubmissionMimeType(file.Name), bytes);
                attachments.Files.Add((id, file.Name));
                RenderAttachments();
                status.Text = "File attached.";
            }
            catch (Exception ex) { status.Text = $"Unable to attach file: {ex.Message}"; }
            finally
            {
                attachFile.IsEnabled = true;
                attachResource.IsEnabled = true;
            }
        };

        var resourceFlyout = new Flyout();
        var resourceList = new ListView
        {
            Width = 380, MaxHeight = 340, SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = false, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetAutomationId(resourceList, automationPrefix + "ResourcePickerList");
        resourceFlyout.Content = resourceList;
        attachResource.Click += async (_, _) =>
        {
            resourceList.Items.Clear();
            resourceList.Items.Add(new ListViewItem { Content = Text("Loading resources…", 12), IsEnabled = false });
            resourceFlyout.ShowAt(attachResource);
            attachResource.IsEnabled = false;
            attachFile.IsEnabled = false;
            try
            {
                var choices = await LoadMessageResourceChoicesAsync(session);
                resourceList.Items.Clear();
                foreach (var choice in choices)
                {
                    var item = new ListViewItem
                    {
                        Content = Text(choice.Title, 13),
                        HorizontalContentAlignment = HorizontalAlignment.Stretch
                    };
                    item.Tapped += (_, _) =>
                    {
                        if (attachments.Resources.All(existing => existing.Id != choice.Id))
                        {
                            attachments.Resources.Add((choice.Id, choice.Title));
                            RenderAttachments();
                            status.Text = "Resource attached.";
                        }
                        resourceFlyout.Hide();
                    };
                    resourceList.Items.Add(item);
                }
                if (choices.Count == 0)
                    resourceList.Items.Add(new ListViewItem { Content = Text("No resources found.", 12), IsEnabled = false });
            }
            catch (Exception ex)
            {
                resourceList.Items.Clear();
                resourceList.Items.Add(new ListViewItem { Content = Text($"Unable to load resources: {ex.Message}", 12), IsEnabled = false });
            }
            finally
            {
                attachResource.IsEnabled = true;
                attachFile.IsEnabled = true;
            }
        };
        return attachments;
    }

    private Task<JsonDocument> CreateUpdateWithAttachmentsAsync(
        SchoologyAuthService.Session session, string kind, SchoologyEntity entity,
        string body, PostAttachmentState attachments)
    {
        if (attachments.Files.Count == 0 && attachments.Resources.Count == 0)
            return _api.CreateUpdateAsync(session, kind, entity.Id, body);
        var realm = kind == "Groups" ? "groups" : "sections";
        var payload = new Dictionary<string, object> { ["body"] = body };
        AddPostAttachmentPayload(payload, attachments);
        return _auth.SendApiJsonBodyAsync(session, HttpMethod.Post, $"{realm}/{entity.Id}/updates", payload);
    }

    private Task<JsonDocument> CreateDiscussionWithAttachmentsAsync(
        SchoologyAuthService.Session session, string kind, SchoologyEntity entity,
        string title, string body, PostAttachmentState attachments)
    {
        if (attachments.Files.Count == 0 && attachments.Resources.Count == 0)
            return _api.CreateDiscussionAsync(session, kind, entity.Id, title, body);
        var payload = new Dictionary<string, object> { ["title"] = title, ["body"] = body, ["published"] = 1 };
        AddPostAttachmentPayload(payload, attachments);
        var realm = kind == "Groups" ? "groups" : "sections";
        return _auth.SendApiJsonBodyAsync(session, HttpMethod.Post, $"{realm}/{entity.Id}/discussions", payload);
    }

    private Task<JsonDocument> CreateEventWithAttachmentsAsync(
        SchoologyAuthService.Session session, string kind, SchoologyEntity entity,
        string title, string description, string start, bool allDay, bool commentsEnabled,
        int rsvp, string? end, PostAttachmentState attachments)
    {
        var payload = new Dictionary<string, object>
        {
            ["title"] = title, ["description"] = description, ["start"] = start,
            ["is_all_day"] = allDay ? 1 : 0, ["rsvp"] = rsvp,
            ["comments_enabled"] = commentsEnabled ? 1 : 0
        };
        if (!string.IsNullOrWhiteSpace(end)) { payload["has_end"] = 1; payload["end"] = end; }
        AddPostAttachmentPayload(payload, attachments);
        var realm = kind switch { "Groups" => "groups", "Profile" => "users", _ => "sections" };
        return _auth.SendApiJsonBodyAsync(session, HttpMethod.Post, $"{realm}/{entity.Id}/events", payload);
    }

    private static void AddPostAttachmentPayload(
        Dictionary<string, object> payload, PostAttachmentState attachments)
    {
        if (attachments.Files.Count > 0)
            payload["file-attachment"] = new Dictionary<string, object> { ["id"] = attachments.FileIds };
        if (attachments.Resources.Count > 0)
            payload["attachments"] = attachments.ResourceIds
                .Distinct().Select(id => new Dictionary<string, object> { ["resource"] = id }).ToArray();
    }

    private async Task ShowCreateDiscussionOrEventDialogAsync(bool isEvent)
    {
        var session = _session;
        if (session is null) return;
        try
        {
            var permissions = await LoadPostPermissionMapAsync(session, includeUserEvents: isEvent);
            var destinations = GetPostableDestinations(session, permissions, isEvent ? "events" : "discussions", includeUserEvents: isEvent);
            if (destinations.Count == 0)
            {
                await ShowApiErrorAsync(isEvent ? "Unable to create event" : "Unable to create discussion",
                    isEvent ? "You do not have permission to create events in any available destination." :
                    "You do not have permission to create discussions in any available course or group.");
                return;
            }
            var content = new StackPanel { Spacing = 10, MinWidth = 360 };
            var destination = new ComboBox { Header = "Post to", HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = destinations.Select(item => item.Entity.Name).ToArray(), SelectedIndex = 0 };
            var title = new TextBox { Header = "Title", PlaceholderText = isEvent ? "Event title" : "Discussion title" };
            var descriptionComposer = CreateRichTextComposer("Description", "Add details…",
                "DiscussionEventDescription", 100, 220);
            var description = descriptionComposer.Editor;
            content.Children.Add(destination); content.Children.Add(title); content.Children.Add(descriptionComposer.Container);
            var attachments = AddPostAttachmentControls(content, session, isEvent ? "EventComposer" : "DiscussionComposer");
            DatePicker? eventDate = null; TimePicker? eventTime = null;
            DatePicker? eventEndDate = null; TimePicker? eventEndTime = null;
            ToggleSwitch? allDay = null; ToggleSwitch? commentsEnabled = null;
            CheckBox? hasEnd = null; ComboBox? rsvp = null;
            if (isEvent)
            {
                allDay = new ToggleSwitch { Header = "All day", IsOn = true };
                commentsEnabled = new ToggleSwitch { Header = "Enable comments", IsOn = true };
                eventDate = new DatePicker { Header = "Start date", Date = DateTimeOffset.Now };
                eventTime = new TimePicker { Header = "Start time", Time = DateTimeOffset.Now.TimeOfDay, IsEnabled = false };
                rsvp = new ComboBox { Header = "RSVP", ItemsSource = new[] { "No RSVP", "Yes", "Maybe" }, SelectedIndex = 0 };
                hasEnd = new CheckBox { Content = "Add end date" };
                eventEndDate = new DatePicker { Header = "End date", Date = DateTimeOffset.Now };
                eventEndTime = new TimePicker { Header = "End time", Time = DateTimeOffset.Now.TimeOfDay, IsEnabled = false };
                var endFields = new StackPanel { Spacing = 6, Visibility = Visibility.Collapsed };
                endFields.Children.Add(eventEndDate); endFields.Children.Add(eventEndTime);
                allDay.Toggled += (_, _) => { eventTime.IsEnabled = !allDay.IsOn; eventEndTime.IsEnabled = !allDay.IsOn; };
                hasEnd.Checked += (_, _) => endFields.Visibility = Visibility.Visible;
                hasEnd.Unchecked += (_, _) => endFields.Visibility = Visibility.Collapsed;
                content.Children.Add(allDay); content.Children.Add(eventDate); content.Children.Add(eventTime);
                content.Children.Add(hasEnd); content.Children.Add(endFields); content.Children.Add(rsvp); content.Children.Add(commentsEnabled);
            }
            var dialog = new ContentDialog { Title = isEvent ? "Create event" : "Create discussion", Content = CreateDialogScrollViewport(content),
                PrimaryButtonText = "Create", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary,
                XamlRoot = _root.XamlRoot, RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            if (string.IsNullOrWhiteSpace(title.Text))
            { await ShowApiErrorAsync("Unable to create content", "Enter a title before creating this item."); return; }
            var selected = destinations[Math.Clamp(destination.SelectedIndex, 0, destinations.Count - 1)];
            string? end = null;
            if (isEvent && hasEnd!.IsChecked == true)
            {
                var startAt = eventDate!.Date.DateTime.Date + (allDay!.IsOn ? TimeSpan.Zero : eventTime!.Time);
                var endAt = eventEndDate!.Date.DateTime.Date + (allDay.IsOn ? new TimeSpan(23, 59, 0) : eventEndTime!.Time);
                if (endAt < startAt)
                { await ShowApiErrorAsync("Unable to create event", "The end date and time cannot be before the start date and time."); return; }
                end = endAt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            }
            var descriptionHtml = SerializeTextSubmission(description, out _);
            using var created = isEvent
                ? await CreateEventWithAttachmentsAsync(session, selected.Kind, selected.Entity, title.Text.Trim(), descriptionHtml,
                    (eventDate!.Date.DateTime.Date + (allDay!.IsOn ? TimeSpan.Zero : eventTime!.Time))
                        .ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                    allDay.IsOn, commentsEnabled!.IsOn, rsvp!.SelectedIndex, end, attachments)
                : await CreateDiscussionWithAttachmentsAsync(session, selected.Kind, selected.Entity, title.Text.Trim(), descriptionHtml, attachments);
            if (selected.Kind == "Profile") ShowHome("Recent Activity");
            else ShowThreePane(selected.Kind, selected.Entity);
        }
        catch (Exception ex) { await ShowApiErrorAsync(isEvent ? "Unable to create event" : "Unable to create discussion", ex.Message); }
    }

    private async Task ShowCreateUpdateDialogCoreAsync()
    {
        var session = _session; if (session is null) return;
        try
        {
            var permissions = await LoadPostPermissionMapAsync(session, includeUserEvents: false);
            var destinations = GetPostableDestinations(session, permissions, "updates");
            if (destinations.Count == 0)
            {
                await ShowApiErrorAsync("Unable to create update", "You do not have permission to post updates in any available course or group.");
                return;
            }
            var content = new StackPanel { Spacing = 10, MinWidth = 360 };
            var picker = new ComboBox { Header = "Post to", ItemsSource = destinations.Select(item => item.Entity.Name).ToArray(),
                SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            var bodyComposer = CreateRichTextComposer("Write an update", "Share an update…",
                "LegacyUpdateComposer", 120, 240);
            var body = bodyComposer.Editor;
            content.Children.Add(picker); content.Children.Add(bodyComposer.Container);
            var attachments = AddPostAttachmentControls(content, session, "LegacyUpdateComposer");
            var dialog = new ContentDialog { Title = "Create update", Content = CreateDialogScrollViewport(content), PrimaryButtonText = "Post", CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary, XamlRoot = _root.XamlRoot, RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            var bodyHtml = SerializeTextSubmission(body, out var bodyPlainText);
            if (string.IsNullOrWhiteSpace(bodyPlainText)) return;
            var selected = destinations[Math.Clamp(picker.SelectedIndex, 0, destinations.Count - 1)];
            using var created = await CreateUpdateWithAttachmentsAsync(session, selected.Kind, selected.Entity, bodyHtml, attachments);
            ShowThreePane(selected.Kind, selected.Entity);
        }
        catch (Exception ex) { await ShowApiErrorAsync("Unable to create update", ex.Message); }
    }

    private void UpdateTitleBarCreateButton(string section, string? tab)
    {
        var context = section switch
        {
            "Recent Activity" or "Home" => "home",
            "Courses" when tab == "Updates" => "course-update",
            "Groups" when tab == "Updates" => "group-update",
            _ => string.Empty
        };
        _titleBarCreateContext = context;
        if (_titleBarCreateButton is null) return;
        _titleBarCreateButton.Visibility = context.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _titleBarCreateButton.Content = context switch
        {
            "course-update" or "group-update" => "＋ Post update",
            _ => "＋ Create"
        };
        AutomationProperties.SetName(_titleBarCreateButton, context switch
        {
            "course-update" => "Post a course update",
            "group-update" => "Post a group update",
            "home" => "Create ClassWire content",
            _ => "Create"
        });
    }

    private void ShowHome(string section = "Recent Activity")
    {
        if (_protocolDispatchReady && !_processingProtocolUrls)
            DispatcherQueue.TryEnqueue(() => _ = ProcessPendingProtocolUrlsAsync());
        _ = StopQrScannerAsync();
        _page = "home";
        _section = section;
        UpdateTitleBarCreateButton(section, null);
        if (_session is not null) EnsureBackgroundNotificationPolling();
        if (section is "Courses" or "Groups" or "School Profile") { ShowThreePane(section); return; }
        UIElement page;
        if (section is "Upcoming" or "Recent Activity" or "Home")
            page = BuildHomeSplitView(section == "Upcoming");
        else if (section == "Calendar")
            page = BuildCalendarPage();
        else if (section == "Settings")
            page = BuildSettingsPage();
        else if (section == "Messages")
            page = BuildMessagesPage();
        else if (section == "Grades")
            page = BuildGradesPage();
        else if (section == "Profile")
        {
            if (_activeProfileUserId <= 0) _activeProfileUserId = _session?.UserId ?? 0;
            page = BuildProfilePage();
        }
        else if (section == "Notifications")
            page = BuildNotificationsPage();
        else if (section == "Requests")
            page = BuildRequestsPage();
        else if (section == "Resources")
            page = BuildResourcesPage();
        else
        {
            var panel = new StackPanel { Spacing = 12, Margin = new Thickness(24) };
            panel.Children.Add(Text(section, 24, true));
            panel.Children.Add(EmptyState($"No {section.ToLowerInvariant()} available."));
            page = panel;
        }
        if (_sectionFrame is not null)
            NavigateMainSection(page, section);
        else
            Reset(BuildApplicationShell(section, page));
    }

    private void NavigateMainSection(UIElement page, string section)
    {
        if (_sectionFrame is null) return;
        var effect = section is "Home" or "Recent Activity" ? SlideNavigationTransitionEffect.FromLeft : SlideNavigationTransitionEffect.FromRight;
        var info = new SlideNavigationTransitionInfo { Effect = effect };
        var animationsEnabled = AnimationsEnabled;
        _sectionFrame.ContentTransitions = animationsEnabled
            ? new TransitionCollection { new NavigationThemeTransition { DefaultNavigationTransitionInfo = info } }
            : null;
        _sectionFrame.Navigate(typeof(SchoologyShellContentPage), page, animationsEnabled ? info : null);
    }

    private async Task LoadAccountCollectionsAsync()
    {
        var session = _session;
        if (session is null) return;
        try
        {
            var results = await Task.WhenAll(_api.GetCoursesAsync(session), _api.GetGroupsAsync(session));
            _courses.Clear();
            foreach (var item in results[0]) _courses.Add(item);
            _groups.Clear();
            foreach (var item in results[1]) _groups.Add(item);
            PopulateNavigationHierarchy();
            App.WriteStartupLog($"Loaded ClassWire account collections: {_courses.Count} courses, {_groups.Count} groups.");
        }
        catch (Exception ex)
        {
            App.WriteStartupLog("Could not load ClassWire account collections.", ex);
        }
    }


    private void PopulateNavigationHierarchy()
    {
        if (_appNavigation is null) return;
        foreach (var parent in _appNavigation.MenuItems.OfType<NavigationViewItem>())
        {
            if (parent.Tag is not string tag || tag is not ("Courses" or "Groups")) continue;
            parent.MenuItems.Clear();
            parent.SelectsOnInvoked = false;
            var entities = tag == "Groups" ? _groups : _courses;
            foreach (var entity in entities)
            {
                parent.MenuItems.Add(new NavigationViewItem
                {
                    Content = entity.Name,
                    Tag = (tag == "Groups" ? "group:" : "course:") + entity.Id,
                    Icon = new SymbolIcon(tag == "Groups" ? Symbol.People : Symbol.Page),
                    SelectsOnInvoked = true
                });
            }
        }
    }

    private Grid BuildHomeSplitView(bool upcomingFirst)
    {
        // Landscape mirrors the classic port's two-pane home proportions.
        // Portrait changes the task model to a single content surface with
        // Upcoming promoted into the SelectorBar instead of an overlay pane.
        var root = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
        var wide = new Grid { ColumnSpacing = 0 };
        wide.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        wide.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var wideLeft = new Grid { Margin = new Thickness(20, 14, 12, 16), RowSpacing = 12 };
        wideLeft.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        wideLeft.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        wideLeft.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        wideLeft.Children.Add(Text("Home", 23, true));
        var wideSelector = new SelectorBar { HorizontalAlignment = HorizontalAlignment.Left };
        var wideRecent = new SelectorBarItem { Text = "Recent Activity" };
        var wideDashboard = new SelectorBarItem { Text = "Course Dashboard" };
        wideSelector.Items.Add(wideRecent);
        wideSelector.Items.Add(wideDashboard);
        Grid.SetRow(wideSelector, 1); wideLeft.Children.Add(wideSelector);
        var wideFrame = new Frame { Background = new SolidColorBrush(Colors.Transparent) };
        Grid.SetRow(wideFrame, 2); wideLeft.Children.Add(wideFrame);
        var wideTabCache = new Dictionary<string, UIElement>(StringComparer.Ordinal);
        UIElement GetWideTab(string tab) => GetOrCreateTemporaryView(wideTabCache, tab,
            () => tab == "Course Dashboard" ? BuildCourseDashboardContent() : BuildRecentActivityContent());
        wideSelector.SelectionChanged += (_, _) =>
        {
            if (wideSelector.SelectedItem is not SelectorBarItem item) return;
            NavigateShellFrame(wideFrame, GetWideTab(item.Text), SlideNavigationTransitionEffect.FromRight);
        };
        wideSelector.SelectedItem = upcomingFirst ? wideDashboard : wideRecent;
        NavigateShellFrame(wideFrame, GetWideTab(upcomingFirst ? "Course Dashboard" : "Recent Activity"),
            SlideNavigationTransitionEffect.FromRight);
        Grid.SetColumn(wideLeft, 0); wide.Children.Add(wideLeft);

        var wideUpcoming = new Grid { Margin = new Thickness(14, 14, 20, 16), RowSpacing = 10 };
        wideUpcoming.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        wideUpcoming.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        wideUpcoming.Children.Add(Text("Upcoming", 23, true));
        var wideUpcomingFrame = new Frame { Background = new SolidColorBrush(Colors.Transparent) };
        Grid.SetRow(wideUpcomingFrame, 1); wideUpcoming.Children.Add(wideUpcomingFrame);
        NavigateShellFrame(wideUpcomingFrame, BuildUpcomingContent(), SlideNavigationTransitionEffect.FromRight);
        Grid.SetColumn(wideUpcoming, 1); wide.Children.Add(wideUpcoming);

        var narrow = new Grid { Margin = new Thickness(16, 12, 16, 16), RowSpacing = 10, Visibility = Visibility.Collapsed };
        narrow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        narrow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        narrow.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        narrow.Children.Add(Text("Home", 23, true));
        var narrowSelector = new SelectorBar { HorizontalAlignment = HorizontalAlignment.Stretch };
        var narrowRecent = new SelectorBarItem { Text = "Recent Activity" };
        var narrowDashboard = new SelectorBarItem { Text = "Course Dashboard" };
        var narrowUpcoming = new SelectorBarItem { Text = "Upcoming" };
        narrowSelector.Items.Add(narrowRecent);
        narrowSelector.Items.Add(narrowDashboard);
        narrowSelector.Items.Add(narrowUpcoming);
        Grid.SetRow(narrowSelector, 1); narrow.Children.Add(narrowSelector);
        var narrowFrame = new Frame { Background = new SolidColorBrush(Colors.Transparent) };
        Grid.SetRow(narrowFrame, 2); narrow.Children.Add(narrowFrame);
        var narrowTabCache = new Dictionary<string, UIElement>(StringComparer.Ordinal);
        UIElement GetNarrowTab(string tab) => GetOrCreateTemporaryView(narrowTabCache, tab, () => tab switch
        {
            "Course Dashboard" => BuildCourseDashboardContent(),
            "Upcoming" => BuildUpcomingContent(),
            _ => BuildRecentActivityContent()
        });
        void ShowNarrowTab(SelectorBarItem item) =>
            NavigateShellFrame(narrowFrame, GetNarrowTab(item.Text), SlideNavigationTransitionEffect.FromRight);
        narrowSelector.SelectionChanged += (_, _) =>
        {
            if (narrowSelector.SelectedItem is SelectorBarItem item) ShowNarrowTab(item);
        };
        narrowSelector.SelectedItem = upcomingFirst ? narrowUpcoming : narrowRecent;
        ShowNarrowTab(upcomingFirst ? narrowUpcoming : narrowRecent);

        root.Children.Add(wide);
        root.Children.Add(narrow);
        root.SizeChanged += (_, args) =>
        {
            var useNarrow = args.NewSize.Width < 800;
            wide.Visibility = useNarrow ? Visibility.Collapsed : Visibility.Visible;
            narrow.Visibility = useNarrow ? Visibility.Visible : Visibility.Collapsed;
        };
        return root;
    }

    private ScrollViewer BuildRecentActivityContent()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(4, 2, 10, 8) };
        var statusHost = LoadingStatus("Loading recent ClassWire activity…", out var status);
        panel.Children.Add(statusHost);
        _ = LoadRecentActivityAsync(panel, status);
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private async Task ResolvePostAuthorsAsync(IReadOnlyList<SchoologyApiRecord> records, SchoologyAuthService.Session session)
    {
        var ids = records.Select(record => JsonLong(record.Data, "user_id", "uid", "author_id", "authorId", "created_by_id"))
            .Where(id => id > 0).Distinct().Where(id => !_userProfileCache.ContainsKey(id)).ToArray();
        await Task.WhenAll(ids.Select(async id =>
        {
            try
            {
                using var profile = await _api.GetUserAsync(session, id);
                var user = profile.RootElement;
                if (user.ValueKind == JsonValueKind.Object && user.TryGetProperty("user", out var nested)) user = nested;
                _userProfileCache[id] = user.Clone();
            }
            catch (Exception ex) { App.WriteStartupLog($"Could not resolve ClassWire post author {id}.", ex); }
        }));
    }

    private async Task ResolveCommentAuthorsAsync(IReadOnlyList<SchoologyApiRecord> comments, SchoologyAuthService.Session session)
    {
        var ids = comments.Select(comment => JsonLong(comment.Data, "uid", "user_id", "userId", "author_id", "authorId", "creator_id"))
            .Where(id => id > 0).Distinct().Where(id => !_userProfileCache.ContainsKey(id)).ToArray();
        await Task.WhenAll(ids.Select(async id =>
        {
            try
            {
                using var profile = await _api.GetUserAsync(session, id);
                var user = profile.RootElement;
                if (user.ValueKind == JsonValueKind.Object && user.TryGetProperty("user", out var nested)) user = nested;
                _userProfileCache[id] = user.Clone();
            }
            catch (Exception ex) { App.WriteStartupLog($"Could not resolve ClassWire comment author {id}.", ex); }
        }));
    }

    private async Task LoadRecentActivityAsync(StackPanel panel, TextBlock status)
    {
        try
        {
            var session = _session ?? throw new InvalidOperationException("Sign in to view recent activity.");
            using var document = await _auth.GetApiJsonAsync(session, "recent",
                new Dictionary<string, string> { ["start"] = "0", ["limit"] = "30", ["with_attachments"] = "TRUE", ["richtext"] = "1" });
            var records = SchoologyApiClient.ReadRecords(document.RootElement, "update", "updates", "update_list");
            RemoveLoadingStatus(status);
            if (records.Count == 0) { panel.Children.Add(EmptyState("No recent activity", "Your recent ClassWire activity will appear here.")); return; }

            // Render the feed immediately so post text and image requests do not
            // wait for one profile API lookup per distinct post author.
            RenderRecentActivityCards(panel, records);
            _ = RefreshRecentActivityAuthorsAsync(panel, records, session);
        }
        catch (Exception ex)
        {
            SetStatusText(status, $"Unable to load recent activity: {ex.Message}");
            panel.Children.Add(Action("Retry", async (_, _) => await LoadRecentActivityAsync(panel, status)));
        }
    }

    private void RenderRecentActivityCards(StackPanel panel, IReadOnlyList<SchoologyApiRecord> records)
    {
        panel.Children.Clear();
        foreach (var record in records)
        {
            var id = JsonLong(record.Data, "user_id", "uid", "author_id", "authorId", "created_by_id");
            JsonElement? profile = id > 0 && _userProfileCache.TryGetValue(id, out var cached) ? cached : null;
            panel.Children.Add(BuildUpdateCard("Recent", 0, record, profile));
        }
    }

    private async Task RefreshRecentActivityAuthorsAsync(
        StackPanel panel, IReadOnlyList<SchoologyApiRecord> records, SchoologyAuthService.Session session)
    {
        try
        {
            await ResolvePostAuthorsAsync(records, session);
            if (_session?.UserId == session.UserId)
                RenderRecentActivityCards(panel, records);
        }
        catch (Exception ex)
        {
            App.WriteStartupLog("Could not refresh recent-activity author details.", ex);
        }
    }

    private static ItemsWrapGrid? FindItemsWrapGrid(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is ItemsWrapGrid panel) return panel;
            var nested = FindItemsWrapGrid(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private GridView BuildCourseDashboardContent()
    {
        // Tauri uses responsive course cards with a wide image, title and period.
        // GridView keeps the same wrapping layout and virtualizes large course lists.
        var grid = new GridView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(12),
            Background = new SolidColorBrush(Colors.Transparent)
        };
        AutomationProperties.SetAutomationId(grid, "HomeCourseDashboardGrid");
        grid.Loaded += (_, _) =>
        {
            var itemsPanel = FindItemsWrapGrid(grid);
            if (itemsPanel is not null) itemsPanel.HorizontalAlignment = HorizontalAlignment.Center;
        };
        if (_courses.Count == 0)
        {
            grid.Items.Add(new GridViewItem { Content = EmptyState("No courses were returned for this account.") });
            return grid;
        }
        foreach (var course in _courses)
        {
            var cardContent = new StackPanel { Spacing = 0 };
            var imageSurface = new Border { Height = 130, Background = ThemeBrush("SchoologyBlueDarkBrush") };
            imageSurface.Child = new TextBlock
            {
                Text = course.Name.Length > 0 ? course.Name[..1].ToUpperInvariant() : "C",
                FontSize = 42, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Brush("#FFFFFFFF"), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (!string.IsNullOrWhiteSpace(course.ImageUrl))
            {
                try { imageSurface.Child = new Image { Source = new BitmapImage(new Uri(NormalizeSchoologyImageUrl(course.ImageUrl))),
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill, Height = 130 }; } catch { }
            }
            cardContent.Children.Add(imageSurface);
            var labels = new StackPanel { Spacing = 4, Padding = new Thickness(12, 10, 12, 12),
                Background = InAppAcrylicBrush(0.58, 0.64) };
            labels.Children.Add(Text(course.Name, 15, true));
            if (!string.IsNullOrWhiteSpace(course.Subtitle))
            {
                var subtitle = Text(course.Subtitle, 12);
                subtitle.Foreground = ThemeBrush("SchoologyMutedTextBrush");
                labels.Children.Add(subtitle);
            }
            cardContent.Children.Add(labels);
            var card = new Border { Width = 250, Height = 205, CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1), BorderBrush = ThemeBrush("SchoologyDividerBrush"),
                Background = InAppAcrylicBrush(0.58, 0.64), Child = cardContent };
            var item = new GridViewItem { Content = card, Width = 250, Height = 205,
                Padding = new Thickness(0), Margin = new Thickness(8), Tag = course };
            AutomationProperties.SetName(item, course.Name);
            item.Tapped += (_, _) => ShowThreePane("Courses", course);
            grid.Items.Add(item);
        }
        return grid;
    }

    private void EnsureBackgroundNotificationPolling()
    {
        if (_session is null || !string.Equals(ReadAppSetting("DesktopNotifications", "true"), "true", StringComparison.OrdinalIgnoreCase)) return;
        if (_notificationTimer is null)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _notificationTimer = timer;
            timer.Tick += async (_, _) =>
            {
                if (_notificationPollInProgress || _session is null) return;
                timer.Stop();
                try { await PollSchoologyNotificationsAsync(); }
                finally
                {
                    timer.Interval = TimeSpan.FromMinutes(5);
                    if (string.Equals(ReadAppSetting("DesktopNotifications", "true"), "true", StringComparison.OrdinalIgnoreCase))
                        timer.Start();
                }
            };
        }
        if (!_notificationTimer.IsEnabled) _notificationTimer.Start();
    }

    private async Task PollSchoologyNotificationsAsync()
    {
        if (_session is null || _notificationPollInProgress ||
            !string.Equals(ReadAppSetting("DesktopNotifications", "true"), "true", StringComparison.OrdinalIgnoreCase)) return;
        _notificationPollInProgress = true;
        try
        {
            using var document = await _api.GetNotificationsAsync(_session);
            var root = document.RootElement;
            JsonElement raw = default;
            if (root.TryGetProperty("notification", out var notification)) raw = notification;
            else if (root.TryGetProperty("notifications", out var notifications)) raw = notifications;
            else if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("notification", out notification)) raw = notification;
                else if (data.TryGetProperty("notifications", out notifications)) raw = notifications;
            }
            if (raw.ValueKind == JsonValueKind.Object)
            {
                if (raw.TryGetProperty("notification", out var nested)) raw = nested;
                else if (raw.TryGetProperty("notifications", out nested)) raw = nested;
                else if (raw.TryGetProperty("items", out nested)) raw = nested;
            }
            var items = raw.ValueKind == JsonValueKind.Array
                ? raw.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).Select(item => item.Clone()).ToArray()
                : Array.Empty<JsonElement>();
            var userId = _session.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var keys = items.Select(NotificationKey).ToArray();
            string previousUser = string.Empty;
            var previousIds = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(NotificationSeenStatePath))
                {
                    using var previous = JsonDocument.Parse(File.ReadAllText(NotificationSeenStatePath));
                    previousUser = ProfileValue(previous.RootElement, "userId") ?? string.Empty;
                    if (previous.RootElement.TryGetProperty("ids", out var ids) && ids.ValueKind == JsonValueKind.Array)
                        foreach (var id in ids.EnumerateArray()) if (id.ValueKind == JsonValueKind.String) previousIds.Add(id.GetString() ?? string.Empty);
                }
            }
            catch { }
            if (previousUser == userId)
                foreach (var item in items.Where(item => !previousIds.Contains(NotificationKey(item))).Take(3).Reverse())
                    ShowSchoologyAppNotification(item);
            Directory.CreateDirectory(Path.GetDirectoryName(NotificationSeenStatePath)!);
            File.WriteAllText(NotificationSeenStatePath, JsonSerializer.Serialize(new { userId, ids = keys.Take(200).ToArray() }));
        }
        catch (Exception ex)
        {
            if (!ex.Message.Contains("401", StringComparison.OrdinalIgnoreCase) &&
                !ex.Message.Contains("Not signed in", StringComparison.OrdinalIgnoreCase))
                App.WriteStartupLog("Background ClassWire notification polling failed.", ex);
        }
        finally { _notificationPollInProgress = false; }
    }

    private static string NotificationKey(JsonElement item)
    {
        var id = ProfileValue(item, "id", "notification_id", "notificationId");
        if (!string.IsNullOrWhiteSpace(id)) return id;
        return string.Join(":", ProfileValue(item, "type") ?? string.Empty,
            ProfileValue(item, "created", "timestamp") ?? string.Empty,
            ProfileValue(item, "body", "message", "title") ?? string.Empty);
    }

    private static string NotificationBody(JsonElement item)
    {
        var body = ProfileValue(item, "body", "message", "title") ?? "You have a new ClassWire notification";
        JsonElement args = default;
        if (item.TryGetProperty("body_args", out args) || item.TryGetProperty("bodyArgs", out args) || item.TryGetProperty("body_arg", out args))
        {
            if (args.ValueKind == JsonValueKind.Array)
                foreach (var argument in args.EnumerateArray())
                {
                    var replacement = ProfileValue(argument, "title", "name") ?? string.Empty;
                    var index = body.IndexOf("%s", StringComparison.Ordinal);
                    if (index >= 0) body = body[..index] + replacement + body[(index + 2)..];
                }
        }
        var clean = string.Join(" ", Regex.Replace(body, "<[^>]*>", " ")
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length > 220 ? clean[..220] : clean;
    }

    private static void ShowSchoologyAppNotification(JsonElement item)
    {
        try
        {
            var body = NotificationBody(item);
            var builder = new AppNotificationBuilder().AddText("ClassWire").AddText(body);
            if (!string.Equals(ReadAppSetting("NotificationSound", "false"), "true", StringComparison.OrdinalIgnoreCase))
                builder.MuteAudio();
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception ex) { App.WriteStartupLog("Could not display a Windows app notification.", ex); }
    }

    private Grid BuildUpcomingContent()
    {
        var root = new Grid { RowSpacing = 8 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var statusHost = LoadingStatus("Loading upcoming assignments and events…", out var status);
        status.Foreground = ThemeBrush("SchoologyMutedTextBrush");
        root.Children.Add(statusHost);
        var list = new ListView { SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = false, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent) };
        AutomationProperties.SetAutomationId(list, "UpcomingEventsList");
        Grid.SetRow(list, 1); root.Children.Add(list);
        _ = LoadUpcomingAsync(list, status);
        return root;
    }

    private async Task<IReadOnlyList<SchoologyApiRecord>> FetchUpcomingRecordsAsync()
    {
        if (_session is null) throw new InvalidOperationException("Sign in to view upcoming items.");
        using var document = await _auth.GetApiJsonAsync(_session, $"users/{_session.UserId}/events",
            new Dictionary<string, string> { ["start_date"] = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                ["start"] = "0", ["limit"] = "100" });
        return SchoologyApiClient.ReadRecords(document.RootElement, "event", "events");
    }

    private static DateTime? ParseSchoologyDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (long.TryParse(value, out var epoch))
        {
            try { return epoch > 10_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(epoch).LocalDateTime :
                DateTimeOffset.FromUnixTimeSeconds(epoch).LocalDateTime; } catch { }
        }
        return DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeLocal, out var parsed) ? parsed : null;
    }

    private static string FormatAssignmentDueDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var parsed = ParseSchoologyDate(value);
        if (parsed is null) return value;
        var local = parsed.Value.Kind == DateTimeKind.Utc ? parsed.Value.ToLocalTime() : parsed.Value;
        return local.ToString("ddd, MMM d, yyyy 'at' h:mm tt",
            System.Globalization.CultureInfo.CurrentCulture);
    }

    private void RenderGroupedUpcoming(ListView list, IReadOnlyList<SchoologyApiRecord> records, DateTime? selectedDate = null,
        string? entityKind = null, long entityId = 0)
    {
        list.Items.Clear();
        var ordered = records.Select(record => new { Record = record,
            Date = ParseSchoologyDate(ProfileValue(record.Data, "start", "start_time", "date", "due", "due_date")) })
            .Where(x => selectedDate is null || x.Date?.Date == selectedDate.Value.Date)
            .OrderBy(x => x.Date ?? DateTime.MaxValue).ToArray();
        foreach (var group in ordered.GroupBy(x => x.Date?.Date ?? DateTime.MinValue))
        {
            var title = group.Key == DateTime.MinValue ? "Date not specified" :
                group.Key.ToString("dddd, MMMM d", System.Globalization.CultureInfo.CurrentCulture);
            list.Items.Add(new ListViewItem { Content = Text(title, 13, true),
                IsHitTestVisible = false, IsTabStop = false, Padding = new Thickness(12, 10, 12, 6) });
            foreach (var entry in group)
            {
                var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(12, 9, 12, 9) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                // Reuse the WinUI glyph mapping already used by course materials and discussions.
                row.Children.Add(new FontIcon { Glyph = MaterialGlyph(entry.Record.Data), FontSize = 22,
                    Foreground = ThemeBrush("SchoologyBlueBrush"), VerticalAlignment = VerticalAlignment.Center });
                var details = new StackPanel { Spacing = 3 };
                details.Children.Add(Text(entry.Record.Title, 14, true));
                var course = ProfileValue(entry.Record.Data, "section_title", "course_title", "section_name");
                if (!string.IsNullOrWhiteSpace(course)) details.Children.Add(Text(course, 12));
                var time = entry.Date?.ToString("t", System.Globalization.CultureInfo.CurrentCulture);
                if (!string.IsNullOrWhiteSpace(time)) details.Children.Add(Text(time, 11));
                Grid.SetColumn(details, 1); row.Children.Add(details);
                var item = new ListViewItem { Content = row, Tag = entry.Record,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch };
                AutomationProperties.SetName(item, entry.Record.Title);
                item.Tapped += async (_, _) => await ShowUpcomingItemAsync(entry.Record, entityKind, entityId);
                list.Items.Add(item);
            }
        }
        if (ordered.Length == 0)
            list.Items.Add(new ListViewItem { Content = Surface(Text("Nothing is scheduled for this date.")) });
    }

    private (string Kind, long Id) ResolveContentOwner(JsonElement data, string? fallbackKind = null, long fallbackId = 0)
    {
        var realm = (ProfileValue(data, "realm", "realm_type", "realmType") ?? string.Empty).ToLowerInvariant();
        var realmId = JsonLong(data, "realm_id", "realmId", "owner_id", "ownerId");
        var sectionId = JsonLong(data, "section_id", "sectionId", "course_id", "courseId");
        var groupId = JsonLong(data, "group_id", "groupId");
        var schoolId = JsonLong(data, "school_id", "schoolId");
        var userId = JsonLong(data, "user_id", "userId", "uid");
        if (realm is "group" or "groups" || groupId > 0) return ("Groups", groupId > 0 ? groupId : realmId > 0 ? realmId : fallbackId);
        if (realm is "school" or "schools" || schoolId > 0) return ("School Profile", schoolId > 0 ? schoolId : realmId > 0 ? realmId : fallbackId);
        if (realm is "user" or "users") return ("Profile", userId > 0 ? userId : realmId > 0 ? realmId : _session?.UserId ?? fallbackId);
        if (realm is "section" or "sections" or "course" or "courses" || sectionId > 0)
            return ("Courses", sectionId > 0 ? sectionId : realmId > 0 ? realmId : fallbackId);
        if (!string.IsNullOrWhiteSpace(fallbackKind) && fallbackId > 0) return (fallbackKind, fallbackId);
        if (userId > 0) return ("Profile", userId);
        return ("Profile", _session?.UserId ?? 0);
    }

    private async Task ShowUpcomingItemAsync(SchoologyApiRecord record, string? entityKind = null, long entityId = 0)
    {
        var type = (ProfileValue(record.Data, "type", "event_type", "eventType") ?? "").ToLowerInvariant();
        var assignmentId = JsonLong(record.Data, "assignment_id", "assignmentId", "id");
        var discussionId = JsonLong(record.Data, "discussion_id", "discussionId", "id");
        var eventId = JsonLong(record.Data, "event_id", "eventId", "id");
        var sectionId = JsonLong(record.Data, "section_id", "sectionId");
        var returnSection = _section is "Upcoming" or "Calendar" ? _section : "Recent Activity";
        var owner = ResolveContentOwner(record.Data, entityKind, entityId);
        if (type == "assignment" && assignmentId > 0 && sectionId > 0 && _session is not null)
        { await ShowAssignmentDetailsAsync(sectionId, record); return; }
        if (type is "assessment" or "assessment_v2" or "managed_assessment" or "quiz" or "test")
        {
            if (assignmentId > 0)
            { await OpenSharedWebViewAsync($"https://app.schoology.com/assignment/{assignmentId}", record.Title,
                allowLtiExternal: true, onBack: () => ShowHome(returnSection)); return; }
        }
        if ((type is "external_tool" or "lti" or "scorm") && sectionId > 0 && assignmentId > 0)
        {
            var route = type == "external_tool" ? "external-tool" : type;
            await OpenSharedWebViewAsync($"https://app.schoology.com/section/{sectionId}/{route}/{assignmentId}",
                record.Title, allowLtiExternal: true, onBack: () => ShowHome(returnSection));
            return;
        }
        if (type == "discussion" && discussionId > 0 && owner.Id > 0)
        { await ShowDiscussionDetailsAsync(owner.Kind, owner.Id, record, () => ShowHome(returnSection)); return; }
        if (type == "event" && eventId > 0 && owner.Id > 0)
        { await ShowEventDetailsAsync(owner.Kind, owner.Id, record, () => ShowHome(returnSection)); return; }
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(Text(record.Title, 20, true));
        var course = ProfileValue(record.Data, "section_title", "course_title", "section_name");
        if (!string.IsNullOrWhiteSpace(course)) content.Children.Add(Text(course, 13));
        var date = ProfileValue(record.Data, "start", "start_time", "date", "due", "due_date");
        if (!string.IsNullOrWhiteSpace(date)) content.Children.Add(Text(date, 12));
        var description = ProfileValue(record.Data, "description", "body", "description_richtext");
        if (!string.IsNullOrWhiteSpace(description)) content.Children.Add(CreateRichContent(description, 13));
        await new ContentDialog { Title = "Upcoming item", Content = CreateDialogScrollViewport(content), CloseButtonText = "Close", XamlRoot = _root.XamlRoot, RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme }.ShowAsync();
    }

    private async Task LoadUpcomingAsync(ListView list, TextBlock status)
    {
        try
        {
            var records = await FetchUpcomingRecordsAsync();
            RenderGroupedUpcoming(list, records);
            SetStatusText(status, records.Count == 0 ? "No upcoming events or assignments." : $"{records.Count} upcoming items");
        }
        catch (Exception ex)
        {
            SetStatusText(status, $"Unable to load upcoming items: {ex.Message}");
            if (list.Items.Count == 0) list.Items.Add(new ListViewItem { Content = Action("Retry", async (_, _) => await LoadUpcomingAsync(list, status)) });
        }
    }

    private Grid BuildCalendarPage()
    {
        var root = new Grid { Margin = new Thickness(18), RowSpacing = 12 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var selector = new SelectorBar { HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(selector, "CalendarSelectorBar");
        var calendarTab = new SelectorBarItem { Text = "Calendar" };
        var upcomingTab = new SelectorBarItem { Text = "Upcoming" };
        AutomationProperties.SetAutomationId(calendarTab, "CalendarTab");
        AutomationProperties.SetAutomationId(upcomingTab, "UpcomingTab");
        selector.Items.Add(calendarTab);
        selector.Items.Add(upcomingTab);
        Grid.SetRow(selector, 0);
        root.Children.Add(selector);
        var frame = new Frame { Background = new SolidColorBrush(Colors.Transparent) };
        Grid.SetRow(frame, 1);
        root.Children.Add(frame);
        void Select(SelectorBarItem item)
        {
            frame.Content = item == upcomingTab ? BuildUpcomingContent() : BuildCalendarCalendarView();
        }
        selector.SelectionChanged += (_, _) =>
        {
            if (selector.SelectedItem is SelectorBarItem item) Select(item);
        };
        selector.SelectedItem = calendarTab;
        Select(calendarTab);
        return root;
    }

    private Grid BuildCalendarCalendarView()
    {
        var layout = new Grid { Margin = new Thickness(18), ColumnSpacing = 16, RowSpacing = 12 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(Text("Calendar", 24, true));

        var calendar = new CalendarView
        {
            SelectionMode = CalendarViewSelectionMode.Single,
            IsTodayHighlighted = true
        };
        Grid.SetRow(calendar, 1);
        layout.Children.Add(calendar);

        var right = new Grid { RowSpacing = 8 };
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var selectedDate = DateTime.Today;
        var heading = Text(selectedDate.ToString("dddd, MMMM d",
            System.Globalization.CultureInfo.CurrentCulture), 18, true);
        right.Children.Add(heading);
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetAutomationId(list, "CalendarEventsList");
        Grid.SetRow(list, 1);
        right.Children.Add(list);
        Grid.SetColumn(right, 1);
        Grid.SetRow(right, 1);
        layout.Children.Add(right);

        var monthRecords = new Dictionary<DateTime, IReadOnlyList<SchoologyApiRecord>>();
        var monthEventDates = new Dictionary<DateTime, HashSet<DateTime>>();
        var loadingMonths = new HashSet<DateTime>();
        var visibleDayItems = new Dictionary<DateTime, CalendarViewDayItem>();
        var indicatorColor = ThemeBrush("SchoologyBlueBrush").Color;

        static DateTime MonthKey(DateTime date) => new(date.Year, date.Month, 1);

        void ApplyDayIndicator(CalendarViewDayItem dayItem, DateTime date)
        {
            var month = MonthKey(date);
            if (!monthEventDates.TryGetValue(month, out var dates) || !dates.Contains(date.Date))
            {
                // SetDensityColors controls the CalendarView marker directly;
                // the WinUI 3 CalendarViewDayItem has no Density property.
                dayItem.SetDensityColors(new List<Windows.UI.Color>());
                return;
            }

            dayItem.SetDensityColors(new List<Windows.UI.Color> { indicatorColor });
        }

        void RenderSelectedDate()
        {
            var month = MonthKey(selectedDate);
            if (monthRecords.TryGetValue(month, out var records))
                RenderGroupedUpcoming(list, records, selectedDate);
            else
            {
                list.Items.Clear();
                list.Items.Add(new ListViewItem { Content = LoadingStatus("Loading calendar events…") });
            }
        }

        async Task LoadMonthAsync(DateTime requestedMonth)
        {
            var month = MonthKey(requestedMonth);
            if (monthRecords.ContainsKey(month) || !loadingMonths.Add(month)) return;
            try
            {
                var session = _session ?? throw new InvalidOperationException("Sign in to view your calendar.");
                var records = await _api.GetCalendarEventsForMonthAsync(session, month);
                var dates = records
                    .Select(record => ParseSchoologyDate(ProfileValue(record.Data,
                        "start", "start_time", "date", "due", "due_date")))
                    .Where(date => date.HasValue)
                    .Select(date => date!.Value.Date)
                    .ToHashSet();

                monthRecords[month] = records;
                monthEventDates[month] = dates;
                foreach (var pair in visibleDayItems.Where(pair => MonthKey(pair.Key) == month).ToArray())
                    ApplyDayIndicator(pair.Value, pair.Key);
                if (MonthKey(selectedDate) == month)
                    RenderSelectedDate();
            }
            catch (Exception ex)
            {
                if (MonthKey(selectedDate) == month)
                {
                    list.Items.Clear();
                    var error = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
                    error.Children.Add(Text($"Unable to load calendar: {ex.Message}", 13));
                    error.Children.Add(Action("Retry", async (_, _) => await LoadMonthAsync(month)));
                    list.Items.Add(new ListViewItem { Content = error });
                }
            }
            finally
            {
                loadingMonths.Remove(month);
            }
        }

        // CalendarView virtualizes day items. Cache each month so dates containing
        // events receive a density marker without issuing a request per day.
        calendar.CalendarViewDayItemChanging += (_sender, args) =>
        {
            var date = args.Item.Date.Date;
            visibleDayItems[date] = args.Item;
            ApplyDayIndicator(args.Item, date);
            if (!monthRecords.ContainsKey(MonthKey(date)))
                _ = LoadMonthAsync(date);
        };
        calendar.SelectedDatesChanged += (_, _) =>
        {
            if (calendar.SelectedDates.Count == 0) return;
            selectedDate = calendar.SelectedDates[0].Date;
            heading.Text = selectedDate.ToString("dddd, MMMM d",
                System.Globalization.CultureInfo.CurrentCulture);
            RenderSelectedDate();
            _ = LoadMonthAsync(selectedDate);
        };

        calendar.SelectedDates.Add(new DateTimeOffset(DateTime.Today));
        RenderSelectedDate();
        _ = LoadMonthAsync(selectedDate);
        return layout;
    }

    private sealed record SchoologyRequestItem(string Type, long Id, string Title, string Subtitle, JsonElement Data);
    private sealed record ResourceTreeEntry(string Title, string Kind, long Id, long CollectionId, JsonElement Data)
    {
        public override string ToString() => Title;
    }

    private Grid BuildNotificationsPage()
    {
        var root = new Grid { Margin = new Thickness(24), RowSpacing = 10 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(Text("Notifications", 24, true));
        var list = new ListView { SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = false, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(list, "SchoologyNotificationsList");
        Grid.SetRow(list, 1); root.Children.Add(list);
        list.Items.Add(new ListViewItem { Content = LoadingStatus("Loading notifications…") });
        _ = LoadNotificationsAsync(list);
        return root;
    }

    private async Task LoadNotificationsAsync(ListView list)
    {
        try
        {
            var session = _session ?? throw new InvalidOperationException("Sign in to view notifications.");
            using var document = await _api.GetNotificationsAsync(session);
            var root = document.RootElement;
            var notificationData = root;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var dataNode) &&
                dataNode.ValueKind == JsonValueKind.Object && dataNode.TryGetProperty("notifications", out var dataNotifications))
                notificationData = dataNotifications;
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("notifications", out var nestedNotifications))
                notificationData = nestedNotifications;
            var unviewed = Math.Max(JsonLong(root, "unviewed_count", "unviewedCount"),
                JsonLong(notificationData, "unviewed_count", "unviewedCount"));
            if (unviewed > 0)
            {
                try { using var ignored = await _api.MarkNotificationsReadAsync(session); }
                catch (Exception ex) { App.WriteStartupLog("Could not mark notifications as read.", ex); }
            }
            var records = SchoologyApiClient.ReadRecords(root, "notification", "notifications");
            if (records.Count == 0)
                records = SchoologyApiClient.ReadRecords(notificationData, "notification", "notifications", "items");
            list.Items.Clear();
            if (records.Count == 0)
            {
                list.Items.Add(new ListViewItem { Content = EmptyState("You have no notifications") });
                return;
            }
            foreach (var record in records)
            {
                var body = ProfileValue(record.Data, "body", "message", "title", "text") ?? record.Title;
                var args = record.Data.ValueKind == JsonValueKind.Object &&
                    record.Data.TryGetProperty("body_args", out var bodyArgs) ? bodyArgs :
                    record.Data.ValueKind == JsonValueKind.Object &&
                    record.Data.TryGetProperty("bodyArgs", out var bodyArgsCamel) ? bodyArgsCamel : default;
                body = FormatNotificationBody(body, args);
                var type = (ProfileValue(record.Data, "type", "notification_type", "notificationType") ?? "").ToLowerInvariant();
                var realm = (ProfileValue(record.Data, "realm") ?? "").ToLowerInvariant();
                var iconGlyph = type switch
                {
                    "grade_add" => "\uE8A5",
                    "grade_item_submit" => "\uE8F1",
                    "badge_award" => "\uE734",
                    "course_materials_add" => "\uE8B7",
                    "comment_add" or "comment_private_add" => "\uE90A",
                    "discussion_add" when realm == "schools" => "\uE8BD",
                    "discussion_add" when realm == "groups" => "\uE8B7",
                    "event_add" or "event_update" => "\uE787",
                    _ when type.Contains("assignment") || type.Contains("assessment") => "\uE8F1",
                    _ when type.Contains("event") => "\uE787",
                    _ => "\uE713"
                };
                var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 12, 10, 12) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new FontIcon { Glyph = iconGlyph, FontSize = 22,
                    Foreground = ThemeBrush("SchoologyBlueBrush"), VerticalAlignment = VerticalAlignment.Top });
                var labels = new StackPanel { Spacing = 4 };
                labels.Children.Add(Text(StripMarkup(body), 14, true));
                var dateRaw = ProfileValue(record.Data, "created", "timestamp", "created_at");
                if (!string.IsNullOrWhiteSpace(dateRaw))
                {
                    var date = Text(ParseSchoologyDate(dateRaw)?.ToString("g",
                        System.Globalization.CultureInfo.CurrentCulture) ?? dateRaw, 11);
                    date.Foreground = ThemeBrush("SchoologyMutedTextBrush");
                    labels.Children.Add(date);
                }
                Grid.SetColumn(labels, 1); row.Children.Add(labels);
                var disclosure = new FontIcon { Glyph = "\uE76C", FontSize = 14,
                    Foreground = ThemeBrush("SchoologyMutedTextBrush"), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(disclosure, 2); row.Children.Add(disclosure);
                var item = new ListViewItem { Content = row, Tag = record,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch };
                AutomationProperties.SetName(item, StripMarkup(body));
                item.Tapped += async (_, _) => await OpenNotificationAsync(record, body);
                item.KeyDown += async (_, args) =>
                {
                    if (args.Key == Windows.System.VirtualKey.Enter)
                        await OpenNotificationAsync(record, body);
                };
                list.Items.Add(item);
            }
        }
        catch (Exception ex)
        {
            list.Items.Clear();
            list.Items.Add(new ListViewItem { Content = EmptyState("Unable to load notifications", ex.Message) });
            list.Items.Add(new ListViewItem { Content = Action("Retry", async (_, _) => await LoadNotificationsAsync(list)) });
        }
    }

    private static string NotificationGlyph(JsonElement item)
    {
        var type = (ProfileValue(item, "type", "notification_type", "notificationType",
            "template_type", "templateType") ?? "").ToLowerInvariant();
        var realm = (ProfileValue(item, "realm") ?? "").ToLowerInvariant();
        return type switch
        {
            "grade_add" => "\uE8A5",
            "grade_item_submit" => "\uE8F1",
            "badge_award" => "\uE734",
            "course_materials_add" => "\uE8B7",
            "comment_add" or "comment_private_add" => "\uE90A",
            "discussion_add" when realm == "schools" => "\uE8BD",
            "discussion_add" when realm == "groups" => "\uE8B7",
            "event_add" or "event_update" => "\uE787",
            _ when type.Contains("assignment") || type.Contains("assessment") => "\uE8F1",
            _ when type.Contains("event") => "\uE787",
            _ => MaterialGlyph(item)
        };
    }

    private static string FormatNotificationBody(string body, JsonElement bodyArgs)
    {
        if (string.IsNullOrEmpty(body) || bodyArgs.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return body;
        var args = ReadJsonItems(bodyArgs, "body_arg", "body_args", "items");
        if (args.Length == 0) return body;
        var sequentialIndex = 0;
        return Regex.Replace(body, @"%(?:(\d+)\$|<)?s", match =>
        {
            var index = match.Groups[1].Success
                ? (int.TryParse(match.Groups[1].Value, out var positional) ? positional - 1 : -1)
                : sequentialIndex++;
            if (index < 0 || index >= args.Length) return match.Value;
            var value = ProfileValue(args[index], "title", "name", "text") ?? args[index].ToString();
            return StripMarkup(value);
        });
    }

    private static string? FindNotificationLink(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "url", "href", "link", "target_url", "targetUrl", "web_url", "webUrl" })
                if (element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                    return value.GetString();
            foreach (var property in element.EnumerateObject())
            {
                var nested = FindNotificationLink(property.Value);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindNotificationLink(item);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        return null;
    }

    private static List<JsonElement> CollectNotificationTargets(JsonElement root)
    {
        var result = new List<JsonElement>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(JsonElement node, int depth)
        {
            if (depth > 10 || node.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return;
            if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in node.EnumerateArray()) Walk(child, depth + 1);
                return;
            }
            if (node.ValueKind != JsonValueKind.Object) return;
            var type = ProfileValue(node, "type", "template_type", "templateType") ?? "";
            var id = JsonLong(node, "id");
            if (type.Length > 0 && id > 0 && seen.Add(type + ":" + id.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                result.Add(node.Clone());
            foreach (var property in node.EnumerateObject()) Walk(property.Value, depth + 1);
        }
        Walk(root, 0);
        return result;
    }

    private async Task<bool> OpenNotificationTargetAsync(
        JsonElement argument, string realm, long realmId, string notificationType, string? typeOverride = null)
    {
        var targetType = typeOverride ?? ProfileValue(argument, "type", "template_type", "templateType")?.ToLowerInvariant() ?? "";
        var targetId = JsonLong(argument, "id");
        if (targetId <= 0) return false;
        var title = ProfileValue(argument, "title", "name") ?? "ClassWire";
        switch (targetType)
        {
            case "assignment" when realm == "sections" && realmId > 0:
                await ShowAssignmentDetailsAsync(realmId, new SchoologyApiRecord(title, "", argument.Clone()));
                return true;
            case "assessment" when realm == "sections" && realmId > 0:
            case "assessment_v2" when realm == "sections" && realmId > 0:
            case "managed_assessment" when realm == "sections" && realmId > 0:
                await OpenSharedWebViewAsync(new Uri(new Uri(_auth.WebSessionUrl), $"assignment/{targetId}").AbsoluteUri,
                    title, onBack: () => ShowHome("Notifications"));
                return true;
            case "discussion" when realmId > 0 && (realm is "sections" or "groups" or "schools"):
                await ShowDiscussionDetailsAsync(realm == "groups" ? "Groups" :
                    realm == "schools" ? "School Profile" : "Courses",
                    realmId, new SchoologyApiRecord(title, "", argument.Clone()));
                return true;
            case "event" when realmId > 0 && (realm is "sections" or "groups" or "schools"):
                await ShowEventDetailsAsync(realm == "groups" ? "Groups" :
                    realm == "schools" ? "School Profile" : "Courses",
                    realmId, new SchoologyApiRecord(title, "", argument.Clone()));
                return true;
            case "group":
                ShowThreePane("Groups", _groups.FirstOrDefault(item => item.Id == targetId) ??
                    new SchoologyEntity(targetId, title, "", null, argument.Clone()));
                return true;
            case "section":
                ShowThreePane("Courses", _courses.FirstOrDefault(item => item.Id == targetId) ??
                    new SchoologyEntity(targetId, title, "", null, argument.Clone()));
                return true;
            case "school":
                ShowThreePane("School Profile", new SchoologyEntity(targetId, title, "", null, argument.Clone()));
                return true;
            case "user":
                await OpenSharedWebViewAsync(new Uri(new Uri(_auth.WebSessionUrl), $"user/{targetId}").AbsoluteUri,
                    title, onBack: () => ShowHome("Notifications"));
                return true;
            case "document" when realm == "sections" && realmId > 0 && _session is not null:
                using (var document = await _api.GetDocumentAsync(_session, realmId, targetId))
                {
                    var documentRoot = document.RootElement.TryGetProperty("document", out var nestedDocument)
                        ? nestedDocument : document.RootElement;
                    var attachments = ReadAttachmentItems(documentRoot);
                    if (attachments.Count > 0)
                    {
                        await OpenAttachmentAsync(attachments[0].Item, attachments[0].Kind,
                            () => ShowHome("Notifications"));
                        return true;
                    }
                }
                return false;
            case "page":
                await OpenSharedWebViewAsync(new Uri(new Uri(_auth.WebSessionUrl), $"page/{targetId}").AbsoluteUri,
                    title, onBack: () => ShowHome("Notifications"));
                return true;
            default:
                return false;
        }
    }

    private async Task OpenNotificationAsync(SchoologyApiRecord record, string body)
    {
        var target = FindJsonPropertyRecursive(record.Data, "body_args", "bodyArgs", "body_arg", "bodyArg");
        var topArguments = ReadJsonItems(target, "body_arg", "body_args", "items", "all");
        var realm = (ProfileValue(record.Data, "realm") ?? "users").ToLowerInvariant() switch
        {
            "section" => "sections", "group" => "groups", "school" => "schools", "user" => "users",
            var value => value
        };
        var realmId = JsonLong(record.Data, "realm_id", "realmId", "user_id", "uid");
        if (realmId <= 0 && realm == "users") realmId = _session?.UserId ?? 0;
        var notificationType = (ProfileValue(record.Data, "type", "notification_type", "notificationType") ?? "").ToLowerInvariant();

        var candidates = CollectNotificationTargets(target);
        var lastArgument = topArguments.LastOrDefault();
        var lastArgumentType = ProfileValue(lastArgument, "type", "template_type", "templateType")?.ToLowerInvariant() ?? "";
        var expectedType = notificationType switch
        {
            var value when value.Contains("badge", StringComparison.Ordinal) => "section",
            var value when value.Contains("discussion", StringComparison.Ordinal) => "discussion",
            var value when value.Contains("event", StringComparison.Ordinal) => "event",
            "grade_item_submit" => lastArgumentType.Length > 0 ? lastArgumentType : "assignment",
            var value when value.Contains("assessment_v2", StringComparison.Ordinal) || value.Contains("managed_assessment", StringComparison.Ordinal) => "assessment_v2",
            var value when value.Contains("assessment", StringComparison.Ordinal) || value.Contains("quiz", StringComparison.Ordinal) => "assessment",
            var value when value.Contains("assignment", StringComparison.Ordinal) => "assignment",
            var value when value.Contains("document", StringComparison.Ordinal) || value.Contains("resource", StringComparison.Ordinal) => "document",
            var value when value.Contains("page", StringComparison.Ordinal) => "page",
            var value when value.Contains("group", StringComparison.Ordinal) => "group",
            var value when value.Contains("friend", StringComparison.Ordinal) || value.Contains("profile", StringComparison.Ordinal) || value.Contains("user", StringComparison.Ordinal) => "user",
            _ => ""
        };
        bool Matches(JsonElement candidate, string expected)
        {
            var actual = ProfileValue(candidate, "type", "template_type", "templateType")?.ToLowerInvariant() ?? "";
            if (actual == expected) return true;
            if (expected == "assessment_v2") return actual is "assessment_v2" or "managed_assessment" or "quiz";
            if (expected == "assessment") return actual is "assessment" or "quiz";
            return false;
        }
        var selected = expectedType.Length > 0
            ? candidates.LastOrDefault(candidate => Matches(candidate, expectedType))
            : default;
        if (selected.ValueKind == JsonValueKind.Undefined)
            selected = candidates.LastOrDefault(candidate =>
                !string.Equals(ProfileValue(candidate, "type"), "user", StringComparison.OrdinalIgnoreCase));
        if (selected.ValueKind == JsonValueKind.Undefined)
            selected = candidates.LastOrDefault();

        var topMore = topArguments.LastOrDefault();
        if (string.Equals(ProfileValue(topMore, "type"), "more", StringComparison.OrdinalIgnoreCase))
        {
            var moreItems = ReadJsonItems(topMore, "all", "items");
            if (moreItems.Length > 0)
            {
                var list = new ListView
                {
                    SelectionMode = ListViewSelectionMode.None,
                    IsItemClickEnabled = false,
                    MaxHeight = 440,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };
                AutomationProperties.SetAutomationId(list, "NotificationTargetList");
                JsonElement chosenTarget = default;
                ContentDialog moreDialog = null!;
                foreach (var option in moreItems)
                {
                    var optionTitle = ProfileValue(option, "title", "name") ?? ProfileValue(option, "type") ?? "Open item";
                    var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 8, 10, 8) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.Children.Add(new FontIcon { Glyph = NotificationGlyph(option), FontSize = 21,
                        Foreground = ThemeBrush("SchoologyBlueBrush"), VerticalAlignment = VerticalAlignment.Center });
                    var label = Text(optionTitle, 13, true);
                    label.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(label, 1);
                    row.Children.Add(label);
                    var item = new ListViewItem { Content = row, Tag = option.Clone(),
                        HorizontalContentAlignment = HorizontalAlignment.Stretch };
                    AutomationProperties.SetName(item, optionTitle);
                    item.Tapped += (_, _) =>
                    {
                        chosenTarget = option.Clone();
                        moreDialog.Hide();
                    };
                    list.Items.Add(item);
                }
                moreDialog = new ContentDialog { Title = "Choose notification item", Content = list,
                    CloseButtonText = "Close", XamlRoot = _root.XamlRoot, RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme };
                await moreDialog.ShowAsync();
                if (chosenTarget.ValueKind != JsonValueKind.Undefined)
                {
                    var opened = await OpenNotificationTargetAsync(chosenTarget, realm, realmId, notificationType);
                    if (!opened) await OpenNotificationFallbackAsync(record, body);
                }
                return;
            }
        }

        if (selected.ValueKind != JsonValueKind.Undefined)
        {
            var selectedType = ProfileValue(selected, "type", "template_type", "templateType")?.ToLowerInvariant() ?? "";
            var overrideType = notificationType == "badge_award" && selectedType == "user" ? "section" : null;
            if (await OpenNotificationTargetAsync(selected, realm, realmId, notificationType, overrideType)) return;
        }
        else
        {
            var fallbackType = ProfileValue(record.Data, "object_type", "objectType") ??
                (notificationType == "discussion_add" ? "discussion" :
                 notificationType == "grade_item_submit" ? "assignment" : "");
            var fallbackId = JsonLong(record.Data, "object_id", "objectId", "assignment_id", "assignmentId");
            if (fallbackId > 0 && fallbackType.Length > 0)
            {
                using var fallbackDocument = JsonDocument.Parse(JsonSerializer.Serialize(new { id = fallbackId, type = fallbackType, title = record.Title }));
                if (await OpenNotificationTargetAsync(fallbackDocument.RootElement, realm, realmId, notificationType)) return;
            }
        }
        await OpenNotificationFallbackAsync(record, body);
    }

    private async Task OpenNotificationFallbackAsync(SchoologyApiRecord record, string body)
    {
        var url = FindNotificationLink(record.Data);
        if (!string.IsNullOrWhiteSpace(url))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                uri = new Uri(new Uri(_auth.WebSessionUrl), url.TrimStart('/'));
            if (uri.Scheme is "https" or "http")
            {
                await OpenSharedWebViewAsync(uri.AbsoluteUri, "Notification", onBack: () => ShowHome("Notifications"));
                return;
            }
        }
        var dialog = new ContentDialog { Title = "Notification", Content = Text(StripMarkup(body), 14),
            CloseButtonText = "Close", XamlRoot = _root.XamlRoot, RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme };
        await dialog.ShowAsync();
    }

    private Grid BuildRequestsPage()
    {
        var root = new Grid { Margin = new Thickness(24), RowSpacing = 10 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(Text("Requests", 24, true));
        var list = new ListView { SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = false, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(list, "SchoologyRequestsList");
        Grid.SetRow(list, 1); root.Children.Add(list);
        list.Items.Add(new ListViewItem { Content = LoadingStatus("Loading requests…") });
        _ = LoadRequestsAsync(list);
        return root;
    }

    private async Task LoadRequestsAsync(ListView list)
    {
        try
        {
            var session = _session ?? throw new InvalidOperationException("Sign in to view requests.");
            var results = await Task.WhenAll(
                _api.GetFriendRequestsAsync(session),
                _api.GetGroupInvitationsAsync(session),
                _api.GetSectionInvitationsAsync(session));
            using var friendRequests = results[0];
            using var groupInvitations = results[1];
            using var sectionInvitations = results[2];
            var requests = new List<SchoologyRequestItem>();
            AddRequests(friendRequests.RootElement, "friend", "request", "requests");
            AddRequests(groupInvitations.RootElement, "group", "invite", "invites");
            AddRequests(sectionInvitations.RootElement, "section", "invite", "invites");
            void AddRequests(JsonElement root, string type, params string[] keys)
            {
                foreach (var record in SchoologyApiClient.ReadRecords(root, keys))
                {
                    var id = JsonLong(record.Data, "id", "request_id", "requestId", "invite_id", "inviteId");
                    if (id <= 0) continue;
                    var title = type == "friend"
                        ? ProfileValue(record.Data, "requester_name", "requesterName", "name", "display_name") ?? "Friend request"
                        : ProfileValue(record.Data, "name", "title", type == "group" ? "group_name" : "section_title") ?? "Invitation";
                    var subtitle = ProfileValue(record.Data, "school_name", "schoolName", "description", "message") ?? "";
                    requests.Add(new SchoologyRequestItem(type, id, title, subtitle, record.Data.Clone()));
                }
            }

            list.Items.Clear();
            if (requests.Count == 0)
            {
                list.Items.Add(new ListViewItem { Content = EmptyState("You have no requests") });
                return;
            }
            foreach (var request in requests)
            {
                var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 12, 10, 12) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var details = new StackPanel { Spacing = 4 };
                var title = Action(request.Title, async (_, _) => await OpenRequestAsync(request));
                title.HorizontalAlignment = HorizontalAlignment.Left;
                title.HorizontalContentAlignment = HorizontalAlignment.Left;
                title.Padding = new Thickness(0, 2, 0, 2);
                title.Background = new SolidColorBrush(Colors.Transparent);
                title.BorderThickness = new Thickness(0);
                details.Children.Add(title);
                if (!string.IsNullOrWhiteSpace(request.Subtitle))
                {
                    var subtitle = Text(request.Subtitle, 12);
                    subtitle.Foreground = ThemeBrush("SchoologyMutedTextBrush");
                    details.Children.Add(subtitle);
                }
                Grid.SetColumn(details, 0); row.Children.Add(details);
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
                    VerticalAlignment = VerticalAlignment.Center };
                var accept = Action("Accept", async (_, _) => await RespondToRequestAsync(request, true), true);
                accept.Width = 88;
                var decline = Action("Decline", async (_, _) => await RespondToRequestAsync(request, false));
                decline.Width = 88;
                actions.Children.Add(accept); actions.Children.Add(decline);
                Grid.SetColumn(actions, 1); row.Children.Add(actions);
                var item = new ListViewItem { Content = row, Tag = request,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch };
                list.Items.Add(item);
            }
        }
        catch (Exception ex)
        {
            list.Items.Clear();
            list.Items.Add(new ListViewItem { Content = EmptyState("Unable to load requests", ex.Message) });
            list.Items.Add(new ListViewItem { Content = Action("Retry", async (_, _) => await LoadRequestsAsync(list)) });
        }
    }

    private async Task OpenRequestAsync(SchoologyRequestItem request)
    {
        var id = request.Type == "friend"
            ? JsonLong(request.Data, "requester_id", "requesterId", "user_id", "uid")
            : JsonLong(request.Data, "realm_id", "realmId", "group_id", "groupId", "section_id", "sectionId");
        if (id <= 0) return;
        var url = request.Type == "friend" ? $"https://app.schoology.com/user/{id}" :
            request.Type == "group" ? $"https://app.schoology.com/group/{id}" : $"https://app.schoology.com/course/{id}";
        await OpenSharedWebViewAsync(url, request.Title, onBack: () => ShowHome("Requests"));
    }

    private async Task RespondToRequestAsync(SchoologyRequestItem request, bool accept)
    {
        try
        {
            if (_session is null) throw new InvalidOperationException("Sign in to manage requests.");
            using var response = await _api.RespondToRequestAsync(_session, request.Type, request.Id, accept);
            if (_sectionFrame is not null) await LoadRequestsAsync((ListView)((Grid)((SchoologyShellContentPage)_sectionFrame.Content!).Content!).Children.OfType<ListView>().First());
            else ShowHome("Requests");
        }
        catch (Exception ex) { await ShowApiErrorAsync($"Unable to {(accept ? "accept" : "decline")} request", ex.Message); }
    }

    private StackPanel BuildResourceEntriesView(
        IReadOnlyList<(string Title, IReadOnlyList<ResourceTreeEntry> Entries)> categories,
        string contextKind, long contextId, Action? onBack = null,
        Action<ResourceTreeEntry>? onSelect = null, HashSet<long>? selectedResourceIds = null)
    {
        var content = new StackPanel { Spacing = 8, Margin = new Thickness(4, 4, 4, 16) };
        foreach (var category in categories)
        {
            content.Children.Add(Text(category.Title, 13, true));
            if (category.Entries.Count == 0) content.Children.Add(EmptyState("No resources"));
            else AddResourceEntryRows(content, category.Entries, contextKind, contextId, onBack, 0,
                onSelect, selectedResourceIds);
        }
        return content;
    }

    private void AddResourceEntryRows(StackPanel destination, IReadOnlyList<ResourceTreeEntry> entries,
        string contextKind, long contextId, Action? onBack, int depth,
        Action<ResourceTreeEntry>? onSelect = null, HashSet<long>? selectedResourceIds = null)
    {
        foreach (var entry in entries)
        {
            var type = (ProfileValue(entry.Data, "type", "resource_type", "document_type",
                "documentType", "template_type", "templateType") ?? "").ToLowerInvariant();
            var isFolder = entry.Kind is "collection" or "folder" ||
                type.Contains("folder", StringComparison.Ordinal) || IsFolderRecord(entry.Data);
            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(8 + depth * 24, 9, 8, 9), MinHeight = 52 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (isFolder || onSelect is not null) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.Children.Add(isFolder ? FolderIcons.CreateIcon(FolderColorName(entry.Data), 27) :
                new FontIcon { Glyph = AttachmentGlyph(entry.Data), FontSize = 23,
                    Foreground = ThemeBrush("SchoologyBlueBrush"), VerticalAlignment = VerticalAlignment.Center });
            var labels = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(Text(entry.Title, 14, true));
            var subtitle = ProfileValue(entry.Data, "description", "body", "resource_description");
            if (!string.IsNullOrWhiteSpace(subtitle)) labels.Children.Add(Text(StripMarkup(subtitle), 12));
            Grid.SetColumn(labels, 1);
            row.Children.Add(labels);

            FontIcon? arrow = null;
            FontIcon? selectionIcon = null;
            if (isFolder)
            {
                arrow = new FontIcon { Glyph = "\uE70D", FontSize = 14,
                    Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                    RenderTransform = new RotateTransform { Angle = 0 } };
                arrow.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
                Grid.SetColumn(arrow, 2);
                row.Children.Add(arrow);
            }
            else if (onSelect is not null)
            {
                selectionIcon = new FontIcon
                {
                    Glyph = selectedResourceIds?.Contains(entry.Id) == true ? "\uE73E" : string.Empty,
                    FontSize = 16,
                    Foreground = ThemeBrush("SchoologyBlueBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetColumn(selectionIcon, 2);
                row.Children.Add(selectionIcon);
            }

            var wrapper = new StackPanel { Spacing = 0 };
            var offset = new TranslateTransform();
            StackPanel? children = null;
            Storyboard? childStoryboard = null;
            var expanded = false;
            var loaded = false;
            var loading = false;

            Storyboard? AnimateChildren(bool show, Action? completed = null)
            {
                if (children is null) return null;
                if (!AnimationsEnabled)
                {
                    children.Opacity = 1; offset.Y = 0;
                    children.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                    completed?.Invoke();
                    return null;
                }
                var fade = new DoubleAnimation { From = show ? 0 : 1, To = show ? 1 : 0,
                    Duration = TimeSpan.FromMilliseconds(160), EnableDependentAnimation = true };
                var slide = new DoubleAnimation { From = show ? -6 : 0, To = show ? 0 : -6,
                    Duration = TimeSpan.FromMilliseconds(180), EnableDependentAnimation = true };
                var storyboard = new Storyboard();
                Storyboard.SetTarget(fade, children); Storyboard.SetTargetProperty(fade, "Opacity");
                Storyboard.SetTarget(slide, offset); Storyboard.SetTargetProperty(slide, "Y");
                storyboard.Children.Add(fade); storyboard.Children.Add(slide);
                storyboard.Completed += (_, _) => completed?.Invoke();
                storyboard.Begin();
                return storyboard;
            }

            void AnimateArrow(bool show)
            {
                if (arrow?.RenderTransform is not RotateTransform transform) return;
                if (!AnimationsEnabled) { transform.Angle = show ? 180 : 0; return; }
                var animation = new DoubleAnimation { To = show ? 180 : 0,
                    Duration = TimeSpan.FromMilliseconds(180), EnableDependentAnimation = true };
                var storyboard = new Storyboard();
                Storyboard.SetTarget(animation, transform); Storyboard.SetTargetProperty(animation, "Angle");
                storyboard.Children.Add(animation); storyboard.Begin();
            }

            async Task LoadChildrenAsync()
            {
                if (children is null || loading) return;
                loading = true;
                children.Children.Clear();
                children.Children.Add(new TextBlock { Text = "Loading resources…",
                    Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                    Margin = new Thickness(24, 8, 8, 8), Foreground = ThemeBrush("TextFillColorSecondaryBrush") });
                try
                {
                    var nested = await LoadResourceChildrenAsync(entry, contextKind, contextId);
                    children.Children.Clear();
                    if (nested.Count == 0)
                        children.Children.Add(new TextBlock { Text = "This folder is empty.",
                            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                            Margin = new Thickness(24, 8, 8, 8), Foreground = ThemeBrush("TextFillColorSecondaryBrush") });
                    else AddResourceEntryRows(children, nested, contextKind, contextId, onBack, depth + 1,
                        onSelect, selectedResourceIds);
                    loaded = true;
                }
                catch (Exception ex)
                {
                    children.Children.Clear();
                    var error = new StackPanel { Spacing = 6, Margin = new Thickness(24, 8, 8, 8) };
                    error.Children.Add(new TextBlock { Text = $"Unable to load this folder: {ex.Message}",
                        Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                        Foreground = ThemeBrush("SystemFillColorCriticalBrush"), TextWrapping = TextWrapping.Wrap });
                    error.Children.Add(Action("Retry", async (_, _) => await LoadChildrenAsync()));
                    children.Children.Add(error);
                }
                finally
                {
                    loading = false;
                    if (expanded && loaded)
                    {
                        children.Opacity = 0; offset.Y = -6; children.Visibility = Visibility.Visible;
                        childStoryboard?.Stop(); childStoryboard = AnimateChildren(true);
                    }
                    else if (!expanded)
                    {
                        children.Visibility = Visibility.Collapsed; children.Opacity = 0; offset.Y = -6;
                    }
                    else { children.Opacity = 1; offset.Y = 0; }
                }
            }

            var item = new ListViewItem { Content = row, Tag = entry,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0) };
            AutomationProperties.SetName(item, entry.Title);
            wrapper.Children.Add(item);
            item.Tapped += async (_, args) =>
            {
                args.Handled = true;
                if (!isFolder)
                {
                    if (onSelect is not null)
                    {
                        onSelect(entry);
                        if (selectionIcon is not null)
                            selectionIcon.Glyph = selectedResourceIds?.Contains(entry.Id) == true ? "\uE73E" : string.Empty;
                        return;
                    }
                    await OpenResourceTreeEntryAsync(entry, onBack);
                    return;
                }
                if (expanded)
                {
                    expanded = false;
                    if (children is not null)
                    {
                        childStoryboard?.Stop();
                        childStoryboard = AnimateChildren(false, () =>
                        { if (!expanded && children is not null) children.Visibility = Visibility.Collapsed; });
                    }
                    AnimateArrow(false);
                    return;
                }
                expanded = true;
                if (children is null)
                {
                    children = new StackPanel { Spacing = 2, Visibility = Visibility.Visible,
                        Opacity = 1, RenderTransform = offset };
                    wrapper.Children.Add(children);
                }
                childStoryboard?.Stop(); childStoryboard = null;
                children.Visibility = Visibility.Visible;
                AnimateArrow(true);
                if (loaded) { childStoryboard = AnimateChildren(true); return; }
                if (loading) { children.Opacity = 1; offset.Y = 0; return; }
                await LoadChildrenAsync();
            };
            destination.Children.Add(wrapper);
        }
    }

    private async Task<IReadOnlyList<ResourceTreeEntry>> LoadResourceChildrenAsync(
        ResourceTreeEntry parent, string contextKind, long contextId)
    {
        var session = _session ?? throw new InvalidOperationException("Sign in to view resources.");
        var collectionId = parent.Kind == "collection" ? parent.Id : parent.CollectionId;
        if (collectionId <= 0) collectionId = JsonLong(parent.Data, "collection_id", "collectionID", "collectionId");
        var folderId = parent.Kind == "collection"
            ? JsonLong(parent.Data, "folder_id", "folderId", "collection_folder_id", "collectionFolderId")
            : parent.Id;
        using var document = collectionId > 0
            ? await _api.GetCollectionResourcesAsync(session, collectionId, folderId)
            : contextKind == "Groups" && contextId > 0
                ? await _api.GetGroupResourcesAsync(session, contextId, folderId)
                : throw new InvalidOperationException("ClassWire did not provide a resource collection for this folder.");
        var records = SchoologyApiClient.ReadRecords(document.RootElement, "resource", "resources", "resource_list", "items");
        return records.Select(record =>
        {
            var type = (ProfileValue(record.Data, "type", "resource_type", "document_type",
                "documentType", "template_type", "templateType") ?? "").ToLowerInvariant();
            var isFolder = type.Contains("folder", StringComparison.Ordinal) || IsFolderRecord(record.Data);
            var id = JsonLong(record.Data, "id", "resource_id", "resourceId", "folder_id", "folderId");
            var title = ProfileValue(record.Data, "resource_title", "resourceTitle", "title", "name",
                "filename", "file_name") ?? record.Title;
            var childCollectionId = JsonLong(record.Data, "collection_id", "collectionID", "collectionId");
            if (childCollectionId <= 0) childCollectionId = collectionId;
            return new ResourceTreeEntry(title, isFolder ? "folder" : "resource", id, childCollectionId, record.Data.Clone());
        }).ToArray();
    }

    private Grid BuildResourcesPage()
    {
        var root = new Grid { Margin = new Thickness(24), RowSpacing = 10 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(Text("Resources", 24, true));
        var host = new Grid();
        Grid.SetRow(host, 1);
        root.Children.Add(host);
        async Task LoadRootAsync()
        {
            host.Children.Clear();
            host.Children.Add(LoadingStatus("Loading resources…"));
            try
            {
                var session = _session ?? throw new InvalidOperationException("Sign in to view resources.");
                using var collectionsDocument = await _api.GetResourceCollectionsAsync(session);
                var apps = new List<SchoologyApiRecord>();
                try
                {
                    using var appsDocument = await _api.GetResourceAppsAsync(session);
                    apps.AddRange(SchoologyApiClient.ReadRecords(appsDocument.RootElement,
                        "resource_apps", "resourceApps", "collection", "collections"));
                }
                catch (Exception ex) { App.WriteStartupLog("ClassWire resource apps could not be loaded; keeping resource collections available.", ex); }
                var all = SchoologyApiClient.ReadRecords(collectionsDocument.RootElement, "collection", "collections", "collection_list");
                var mine = new List<ResourceTreeEntry>();
                var shared = new List<ResourceTreeEntry>();
                var groups = new List<ResourceTreeEntry>();
                foreach (var collection in all)
                {
                    var owner = JsonLong(collection.Data, "collection_owner_id", "collectionOwnerId", "uid", "owner_id");
                    var realm = (ProfileValue(collection.Data, "collection_realm", "collectionRealm", "realm") ?? "").ToLowerInvariant();
                    var sharedCount = JsonLong(collection.Data, "shared_users_count", "sharedUsersCount", "shared_users");
                    var title = ProfileValue(collection.Data, "collection_title", "title", "name") ?? collection.Title;
                    var id = JsonLong(collection.Data, "collection_id", "collectionID", "id");
                    var entry = new ResourceTreeEntry(title, "collection", id, id, collection.Data.Clone());
                    if (string.IsNullOrWhiteSpace(realm) && owner == session.UserId) mine.Add(entry);
                    else if (sharedCount > 0) shared.Add(entry);
                    else if (realm == "group") groups.Add(entry);
                }
                var appEntries = apps.Select(app => new ResourceTreeEntry(
                    ProfileValue(app.Data, "collection_title", "app_title", "appTitle", "title", "name") ?? app.Title,
                    "app", JsonLong(app.Data, "collection_id", "collectionID", "id", "app_id", "appId"),
                    0, app.Data.Clone())).ToArray();
                var categories = new List<(string Title, IReadOnlyList<ResourceTreeEntry> Entries)>
                {
                    ("My Resources", mine), ("Shared", shared), ("Groups", groups), ("Resource Apps", appEntries)
                };
                host.Children.Clear();
                host.Children.Add(new ScrollViewer
                {
                    Content = BuildResourceEntriesView(categories, "Resources", 0, () => ShowHome("Resources")),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Background = new SolidColorBrush(Colors.Transparent)
                });
            }
            catch (Exception ex)
            {
                host.Children.Clear();
                var error = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
                error.Children.Add(EmptyState("Unable to load resources.", ex.Message));
                error.Children.Add(Action("Retry", async (_, _) => await LoadRootAsync()));
                host.Children.Add(error);
            }
        }
        _ = LoadRootAsync();
        return root;
    }

    private async Task LoadResourcesAsync(TreeView tree)
    {
        try
        {
            var session = _session ?? throw new InvalidOperationException("Sign in to view resources.");
            using var collectionsDocument = await _api.GetResourceCollectionsAsync(session);
            var apps = new List<SchoologyApiRecord>();
            try
            {
                using var appsDocument = await _api.GetResourceAppsAsync(session);
                apps.AddRange(SchoologyApiClient.ReadRecords(appsDocument.RootElement,
                    "resource_apps", "resourceApps", "collection", "collections"));
            }
            catch (Exception ex) { App.WriteStartupLog("ClassWire resource apps could not be loaded; keeping resource collections available.", ex); }
            var all = SchoologyApiClient.ReadRecords(collectionsDocument.RootElement, "collection", "collections", "collection_list");
            var uid = session.UserId;
            var mine = new List<SchoologyApiRecord>();
            var shared = new List<SchoologyApiRecord>();
            var groups = new List<SchoologyApiRecord>();
            foreach (var item in all)
            {
                var owner = JsonLong(item.Data, "collection_owner_id", "collectionOwnerId", "uid", "owner_id");
                var realm = (ProfileValue(item.Data, "collection_realm", "collectionRealm", "realm") ?? "").ToLowerInvariant();
                var sharedCount = JsonLong(item.Data, "shared_users_count", "sharedUsersCount", "shared_users");
                if (string.IsNullOrWhiteSpace(realm) && owner == uid) mine.Add(item);
                else if (sharedCount > 0) shared.Add(item);
                else if (realm == "group") groups.Add(item);
            }
            tree.RootNodes.Clear();
            if (mine.Count + shared.Count + groups.Count + apps.Count == 0)
            {
                tree.RootNodes.Add(new TreeViewNode { Content = EmptyState("No resources") });
                return;
            }
            AddCategory("My Resources", mine);
            AddCategory("Shared", shared);
            AddCategory("Groups", groups);
            AddCategory("Resource Apps", apps);

            void AddCategory(string title, IReadOnlyList<SchoologyApiRecord> items)
            {
                var category = new TreeViewNode { Content = title, IsExpanded = false };
                if (items.Count == 0)
                    category.Children.Add(new TreeViewNode { Content = "No resources" });
                foreach (var item in items)
                {
                    var id = JsonLong(item.Data, "collection_id", "collectionID", "id", "app_id", "appId");
                    var entryTitle = ProfileValue(item.Data, "collection_title", "app_title", "appTitle", "title", "name") ?? item.Title;
                    var entry = new ResourceTreeEntry(entryTitle, title == "Resource Apps" ? "app" : "collection", id, 0, item.Data.Clone());
                    var node = new TreeViewNode { Content = entry, HasUnrealizedChildren = entry.Kind == "collection" };
                                        category.Children.Add(node);
                }
                tree.RootNodes.Add(category);
            }
        }
        catch (Exception ex)
        {
            tree.RootNodes.Clear();
            var error = new TreeViewNode { Content = $"Unable to load resources: {ex.Message}" };
            tree.RootNodes.Add(error);
            tree.RootNodes.Add(new TreeViewNode { Content = Action("Retry", async (_, _) => await LoadResourcesAsync(tree)) });
        }
    }

    private async Task LoadResourceTreeChildrenAsync(TreeViewNode node, ResourceTreeEntry entry)
    {
        node.HasUnrealizedChildren = false;
        node.Children.Clear();
        node.Children.Add(new TreeViewNode { Content = LoadingStatus("Loading resources…") });
        try
        {
            if (_session is null)
                throw new InvalidOperationException("Sign in to view this resource collection.");
            var collectionId = entry.Kind == "collection" ? entry.Id : entry.CollectionId;
            if (collectionId <= 0)
                throw new InvalidOperationException("This resource collection is unavailable.");
            var folderId = entry.Kind == "folder" ? entry.Id :
                JsonLong(entry.Data, "folder_id", "folderId", "collection_folder_id", "collectionFolderId");
            using var document = await _api.GetCollectionResourcesAsync(_session, collectionId, folderId);
            var records = SchoologyApiClient.ReadRecords(document.RootElement, "resource", "resources", "resource_list");
            node.Children.Clear();
            if (records.Count == 0)
            {
                node.Children.Add(new TreeViewNode { Content = "No resources" });
                return;
            }
            foreach (var record in records)
            {
                var type = (ProfileValue(record.Data, "type", "resource_type", "document_type", "documentType") ?? "").ToLowerInvariant();
                var isFolder = type.Contains("folder");
                var id = JsonLong(record.Data, "id", "resource_id", "resourceId", "folder_id", "folderId");
                var childTitle = ProfileValue(record.Data, "resource_title", "resourceTitle", "title", "name",
                    "filename", "file_name") ?? record.Title;
                var child = new ResourceTreeEntry(childTitle, isFolder ? "folder" : "resource", id,
                    collectionId, record.Data.Clone());
                var childNode = new TreeViewNode { Content = child, HasUnrealizedChildren = isFolder };
                node.Children.Add(childNode);
            }
        }
        catch (Exception ex)
        {
            node.Children.Clear();
            node.Children.Add(new TreeViewNode { Content = $"Unable to load resources: {ex.Message}" });
            node.HasUnrealizedChildren = true;
        }
    }

    private async Task OpenResourceTreeEntryAsync(ResourceTreeEntry entry, Action? onBack = null)
    {
        try
        {
            if (_session is null) return;
            onBack ??= () => ShowHome("Resources");
            if (entry.Kind == "app")
            {
                if (entry.Id <= 0) throw new InvalidOperationException("This resource app is missing its ID.");
                using var launch = await _api.LaunchResourceAppAsync(_session, entry.Id);
                var launchUrl = ProfileValue(launch.RootElement, "url", "launch_token_url", "launchTokenUrl");
                if (string.IsNullOrWhiteSpace(launchUrl) &&
                    launch.RootElement.TryGetProperty("data", out var data))
                    launchUrl = ProfileValue(data, "url", "launchTokenUrl", "launch_token_url");
                if (string.IsNullOrWhiteSpace(launchUrl))
                    throw new InvalidOperationException("ClassWire did not return a resource-app launch URL.");
                await OpenSharedWebViewAsync(launchUrl, entry.Title, allowLtiExternal: true, onBack: onBack);
                return;
            }

            var type = (ProfileValue(entry.Data, "type", "resource_type", "document_type",
                "documentType", "template_type", "templateType") ?? "").ToLowerInvariant();
            var file = FirstFileAttachment(entry.Data);
            if (file.ValueKind == JsonValueKind.Object)
            {
                await ShowAttachmentOptionsAsync(file, onBack);
                return;
            }
            var url = ProfileValue(entry.Data, "web_url", "webUrl", "launch_url", "launchUrl",
                "url", "link", "download_url", "downloadUrl", "file_download_url", "fileDownloadURL",
                "download_path", "downloadPath", "file_url", "fileUrl", "location");
            if (string.IsNullOrWhiteSpace(url) && entry.Data.ValueKind == JsonValueKind.Object &&
                (entry.Data.TryGetProperty("attachments", out var attachments) ||
                 entry.Data.TryGetProperty("attachment", out attachments)) &&
                attachments.ValueKind == JsonValueKind.Object && attachments.TryGetProperty("links", out var links))
            {
                var link = links.ValueKind == JsonValueKind.Object && links.TryGetProperty("link", out var nestedLink)
                    ? nestedLink : links;
                if (link.ValueKind == JsonValueKind.Array && link.GetArrayLength() > 0) link = link[0];
                url = ProfileValue(link, "url", "link_url", "linkURL", "href");
            }
            var resourceId = JsonLong(entry.Data, "id", "resource_id", "resourceId", "template_id", "templateID");
            if (string.IsNullOrWhiteSpace(url) && (type is "assignment" or "assignment_v2") && resourceId > 0)
                url = $"https://app.schoology.com/assignment/{resourceId}";
            if (string.IsNullOrWhiteSpace(url))
            {
                var details = new StackPanel { Spacing = 8 };
                details.Children.Add(Text(entry.Title, 18, true));
                var description = ProfileValue(entry.Data, "description", "body", "text");
                if (!string.IsNullOrWhiteSpace(description)) details.Children.Add(Text(StripMarkup(description), 13));
                details.Children.Add(Text("ClassWire did not provide a destination for this resource.", 12));
                await new ContentDialog { Title = "Resource details", Content = CreateDialogScrollViewport(details, 560),
                    CloseButtonText = "Close", XamlRoot = _root.XamlRoot,
                    RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme }.ShowAsync();
                return;
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out var target))
                target = new Uri("https://app.schoology.com/" + url.TrimStart('/'));
            if (target.Scheme is not ("https" or "http"))
                throw new InvalidOperationException("ClassWire returned an unsupported resource URL.");
            if (type is "file" or "document" || url.Contains("download", StringComparison.OrdinalIgnoreCase))
                if (await TryOpenDocumentAttachmentAsync(target.AbsoluteUri, onBack)) return;

            var embeddedExternal = type.Contains("lti") || type.Contains("external_tool") ||
                type.Contains("assessment") || type.Contains("quiz") || type.Contains("test") ||
                type.Contains("embed") || type.Contains("scorm");
            if (type is "link" or "video") await OpenExternalLinkAsync(target.AbsoluteUri);
            else await OpenSharedWebViewAsync(target.AbsoluteUri, entry.Title, embeddedExternal, onBack);
        }
        catch (Exception ex) { await ShowApiErrorAsync("Unable to open resource", ex.Message); }
    }

    private void StartAutomaticUpdateChecks()
    {
        if (_automaticUpdateChecksStarted) return;
        _automaticUpdateChecksStarted = true;
        _ = RunAutomaticUpdateChecksAsync();
    }

    private async Task RunAutomaticUpdateChecksAsync()
    {
        try
        {
            var initialDelay = GetInitialUpdateCheckDelay();
            App.WriteStartupLog($"A fresh startup update check is scheduled in {initialDelay.TotalSeconds:0} seconds.");
            if (initialDelay > TimeSpan.Zero) await Task.Delay(initialDelay);
            while (!_allowExit)
            {
                try
                {
                    var update = await CheckForUpdatesAsync();
                    if (update is not null)
                    {
                        App.WriteStartupLog($"A newer ClassWire release is available: {update.Tag}.");
                        if (_isBackgroundHidden) _pendingUpdateInfo = update;
                        else await ShowUpdateAvailableDialogOnUiAsync(update);
                    }
                    else App.WriteStartupLog("Startup/periodic update check completed; no newer ClassWire release is available.");
                    await Task.Delay(UpdateCheckInterval);
                }
                catch (Exception ex)
                {
                    App.WriteStartupLog("Automatic update check failed; it will retry later.", ex);
                    await Task.Delay(UpdateRetryInterval);
                }
            }
        }
        catch (Exception ex)
        {
            App.WriteStartupLog("Automatic update-check loop stopped unexpectedly.", ex);
        }
    }

    private static TimeSpan GetInitialUpdateCheckDelay()
    {
        // Always perform one fresh check after launch. The saved timestamp is
        // useful for diagnostics, but must not suppress the startup check.
        return TimeSpan.FromSeconds(8);
    }

    private static void SaveUpdateCheckState(string tag)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UpdateStatePath)!);
            var state = new UpdateCheckState(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), tag);
            var temporary = UpdateStatePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state));
            File.Move(temporary, UpdateStatePath, overwrite: true);
        }
        catch (Exception ex) { App.WriteStartupLog("Could not save update-check state.", ex); }
    }

    private async Task<UpdateReleaseInfo?> CheckForUpdatesAsync()
    {
        await _updateCheckGate.WaitAsync();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://api.github.com/repos/tling0001/classwire-winui-for-schoology/releases/latest");
            request.Headers.TryAddWithoutValidation("User-Agent", "ClassWire-WinUI-Updater");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await UpdateMetadataClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"GitHub update check failed (HTTP {(int)response.StatusCode}).");

            using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = release.RootElement;
            if ((root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) ||
                (root.TryGetProperty("prerelease", out var prerelease) && prerelease.ValueKind == JsonValueKind.True))
            {
                SaveUpdateCheckState(string.Empty);
                return null;
            }

            var tag = root.TryGetProperty("tag_name", out var tagValue) ? tagValue.GetString() ?? string.Empty : string.Empty;
            var localVersion = typeof(MainWindow).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(MainWindow).Assembly.GetName().Version?.ToString()
                ?? string.Empty;
            App.WriteStartupLog($"Compared latest WinUI release '{tag}' with installed version '{localVersion}'.");
            if (string.IsNullOrWhiteSpace(tag) || !IsNewerReleaseVersion(tag, localVersion))
            {
                SaveUpdateCheckState(tag);
                return null;
            }

            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("The latest GitHub release does not contain downloadable installer assets.");

            var architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "ARM64" : "x64";
            var expectedAssetName = $"ClassWire-WinUI-{tag}-{architecture}-Setup.exe";
            var asset = assets.EnumerateArray().FirstOrDefault(item =>
                item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("name", out var name) &&
                string.Equals(name.GetString(), expectedAssetName, StringComparison.OrdinalIgnoreCase));
            if (asset.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"The latest release does not include the {architecture} installer.");

            var assetUrl = asset.TryGetProperty("browser_download_url", out var urlValue) ? urlValue.GetString() : null;
            if (!Uri.TryCreate(assetUrl, UriKind.Absolute, out var downloadUri) ||
                downloadUri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(downloadUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update download URL is not a valid GitHub HTTPS URL.");

            var size = asset.TryGetProperty("size", out var sizeValue) && sizeValue.TryGetInt64(out var parsedSize)
                ? parsedSize : 0;
            var digest = asset.TryGetProperty("digest", out var digestValue) && digestValue.ValueKind == JsonValueKind.String
                ? digestValue.GetString() : null;
            SaveUpdateCheckState(tag);
            return new UpdateReleaseInfo(tag, tag.TrimStart('v', 'V'), expectedAssetName, downloadUri, size, digest);
        }
        finally { _updateCheckGate.Release(); }
    }

    private static bool IsNewerReleaseVersion(string remoteTag, string localVersion)
    {
        static (Version BaseVersion, int Port)? Parse(string value)
        {
            var normalized = value.Trim().TrimStart('v', 'V').Split('+', 2)[0];
            var match = Regex.Match(normalized,
                @"^(?<base>\d+(?:\.\d+){2})-port\.(?<port>\d+)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success || !Version.TryParse(match.Groups["base"].Value, out var baseVersion) ||
                !int.TryParse(match.Groups["port"].Value, out var port)) return null;
            return (baseVersion, port);
        }

        var remote = Parse(remoteTag);
        var local = Parse(localVersion);
        if (remote is null || local is null)
            throw new InvalidDataException($"Could not compare ClassWire release versions ('{localVersion}' and '{remoteTag}').");
        var baseComparison = remote.Value.BaseVersion.CompareTo(local.Value.BaseVersion);
        return baseComparison > 0 || (baseComparison == 0 && remote.Value.Port > local.Value.Port);
    }

    private async Task CheckForUpdatesManuallyAsync(Button button)
    {
        if (!button.IsEnabled) return;
        var previousContent = button.Content;
        button.IsEnabled = false;
        button.Content = "Checking…";
        try
        {
            var update = await CheckForUpdatesAsync();
            if (update is null)
                await ShowApiErrorAsync("Up to date", "You are using the latest version of ClassWire.");
            else
                await ShowUpdateAvailableDialogAsync(update);
        }
        catch (Exception ex)
        {
            App.WriteStartupLog("Manual update check failed.", ex);
            await ShowApiErrorAsync("Unable to check for updates", ex.Message);
        }
        finally
        {
            button.Content = previousContent;
            button.IsEnabled = true;
        }
    }

    private async Task ShowUpdateAvailableDialogOnUiAsync(UpdateReleaseInfo update)
    {
        var dispatcher = _root.DispatcherQueue;
        if (dispatcher.HasThreadAccess)
        {
            await ShowUpdateAvailableDialogAsync(update);
            return;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(async () =>
        {
            try
            {
                await ShowUpdateAvailableDialogAsync(update);
                completion.TrySetResult(true);
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        }))
            throw new InvalidOperationException("The Windows UI dispatcher is unavailable for the update confirmation dialog.");
        await completion.Task;
    }

    private async Task ShowUpdateAvailableDialogAsync(UpdateReleaseInfo update)
    {
        if (_isBackgroundHidden)
        {
            _pendingUpdateInfo = update;
            return;
        }
        if (_updateDialogOpen) return;

        _updateDialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = "Update available",
                Content = $"ClassWire {update.Version} is available. The update will download only after you choose Install update.",
                PrimaryButtonText = "Install update",
                CloseButtonText = "Later",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = _root.XamlRoot,
                RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
            };
            var result = await dialog.ShowAsync();
            App.WriteStartupLog(result == ContentDialogResult.Primary
                ? $"User confirmed installation of ClassWire {update.Version}."
                : $"User deferred installation of ClassWire {update.Version}.");
            if (result == ContentDialogResult.Primary)
                await DownloadAndInstallUpdateAsync(update);
        }
        finally { _updateDialogOpen = false; }
    }

    private async Task DownloadAndInstallUpdateAsync(UpdateReleaseInfo update)
    {
        var status = Text("Downloading update…", 13);
        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            IsIndeterminate = update.Size <= 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var content = new StackPanel { Spacing = 12, MinWidth = 320 };
        content.Children.Add(status);
        content.Children.Add(progressBar);
        var progressDialog = new ContentDialog
        {
            Title = $"Downloading ClassWire {update.Version}",
            Content = content,
            XamlRoot = _root.XamlRoot,
            RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
        };
        var dialogOperation = progressDialog.ShowAsync();
        try
        {
            var progress = new Progress<double>(percent =>
            {
                progressBar.IsIndeterminate = false;
                progressBar.Value = Math.Clamp(percent, 0, 100);
                SetStatusText(status, $"Downloading update… {Math.Round(percent):0}%");
            });
            var installerPath = await DownloadAndVerifyInstallerAsync(update, progress);
            progressDialog.Hide();
            await dialogOperation;

            StartInstallerAfterTaskkill(installerPath,
                "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS /RESTARTAPP=1");
            App.WriteStartupLog($"Scheduled silent ClassWire update to {update.Version} using {update.AssetName}; the handoff helper will run taskkill for ClassWire.exe before starting Inno Setup.");
            _allowExit = true;
            Close();
        }
        catch (Exception ex)
        {
            try { progressDialog.Hide(); } catch { }
            try { await dialogOperation; } catch { }
            App.WriteStartupLog("Could not download or start the ClassWire update.", ex);
            await ShowApiErrorAsync("Unable to install update", ex.Message);
        }
    }

    private static void StartInstallerAfterTaskkill(
        string installerPath, string installerArguments,
        string? uninstallerPath = null, string? uninstallerArguments = null)
    {
        if (!File.Exists(installerPath))
            throw new FileNotFoundException("The installer file is missing.", installerPath);

        static string Encode(string value) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

        var targetPid = Environment.ProcessId;
        var installerPathBase64 = Encode(Path.GetFullPath(installerPath));
        var installerArgumentsBase64 = Encode(installerArguments);
        var uninstallerPathBase64 = string.IsNullOrWhiteSpace(uninstallerPath)
            ? string.Empty
            : Encode(Path.GetFullPath(uninstallerPath));
        var uninstallerArgumentsBase64 = string.IsNullOrWhiteSpace(uninstallerArguments)
            ? string.Empty
            : Encode(uninstallerArguments);

        // Start the helper through the Windows shell so it is independent of the
        // WinUI process lifetime. It waits for this exact PID to exit before
        // touching installed files; it never kills other Schoology instances.
        var script = $@"
$ErrorActionPreference = 'Stop';
$TargetPid = {targetPid};
$InstallerPath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{installerPathBase64}'));
$InstallerArguments = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{installerArgumentsBase64}'));
$UninstallerPathBase64 = '{uninstallerPathBase64}';
$UninstallerArgumentsBase64 = '{uninstallerArgumentsBase64}';
$UninstallerPath = if ($UninstallerPathBase64) {{ [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($UninstallerPathBase64)) }} else {{ '' }};
$UninstallerArguments = if ($UninstallerArgumentsBase64) {{ [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($UninstallerArgumentsBase64)) }} else {{ '' }};
$LogPath = Join-Path $env:TEMP ('Schoology-InstallerHandoff-' + $TargetPid + '.log');
try {{
    Add-Content -LiteralPath $LogPath -Value ('Handoff helper started at ' + (Get-Date -Format o) + '; waiting for WinUI PID ' + $TargetPid);
    $AppProcess = Get-Process -Id $TargetPid -ErrorAction SilentlyContinue;
    if ($AppProcess) {{
        try {{
            Wait-Process -Id $TargetPid -Timeout 90 -ErrorAction Stop;
            Add-Content -LiteralPath $LogPath -Value ('WinUI process exited normally at ' + (Get-Date -Format o));
        }} catch {{
            $StillRunning = Get-Process -Id $TargetPid -ErrorAction SilentlyContinue;
            if ($StillRunning) {{
                Add-Content -LiteralPath $LogPath -Value ('WinUI did not exit within 90 seconds; stopping only PID ' + $TargetPid);
                Stop-Process -Id $TargetPid -Force -ErrorAction Stop;
                Wait-Process -Id $TargetPid -Timeout 15 -ErrorAction SilentlyContinue;
            }}
        }}
    }};
    if (Get-Process -Id $TargetPid -ErrorAction SilentlyContinue) {{ throw ('Schoology.WinUI PID ' + $TargetPid + ' is still running; installer launch was cancelled.') }};
    Start-Sleep -Seconds 2;
    if (!(Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {{ throw ('Installer file was not found: ' + $InstallerPath) }};
    if ($UninstallerPath) {{
        if (!(Test-Path -LiteralPath $UninstallerPath -PathType Leaf)) {{ throw ('WinUI uninstaller was not found: ' + $UninstallerPath) }};
        Add-Content -LiteralPath $LogPath -Value ('Starting WinUI uninstaller at ' + (Get-Date -Format o));
        $Uninstall = Start-Process -FilePath $UninstallerPath -ArgumentList $UninstallerArguments -Wait -PassThru;
        if ($Uninstall.ExitCode -ne 0) {{ throw ('WinUI uninstaller exited with code ' + $Uninstall.ExitCode) }};
    }};
    Add-Content -LiteralPath $LogPath -Value ('Starting installer at ' + (Get-Date -Format o) + ': ' + $InstallerPath);
    $Install = Start-Process -FilePath $InstallerPath -ArgumentList $InstallerArguments -Wait -PassThru;
    if ($Install.ExitCode -ne 0) {{ throw ('Installer exited with code ' + $Install.ExitCode) }};
    Add-Content -LiteralPath $LogPath -Value ('Installer completed successfully at ' + (Get-Date -Format o));
    Remove-Item -LiteralPath $InstallerPath -Force -ErrorAction SilentlyContinue;
}} catch {{
    Add-Content -LiteralPath $LogPath -Value ('Installer handoff failed at ' + (Get-Date -Format o) + ': ' + $_.Exception.Message);
    exit 1;
}}";
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-WindowStyle");
        start.ArgumentList.Add("Hidden");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(encodedScript);

        using var helper = Process.Start(start);
        if (helper is null)
            throw new InvalidOperationException("Windows could not start the detached PowerShell installer handoff.");
        App.WriteStartupLog(
            $"Started detached PowerShell installer handoff PID {helper.Id}; it will wait for WinUI PID {targetPid} to exit before running the installer. Handoff log: %TEMP%\\ClassWire-InstallerHandoff-{targetPid}.log");
    }

    private async Task TrySchoologyClassicAsync(Button button)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            await ShowApiErrorAsync("Schoology classic unavailable",
                "The supplied classic installer is x64. This action is only available when running the x64 WinUI app.");
            return;
        }
        var confirm = new ContentDialog
        {
            Title = "Switch to Schoology classic?",
            Content = Text("This downloads the latest classic Schoology installer, closes ClassWire, silently uninstalls the WinUI version, and installs classic Schoology. Your Schoology account and local documents are not intentionally removed.", 13),
            PrimaryButtonText = "Download and switch",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _root.XamlRoot,
            RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        button.IsEnabled = false;
        var status = Text("Downloading the classic installer…", 13);
        var progress = new ProgressBar { IsIndeterminate = true, Minimum = 0, Maximum = 100 };
        var content = new StackPanel { Spacing = 10, MinWidth = 320 };
        content.Children.Add(status);
        content.Children.Add(progress);
        var dialog = new ContentDialog { Title = "Try Schoology classic", Content = CreateDialogScrollViewport(content), XamlRoot = _root.XamlRoot, RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme };
        var operation = dialog.ShowAsync();
        try
        {
            var downloadUri = await GetLatestClassicInstallerUriAsync();
            var installerPath = await DownloadClassicInstallerAsync(downloadUri);
            dialog.Hide();
            await operation;
            var programDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Schoology");
            var uninstaller = Path.Combine(programDirectory, "unins000.exe");
            if (!File.Exists(uninstaller))
                throw new FileNotFoundException("The ClassWire WinUI uninstaller was not found in the current per-user install directory.", uninstaller);
            StartInstallerAfterTaskkill(installerPath, "/S", uninstaller,
                "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS");
            App.WriteStartupLog("Scheduled migration from ClassWire to classic Schoology. The helper terminates ClassWire.exe, silently uninstalls it, then starts the classic installer.");
            _allowExit = true;
            Close();
        }
        catch (Exception ex)
        {
            try { dialog.Hide(); } catch { }
            try { await operation; } catch { }
            button.IsEnabled = true;
            App.WriteStartupLog("Could not prepare the Schoology classic migration.", ex);
            await ShowApiErrorAsync("Unable to switch to Schoology classic", ex.Message);
        }
    }

    private static async Task<Uri> GetLatestClassicInstallerUriAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.github.com/repos/tling0001/schoology-for-desktop-port/releases/latest");
        request.Headers.TryAddWithoutValidation("User-Agent", "ClassWire-WinUI-Classic-Migration");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await UpdateMetadataClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Could not retrieve the latest classic release (HTTP {(int)response.StatusCode}).");

        using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = release.RootElement;
        if (JsonBoolean(root, "draft") || JsonBoolean(root, "prerelease"))
            throw new InvalidDataException("The latest classic Schoology release is not a stable release.");
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The latest classic Schoology release does not contain downloadable assets.");

        var asset = assets.EnumerateArray().FirstOrDefault(item =>
            item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty("name", out var name) &&
            name.ValueKind == JsonValueKind.String &&
            name.GetString()!.StartsWith("ClassWire_", StringComparison.OrdinalIgnoreCase) &&
            name.GetString()!.EndsWith("_x64-setup.exe", StringComparison.OrdinalIgnoreCase));
        if (asset.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The latest classic release does not include the ClassWire x64 Windows installer.");

        var assetName = asset.GetProperty("name").GetString() ?? string.Empty;
        var download = asset.TryGetProperty("browser_download_url", out var downloadValue)
            ? downloadValue.GetString() : null;
        if (!Uri.TryCreate(download, UriKind.Absolute, out var downloadUri) ||
            downloadUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(downloadUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The latest classic installer URL is not a trusted GitHub HTTPS URL.");

        var tag = root.TryGetProperty("tag_name", out var tagValue) ? tagValue.GetString() : null;
        App.WriteStartupLog($"Resolved classic Schoology installer '{assetName}' from release '{tag}'.");
        return downloadUri;
    }

    private static async Task<string> DownloadClassicInstallerAsync(Uri downloadUri)
    {
        if (!IsAllowedGitHubDownloadHost(downloadUri))
            throw new InvalidDataException("The classic installer must be downloaded from GitHub over HTTPS.");
        var directory = Path.Combine(Path.GetTempPath(), "Schoology", "ClassicMigration");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "ClassWire-classic-x64-setup.exe");
        var temporary = target + ".download";
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);
        request.Headers.TryAddWithoutValidation("User-Agent", "ClassWire-WinUI-Classic-Migration");
        using var response = await UpdateDownloadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Classic installer download failed (HTTP {(int)response.StatusCode}).");
        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null || !IsAllowedGitHubDownloadHost(finalUri))
            throw new InvalidDataException("The classic installer redirected to an untrusted host.");
        var length = response.Content.Headers.ContentLength;
        if (length is > 500 * 1024 * 1024)
            throw new InvalidDataException("The classic installer exceeds the 500 MB safety limit.");
        try
        {
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            await using var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            var buffer = new byte[81920];
            var received = 0L;
            while (true)
            {
                var count = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token);
                if (count == 0) break;
                received += count;
                if (received > 500L * 1024 * 1024) throw new InvalidDataException("The classic installer exceeds the 500 MB safety limit.");
                await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
            }
            await output.FlushAsync(timeout.Token);
            if (received < 1024 * 1024 || length is > 0 && received != length.Value)
                throw new InvalidDataException("The downloaded classic installer is incomplete.");
        }
        catch
        {
            try { File.Delete(temporary); } catch { }
            throw;
        }
        var header = new byte[2];
        await using (var check = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (await check.ReadAsync(header.AsMemory(0, 2), timeout.Token) != 2 ||
                header[0] != (byte)'M' || header[1] != (byte)'Z')
            {
                File.Delete(temporary);
                throw new InvalidDataException("The downloaded classic installer is not a valid Windows executable.");
            }
        }
        File.Move(temporary, target, overwrite: true);
        return target;
    }

    private static bool IsAllowedGitHubDownloadHost(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        (string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(uri.Host, "objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(uri.Host, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    private static async Task<string> DownloadAndVerifyInstallerAsync(UpdateReleaseInfo update, IProgress<double>? progress)
    {
        if (!IsAllowedGitHubDownloadHost(update.DownloadUri))
            throw new InvalidDataException("Updates must be downloaded from GitHub over HTTPS.");

        var directory = Path.Combine(Path.GetTempPath(), "Schoology", "Updates");
        Directory.CreateDirectory(directory);
        var safeName = Path.GetFileName(update.AssetName);
        if (string.IsNullOrWhiteSpace(safeName) || !string.Equals(safeName, update.AssetName, StringComparison.Ordinal))
            throw new InvalidDataException("The update installer filename is invalid.");
        var target = Path.Combine(directory, safeName);
        var temporary = target + ".download";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUri);
            request.Headers.TryAddWithoutValidation("User-Agent", "ClassWire-WinUI-Updater");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            using var response = await UpdateDownloadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Update download failed (HTTP {(int)response.StatusCode}).");
            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri is null || !IsAllowedGitHubDownloadHost(finalUri))
                throw new InvalidDataException("The update download redirected to an untrusted host.");

            var total = update.Size > 0 ? update.Size : response.Content.Headers.ContentLength ?? 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            {
                await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                await using var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                var buffer = new byte[81920];
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token);
                    if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                    hash.AppendData(buffer, 0, read);
                    received += read;
                    if (total > 0) progress?.Report(Math.Min(100, received * 100d / total));
                }
                await output.FlushAsync(timeout.Token);
            }

            if (total > 0 && received != total)
                throw new InvalidDataException($"The downloaded installer was incomplete ({received}/{total} bytes).");
            if (!string.IsNullOrWhiteSpace(update.Digest) && update.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                var expected = update.Digest["sha256:".Length..].Trim();
                var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded installer failed its SHA-256 integrity check.");
            }

            File.Move(temporary, target, overwrite: true);
            progress?.Report(100);
            return target;
        }
        catch
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            throw;
        }
    }

    private ScrollViewer BuildSettingsPage()
    {
        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(24), MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(Text("Settings", 24, true));
        var appearance = new StackPanel { Spacing = 8 };
        appearance.Children.Add(Text("Appearance", 18, true));
        appearance.Children.Add(Text("App theme", 13, true));
        var theme = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var value in new[] { "System default", "Light", "Dark" }) theme.Items.Add(value);
        var saved = ReadAppSetting("AppTheme", "System default");
        theme.SelectedItem = saved;
        theme.SelectionChanged += (_, _) =>
        {
            var value = theme.SelectedItem?.ToString() ?? "System default";
            WriteAppSetting("AppTheme", value);
            _root.RequestedTheme = value switch { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        };
        appearance.Children.Add(theme);
        panel.Children.Add(Surface(appearance, new Thickness(14)));
        var behavior = new StackPanel { Spacing = 8 };
        behavior.Children.Add(Text("Behavior", 18, true));
        var desktopNotifications = new CheckBox { Content = "Show ClassWire desktop notifications",
            IsChecked = !string.Equals(ReadAppSetting("DesktopNotifications", "true"), "false", StringComparison.OrdinalIgnoreCase) };
        desktopNotifications.Checked += (_, _) => { WriteAppSetting("DesktopNotifications", "true"); EnsureBackgroundNotificationPolling(); };
        desktopNotifications.Unchecked += (_, _) => { WriteAppSetting("DesktopNotifications", "false"); _notificationTimer?.Stop(); };
        behavior.Children.Add(desktopNotifications);
        var notificationSound = new CheckBox { Content = "Play notification sounds",
            IsChecked = string.Equals(ReadAppSetting("NotificationSound", "false"), "true", StringComparison.OrdinalIgnoreCase) };
        notificationSound.Checked += (_, _) => WriteAppSetting("NotificationSound", "true");
        notificationSound.Unchecked += (_, _) => WriteAppSetting("NotificationSound", "false");
        behavior.Children.Add(notificationSound);
        var startupBackground = new CheckBox { Content = "Run ClassWire in the background at Windows startup", IsChecked = BackgroundModeEnabled };
        startupBackground.Checked += (_, _) => { WriteAppSetting("RunInBackgroundAtStartup", "true"); ConfigureStartupRegistration(); };
        startupBackground.Unchecked += (_, _) => { WriteAppSetting("RunInBackgroundAtStartup", "false"); ConfigureStartupRegistration(); };
        behavior.Children.Add(startupBackground);
        var reduceMotion = new CheckBox { Content = "Reduce motion",
            IsChecked = string.Equals(ReadAppSetting("ReduceMotion", "false"), "true", StringComparison.OrdinalIgnoreCase) };
        reduceMotion.Checked += (_, _) => WriteAppSetting("ReduceMotion", "true");
        reduceMotion.Unchecked += (_, _) => WriteAppSetting("ReduceMotion", "false");
        behavior.Children.Add(reduceMotion);
        panel.Children.Add(Surface(behavior, new Thickness(14)));

        var updates = new StackPanel { Spacing = 8 };
        updates.Children.Add(Text("Updates", 18, true));
        updates.Children.Add(Text("Check for the latest ClassWire version. Updates download only after you confirm installation.", 13));
        var checkUpdates = new Button
        {
            Content = "Check for updates",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        checkUpdates.Click += async (_, _) => await CheckForUpdatesManuallyAsync(checkUpdates);
        AutomationProperties.SetAutomationId(checkUpdates, "CheckForUpdatesButton");
        AutomationProperties.SetName(checkUpdates, "Check for updates");
        updates.Children.Add(checkUpdates);
        var tryClassic = new Button
        {
            Content = "Try Schoology classic",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(14, 9, 14, 9),
            IsEnabled = RuntimeInformation.ProcessArchitecture == Architecture.X64
        };
        tryClassic.Click += async (_, _) => await TrySchoologyClassicAsync(tryClassic);
        AutomationProperties.SetAutomationId(tryClassic, "TrySchoologyClassicButton");
        AutomationProperties.SetName(tryClassic, "Download and install Schoology classic instead of ClassWire");
        updates.Children.Add(tryClassic);
        updates.Children.Add(Text("Switches from the WinUI port to the classic desktop app. The WinUI version will be silently uninstalled first.", 12));
        panel.Children.Add(Surface(updates, new Thickness(14)));

        panel.Children.Add(Text("About", 18, true));
        panel.Children.Add(Surface(Text($"ClassWire · {GetType().Assembly.GetName().Version}", 13)));
        panel.Children.Add(Action("Open ClassWire website", (_, _) =>
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.schoology.com"))));
        panel.Children.Add(Action("Account Info", async (_, _) =>
            await OpenSharedWebViewAsync("https://app.schoology.com/settings/account", "Account Info",
                onBack: () => ShowHome("Settings"))));
        var host = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        host.Children.Add(panel);
        host.SizeChanged += (_, args) =>
            panel.Width = Math.Max(220, Math.Min(760, args.NewSize.Width - 48));
        return new ScrollViewer
        {
            Content = host,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private StackPanel ActivityItem(string course, string title, string meta)
    {
        var stack = new StackPanel { Spacing = 5 };
        stack.Children.Add(Text(course.ToUpperInvariant(), 11, true));
        stack.Children.Add(Text(title, 16, true));
        stack.Children.Add(Text(meta, 12));
        return stack;
    }

    private Grid CourseRow(string name)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tile = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(7), Background = Brush("#2E66A3"),
            Child = new TextBlock { Text = name[..1], FontSize = 21, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = Brush("#FFFFFF"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        grid.Children.Add(tile);
        var labels = new StackPanel { Spacing = 3 };
        labels.Children.Add(Text(name, 14, true));
        labels.Children.Add(Text("Course", 12));
        Grid.SetColumn(labels, 1);
        grid.Children.Add(labels);
        return grid;
    }

    private void ShowUpcoming() => ShowHome("Upcoming");

    private void SyncEntityNavigationSelection(string kind, SchoologyEntity? entity)
    {
        if (_appNavigation is null || entity is null || kind is not ("Courses" or "Groups")) return;
        var parent = _appNavigation.MenuItems.OfType<NavigationViewItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, kind, StringComparison.Ordinal));
        if (parent is null) return;
        parent.IsExpanded = true;
        var prefix = kind == "Groups" ? "group:" : "course:";
        var child = parent.MenuItems.OfType<NavigationViewItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, prefix + entity.Id, StringComparison.Ordinal));
        if (child is null) return;
        _syncingNavigationSelection = true;
        try { _appNavigation.SelectedItem = child; }
        finally { _syncingNavigationSelection = false; }
    }

    private void ShowThreePane(string kind, SchoologyEntity? initialEntity = null, string? initialTab = null)
    {
        var previousPage = _page;
        _page = kind;
        _section = kind;
        if (initialEntity is not null)
        {
            if (_selectedEntityId != initialEntity.Id || previousPage != kind) _selectedEntityTab = string.Empty;
            _selectedEntity = initialEntity.Name;
            _selectedEntityId = initialEntity.Id;
            SyncEntityNavigationSelection(kind, initialEntity);
            if (!string.IsNullOrWhiteSpace(initialTab)) _selectedEntityTab = initialTab;
        }
        else { _selectedEntity = string.Empty; _selectedEntityId = 0; _selectedEntityTab = string.Empty; }
        UpdateTitleBarCreateButton(kind, _selectedEntityTab);
        var page = BuildThreePane(kind, initialEntity);
        if (_sectionFrame is not null)
            NavigateMainSection(page, kind);
        else
            Reset(BuildApplicationShell(kind, page));
    }

    private SchoologyEntity[] GetSchoolProfileEntities()
    {
        if (_session is null) return Array.Empty<SchoologyEntity>();
        var schoolId = ProfileValue(_session.User, "school_id", "schoolId");
        if (!long.TryParse(schoolId, out var id) || id <= 0) return Array.Empty<SchoologyEntity>();
        var schoolName = ProfileValue(_session.User, "school_title", "school_name", "school") ?? "My School";
        return new[] { new SchoologyEntity(id, schoolName, string.Empty, null, _session.User.Clone()) };
    }

    private Grid BuildThreePane(string kind, SchoologyEntity? initialEntity = null)
    {
        IEnumerable<SchoologyEntity> entries = kind == "Courses" ? _courses :
            kind == "Groups" ? _groups : GetSchoolProfileEntities();
        var selected = initialEntity ?? entries.FirstOrDefault();
        var entityId = selected?.Id ?? 0;
        var entityName = selected?.Name ?? (kind == "School Profile" ? "School" : kind);
        _selectedEntity = entityName;
        _selectedEntityId = entityId;
        var tabs = kind == "School Profile" ? new[] { "Updates", "Info", "Resources", "Discussions" } :
            kind == "Groups" ? new[] { "Updates", "Members", "Discussions", "Resources" } :
            new[] { "Materials", "Updates", "Grades", "Members" };

        var root = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
        var wide = new Grid { ColumnSpacing = 0 };
        wide.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        wide.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        wide.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

        // The left structural pane holds the course/group/school infobox and its app navigation.
        var sidebar = new Grid { Background = InAppAcrylicBrush() };
        sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sidebar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var identity = new StackPanel { Spacing = 8, Margin = new Thickness(14) };
        var hero = new Border { Height = 124, CornerRadius = new CornerRadius(8),
            Background = ThemeBrush("SchoologyBlueDarkBrush") };
        if (!string.IsNullOrWhiteSpace(selected?.ImageUrl))
        {
            try { hero.Child = new Image { Source = new BitmapImage(new Uri(NormalizeSchoologyImageUrl(selected.ImageUrl))),
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill }; } catch { }
        }
        // Landscape keeps the left rail image-only. Course/group/school details
        // belong in the infobox above the selected tab's content.
        identity.Children.Add(hero);
        var infobox = new StackPanel { Spacing = 4 };
        infobox.Children.Add(Text(entityName, 17, true));
        if (!string.IsNullOrWhiteSpace(selected?.Subtitle)) infobox.Children.Add(Text(selected.Subtitle, 12));
        if (kind == "Courses" && selected is not null)
        {
            var schoolLink = BuildCourseSchoolLink(selected);
            if (schoolLink is not null) infobox.Children.Add(schoolLink);
        }
        identity.Children.Add(Surface(infobox, new Thickness(10)));
        sidebar.Children.Add(identity);

        var detail = new Frame { Background = new SolidColorBrush(Colors.Transparent) };
        var compactFrame = new Frame { Background = new SolidColorBrush(Colors.Transparent) };
        var navigation = new ListView { SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = false, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent) };
        AutomationProperties.SetAutomationId(navigation, kind + "ProfileNavigation");
        // Declare caches before wiring item events: the handlers capture these locals.
        var wideTabCache = new Dictionary<string, UIElement>(StringComparer.Ordinal);
        var compactTabCache = new Dictionary<string, UIElement>(StringComparer.Ordinal);
        foreach (var tab in tabs)
        {
            var tabRow = new Grid { ColumnSpacing = 12 };
            tabRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            tabRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            IconElement tabIcon = tab switch
            {
                "Materials" => new FontIcon { Glyph = ((char)0xE8B7).ToString(), FontSize = 20 },
                "Updates" => new FontIcon { Glyph = ((char)0xE8BD).ToString(), FontSize = 20 },
                "Grades" => new FontIcon { Glyph = ((char)0xE9D5).ToString(), FontSize = 20 },
                "Members" => new SymbolIcon(Symbol.People),
                "Discussions" => new FontIcon { Glyph = ((char)0xE8BD).ToString(), FontSize = 20 },
                "Resources" => new FontIcon { Glyph = ((char)0xE8B7).ToString(), FontSize = 20 },
                "Info" => new SymbolIcon(Symbol.Help),
                _ => new SymbolIcon(Symbol.Page)
            };
            tabIcon.Width = 22;
            tabIcon.Height = 22;
            tabIcon.Foreground = ThemeBrush("SchoologyBlueBrush");
            tabIcon.VerticalAlignment = VerticalAlignment.Center;
            tabRow.Children.Add(tabIcon);
            var tabLabel = Text(tab, 14, true);
            tabLabel.Foreground = ThemeBrush("SchoologyTextBrush");
            Grid.SetColumn(tabLabel, 1);
            tabRow.Children.Add(tabLabel);
            var tabItem = new ListViewItem
            {
                Content = tabRow, Tag = tab,
                Padding = new Thickness(14, 10, 14, 10)
            };
            tabItem.Tapped += (_, _) => ShowTab(tab);
            navigation.Items.Add(tabItem);
        }
        Grid.SetRow(navigation, 1);
        sidebar.Children.Add(navigation);
        var upcoming = new Grid { RowSpacing = 8, Padding = new Thickness(14),
            Background = InAppAcrylicBrush() };
        upcoming.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        upcoming.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        upcoming.Children.Add(Text("Upcoming", 19, true));
        var upcomingFrame = new Frame { Background = new SolidColorBrush(Colors.Transparent) };
        Grid.SetRow(upcomingFrame, 1);
        upcoming.Children.Add(upcomingFrame);
        Grid.SetColumn(sidebar, 0); Grid.SetColumn(detail, 1); Grid.SetColumn(upcoming, 2);
        wide.Children.Add(sidebar); wide.Children.Add(detail); wide.Children.Add(upcoming);

        // In narrow/portrait windows, the infobox remains visible and destinations become SelectorBar tabs.
        var narrow = new Grid { RowSpacing = 8, Padding = new Thickness(12), Visibility = Visibility.Collapsed };
        narrow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        narrow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        narrow.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var compactIdentity = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 0, 0, 4) };
        compactIdentity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        compactIdentity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var compactImage = new Border { Width = 68, Height = 68, CornerRadius = new CornerRadius(6),
            Background = ThemeBrush("SchoologyBlueDarkBrush") };
        if (!string.IsNullOrWhiteSpace(selected?.ImageUrl))
        {
            try { compactImage.Child = new Image { Source = new BitmapImage(new Uri(NormalizeSchoologyImageUrl(selected.ImageUrl))),
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill }; } catch { }
        }
        compactIdentity.Children.Add(compactImage);
        var compactLabels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        compactLabels.Children.Add(Text(entityName, 18, true));
        if (kind == "Courses" && !string.IsNullOrWhiteSpace(selected?.Subtitle))
            compactLabels.Children.Add(Text(selected.Subtitle, 12));
        if (kind == "Courses" && selected is not null)
        {
            var schoolLink = BuildCourseSchoolLink(selected);
            if (schoolLink is not null) compactLabels.Children.Add(schoolLink);
        }
        Grid.SetColumn(compactLabels, 1); compactIdentity.Children.Add(compactLabels);
        var compactSelector = new SelectorBar { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var tab in tabs.Concat(["Upcoming"]))
            compactSelector.Items.Add(new SelectorBarItem { Text = tab });
        // In portrait the tab selector precedes the entity infobox.
        Grid.SetRow(compactSelector, 0); narrow.Children.Add(compactSelector);
        Grid.SetRow(compactIdentity, 1); narrow.Children.Add(compactIdentity);
        Grid.SetRow(compactFrame, 2); narrow.Children.Add(compactFrame);

        UIElement BuildSelectedTabContent(string tab) => tab switch
        {
            "Upcoming" => BuildEntityUpcomingContent(kind, entityId),
            "Course Apps" when kind == "Courses" => BuildCourseAppsContent(entityId, selected!),
            _ => BuildEntityTabContent(kind, entityId, entityName, tab, selected, showInfobox: false)
        };
        void ShowTab(string tab)
        {
            if (entityId <= 0) return;
            _selectedEntityTab = tab;
            UpdateTitleBarCreateButton(kind, tab);
            NavigateShellFrame(detail, GetOrCreateTemporaryView(wideTabCache, tab, () => BuildSelectedTabContent(tab)),
                SlideNavigationTransitionEffect.FromRight);
            NavigateShellFrame(compactFrame, GetOrCreateTemporaryView(compactTabCache, tab, () => BuildSelectedTabContent(tab)),
                SlideNavigationTransitionEffect.FromRight);
        }

        compactSelector.SelectionChanged += (_, _) =>
        {
            if (compactSelector.SelectedItem is SelectorBarItem item) ShowTab(item.Text);
        };
        if (kind == "Courses" && entityId > 0 && selected is not null)
            _ = LoadCourseAppsIntoSidebarAsync(entityId, navigation, selected, compactSelector);
        NavigateShellFrame(upcomingFrame, BuildEntityUpcomingContent(kind, entityId), SlideNavigationTransitionEffect.FromRight);
        if (entityId > 0)
        {
            var initialTab = tabs.Contains(_selectedEntityTab, StringComparer.Ordinal) ? _selectedEntityTab : tabs[0];
            navigation.SelectedIndex = Math.Max(0, Array.IndexOf(tabs, initialTab));
            ShowTab(initialTab);
            compactSelector.SelectedItem = compactSelector.Items
                .OfType<SelectorBarItem>()
                .FirstOrDefault(item => item.Text == initialTab);
        }
        root.Children.Add(wide);
        root.Children.Add(narrow);
        root.SizeChanged += (_, args) =>
        {
            var useNarrow = args.NewSize.Width < 900;
            wide.Visibility = useNarrow ? Visibility.Collapsed : Visibility.Visible;
            narrow.Visibility = useNarrow ? Visibility.Visible : Visibility.Collapsed;
        };
        return root;
    }

    private Grid BuildEntityUpcomingContent(string kind, long entityId)
    {
        var root = new Grid { RowSpacing = 8 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var statusHost = LoadingStatus("Loading upcoming items for this " +
            (kind == "Groups" ? "group" : kind == "School Profile" ? "school" : "course") + "…", out var status);
        status.Foreground = ThemeBrush("SchoologyMutedTextBrush");
        root.Children.Add(statusHost);
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent)
        };
        AutomationProperties.SetAutomationId(list, kind + "UpcomingList");
        Grid.SetRow(list, 1);
        root.Children.Add(list);
        _ = LoadEntityUpcomingAsync(kind, entityId, list, status);
        return root;
    }

    private async Task LoadEntityUpcomingAsync(string kind, long entityId, ListView list, TextBlock status)
    {
        try
        {
            var session = _session ?? throw new InvalidOperationException("Sign in to view upcoming items.");
            using var document = await _api.GetEntityTabAsync(session, kind, entityId, "Upcoming");
            var records = SchoologyApiClient.ReadRecords(document.RootElement, "event", "events", "assignment", "assignments");
            RenderGroupedUpcoming(list, records, null, kind, entityId);
            SetStatusText(status, records.Count == 0 ? "Nothing is scheduled for this " +
                (kind == "Groups" ? "group" : kind == "School Profile" ? "school" : "course") + "." :
                $"{records.Count} upcoming items");
        }
        catch (Exception ex)
        {
            SetStatusText(status, $"Unable to load this {kind.ToLowerInvariant()}’s upcoming items: {ex.Message}");
            list.Items.Clear();
            list.Items.Add(new ListViewItem { Content = Action("Retry", async (_, _) =>
                await LoadEntityUpcomingAsync(kind, entityId, list, status)) });
        }
    }

    private async Task LoadCourseAppsIntoSidebarAsync(long sectionId, ListView navigation, SchoologyEntity course, SelectorBar compactSelector)
    {
        try
        {
            var session = _session;
            if (session is null || sectionId <= 0) return;
            using var document = await _api.GetCourseAppsAsync(session, sectionId);
            var root = document.RootElement;
            JsonElement apps = default;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (!root.TryGetProperty("@extra", out apps) &&
                    !root.TryGetProperty("extra", out apps) &&
                    !(root.TryGetProperty("data", out var data) &&
                      data.ValueKind == JsonValueKind.Object &&
                      (data.TryGetProperty("@extra", out apps) || data.TryGetProperty("extra", out apps))))
                    return;
            }
            if (apps.ValueKind != JsonValueKind.Array || apps.GetArrayLength() == 0) return;
            if (!compactSelector.Items.OfType<SelectorBarItem>().Any(item => item.Text == "Course Apps"))
            {
                var appsTab = new SelectorBarItem { Text = "Course Apps", Tag = "Course Apps" };
                AutomationProperties.SetName(appsTab, "Course apps");
                compactSelector.Items.Add(appsTab);
            }

            navigation.Items.Add(new ListViewItem
            {
                Content = Text("COURSE APPS", 11, true),
                Padding = new Thickness(14, 12, 14, 6),
                IsHitTestVisible = false,
                IsTabStop = false
            });
            foreach (var app in apps.EnumerateArray())
            {
                var appId = JsonLong(app, "id", "application_id", "applicationId", "app_id", "appId");
                if (appId <= 0) continue;
                var title = ProfileValue(app, "title", "name") ?? "Course App";
                var logoUrl = ProfileValue(app, "logoUrl", "logo_url", "logo");
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var appIcon = new Grid { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
                var fallbackIcon = new SymbolIcon(Symbol.AllApps)
                {
                    Width = 22, Height = 22, Foreground = ThemeBrush("SchoologyBlueBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                appIcon.Children.Add(fallbackIcon);
                if (!string.IsNullOrWhiteSpace(logoUrl))
                {
                    var logoImage = new Image { Width = 24, Height = 24,
                        Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                        VerticalAlignment = VerticalAlignment.Center };
                    appIcon.Children.Add(logoImage);
                    _ = LoadPostImageAsync(logoImage, logoUrl, fallbackIcon);
                }
                row.Children.Add(appIcon);
                var label = Text(title, 14, true);
                label.Foreground = ThemeBrush("SchoologyTextBrush");
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                var item = new ListViewItem
                {
                    Content = row,
                    Tag = "courseapp:" + appId,
                    Padding = new Thickness(14, 10, 14, 10),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };
                AutomationProperties.SetName(item, title);
                item.Tapped += async (_, _) => await LaunchCourseAppAsync(sectionId, appId, title, course);
                navigation.Items.Add(item);
            }
        }
        catch (Exception ex)
        {
            App.WriteStartupLog($"Could not load course apps for section {sectionId}.", ex);
        }
    }

    private Grid BuildCourseAppsContent(long sectionId, SchoologyEntity course)
    {
        var root = new Grid { RowSpacing = 8, Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var statusHost = LoadingStatus("Loading course apps…", out var status);
        root.Children.Add(statusHost);
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent)
        };
        AutomationProperties.SetAutomationId(list, "CourseAppsList");
        Grid.SetRow(list, 1);
        root.Children.Add(list);
        _ = LoadCourseAppsContentAsync(sectionId, course, list, status);
        return root;
    }

    private async Task LoadCourseAppsContentAsync(long sectionId, SchoologyEntity course, ListView list, TextBlock status)
    {
        try
        {
            var session = _session ?? throw new InvalidOperationException("Sign in to view course apps.");
            using var document = await _api.GetCourseAppsAsync(session, sectionId);
            var root = document.RootElement;
            JsonElement apps = default;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (!root.TryGetProperty("@extra", out apps) &&
                    !root.TryGetProperty("extra", out apps) &&
                    !(root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
                      (data.TryGetProperty("@extra", out apps) || data.TryGetProperty("extra", out apps))))
                    apps = default;
            }
            list.Items.Clear();
            if (apps.ValueKind != JsonValueKind.Array || apps.GetArrayLength() == 0)
            {
                list.Items.Add(new ListViewItem { Content = EmptyState("No course apps", "Apps provided by this course will appear here.") });
                SetStatusText(status, string.Empty);
                return;
            }
            foreach (var app in apps.EnumerateArray())
            {
                var appId = JsonLong(app, "id", "application_id", "applicationId", "app_id", "appId");
                if (appId <= 0) continue;
                var title = ProfileValue(app, "title", "name") ?? "Course App";
                var logoUrl = ProfileValue(app, "logoUrl", "logo_url", "logo");
                var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 8, 10, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var iconHost = new Grid { Width = 36, Height = 36, VerticalAlignment = VerticalAlignment.Center };
                var fallback = new SymbolIcon(Symbol.AllApps)
                {
                    Width = 28, Height = 28, Foreground = ThemeBrush("SchoologyBlueBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                iconHost.Children.Add(fallback);
                if (!string.IsNullOrWhiteSpace(logoUrl))
                {
                    var image = new Image { Width = 36, Height = 36,
                        Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    iconHost.Children.Add(image);
                    _ = LoadPostImageAsync(image, logoUrl, fallback);
                }
                row.Children.Add(iconHost);
                var label = Text(title, 14, true);
                label.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                var item = new ListViewItem { Content = row, Tag = app.Clone(),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch };
                AutomationProperties.SetName(item, title);
                item.Tapped += async (_, _) => await LaunchCourseAppAsync(sectionId, appId, title, course);
                list.Items.Add(item);
            }
            SetStatusText(status, list.Items.Count == 0 ? "No launchable course apps were returned." : $"{list.Items.Count} course apps");
        }
        catch (Exception ex)
        {
            list.Items.Clear();
            var error = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
            error.Children.Add(EmptyState("Unable to load course apps", ex.Message));
            error.Children.Add(Action("Retry", async (_, _) => await LoadCourseAppsContentAsync(sectionId, course, list, status)));
            list.Items.Add(new ListViewItem { Content = error });
            SetStatusText(status, "Course apps could not be loaded.");
        }
    }

    private async Task LaunchCourseAppAsync(long sectionId, long appId, string title, SchoologyEntity course)
    {
        try
        {
            var session = _session ?? throw new InvalidOperationException("Sign in to launch course apps.");
            using var document = await _api.LaunchCourseAppAsync(session, sectionId, appId);
            var root = document.RootElement;
            var launchUrl = ProfileValue(root, "url", "launch_token_url", "launchTokenUrl", "launch_url", "launchUrl");
            if (string.IsNullOrWhiteSpace(launchUrl) && root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("data", out var data))
                launchUrl = ProfileValue(data, "url", "launch_token_url", "launchTokenUrl", "launch_url", "launchUrl");
            if (string.IsNullOrWhiteSpace(launchUrl))
                throw new InvalidOperationException("ClassWire did not return a course-app launch URL.");
            if (!Uri.TryCreate(launchUrl, UriKind.Absolute, out var destination) ||
                destination.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(destination.UserInfo))
                throw new InvalidOperationException("ClassWire returned an invalid course-app launch URL.");
            if (!await Launcher.LaunchUriAsync(destination))
                throw new InvalidOperationException("Windows could not open the course app.");
        }
        catch (Exception ex) { await ShowApiErrorAsync("Unable to open course app", ex.Message); }
    }

    private static bool AnimationsEnabled =>
        new UISettings().AnimationsEnabled &&
        !string.Equals(ReadAppSetting("ReduceMotion", "false"), "true", StringComparison.OrdinalIgnoreCase);

    private static string ReadAppSetting(string key, string fallback)
    {
        try
        {
            if (!File.Exists(AppSettingsPath)) return fallback;
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(AppSettingsPath));
            return values is not null && values.TryGetValue(key, out var value) ? value : fallback;
        }
        catch { return fallback; }
    }

    private static void WriteAppSetting(string key, string value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppSettingsPath)!);
            var values = File.Exists(AppSettingsPath)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(AppSettingsPath)) ?? new Dictionary<string, string>()
                : new Dictionary<string, string>();
            values[key] = value;
            File.WriteAllText(AppSettingsPath, JsonSerializer.Serialize(values));
        }
        catch (Exception ex) { App.WriteStartupLog($"Could not save setting '{key}'.", ex); }
    }

    private static UIElement GetOrCreateTemporaryView(Dictionary<string, UIElement> cache, string key, Func<UIElement> factory)
    {
        if (cache.TryGetValue(key, out var cached)) return cached;
        var content = factory();
        cache[key] = content;
        return content;
    }

    private static void NavigateShellFrame(Frame frame, UIElement content, SlideNavigationTransitionEffect effect)
    {
        var animationsEnabled = AnimationsEnabled;
        var transition = new SlideNavigationTransitionInfo { Effect = effect };
        frame.ContentTransitions = animationsEnabled
            ? new TransitionCollection { new NavigationThemeTransition { DefaultNavigationTransitionInfo = transition } }
            : null;
        frame.Navigate(typeof(SchoologyShellContentPage), content, animationsEnabled ? transition : null);
    }

    private Grid BuildEntityTabContent(string kind, long entityId, string entity, string tab,
        SchoologyEntity? entityInfo = null, bool showInfobox = true)
    {
        var layout = new Grid { RowSpacing = 8, Margin = new Thickness(8) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var heading = new StackPanel { Spacing = 8 };
        var infobox = new StackPanel { Spacing = 4 };
        infobox.Children.Add(Text(entity, 20, true));
        if (!string.IsNullOrWhiteSpace(entityInfo?.Subtitle))
            infobox.Children.Add(Text(entityInfo.Subtitle, 12));
        if (kind == "Courses" && entityInfo is not null)
        {
            var schoolLink = BuildCourseSchoolLink(entityInfo);
            if (schoolLink is not null) infobox.Children.Add(schoolLink);
        }
        if (showInfobox) heading.Children.Add(Surface(infobox, new Thickness(12)));
        heading.Children.Add(Text(tab, 17, true));
        Grid.SetRow(heading, 0); layout.Children.Add(heading);
        var body = new StackPanel { Spacing = 14 };
        FrameworkElement bodyContent = tab is "Assignments" or "Members" or "Discussions" or "Grades"
            ? body
            : new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = body,
                Background = new SolidColorBrush(Colors.Transparent)
            };
        if (tab == "Materials" && bodyContent is ScrollViewer materialsViewport)
            AutomationProperties.SetAutomationId(materialsViewport, "CourseMaterialsViewport");
        Grid.SetRow(bodyContent, 1); layout.Children.Add(bodyContent);
        var statusHost = LoadingStatus("Loading from ClassWire…", out var status);
        status.Foreground = ThemeBrush("SchoologyMutedTextBrush"); body.Children.Add(statusHost);
        _ = LoadEntityTabAsync(kind, entityId, tab, body, status);
        return layout;
    }

    private UIElement? BuildCourseSchoolLink(SchoologyEntity course)
    {
        var schoolName = ProfileValue(course.Data, "school_title", "school_name", "school") ??
            (_session is null ? null : ProfileValue(_session.User, "school_title", "school_name", "school"));
        if (string.IsNullOrWhiteSpace(schoolName)) return null;

        var schoolId = JsonLong(course.Data, "school_id", "schoolId");
        if (schoolId <= 0 && course.Data.ValueKind == JsonValueKind.Object &&
            course.Data.TryGetProperty("school", out var schoolObject) &&
            schoolObject.ValueKind == JsonValueKind.Object)
            schoolId = JsonLong(schoolObject, "id", "school_id", "schoolId");

        if (schoolId > 0)
        {
            var schoolEntity = new SchoologyEntity(schoolId, schoolName, string.Empty, null, course.Data.Clone());
            var link = Action(schoolName, (_, _) => ShowThreePane("School Profile", schoolEntity));
            link.HorizontalAlignment = HorizontalAlignment.Left;
            link.HorizontalContentAlignment = HorizontalAlignment.Left;
            link.Padding = new Thickness(0, 2, 0, 2);
            link.Background = new SolidColorBrush(Colors.Transparent);
            link.BorderThickness = new Thickness(0);
            link.Foreground = ThemeBrush("SchoologyBlueBrush");
            AutomationProperties.SetName(link, "School profile: " + schoolName);
            return link;
        }

        var label = Text(schoolName, 12);
        label.Foreground = ThemeBrush("SchoologyBlueBrush");
        return label;
    }

    private async Task ShowAssignmentDetailsAsync(long sectionId, SchoologyApiRecord record)
    {
        try
        {
            var session = _session ?? throw new InvalidOperationException("Sign in to view this assignment.");
            var assignmentId = JsonLong(record.Data, "assignment_id", "assignmentId", "id");
            if (assignmentId <= 0) throw new InvalidOperationException("ClassWire did not return a valid assignment ID.");
            using var document = await _api.GetAssignmentAsync(session, sectionId, assignmentId);
            var assignment = document.RootElement;
            if (assignment.ValueKind == JsonValueKind.Object && assignment.TryGetProperty("assignment", out var nested))
                assignment = nested;

            var title = ProfileValue(assignment, "title", "assignment_title") ?? record.Title;
            var description = ProfileValue(assignment, "description_richtext", "descriptionRichtext", "description", "body");
            var due = ProfileValue(assignment, "due", "due_date", "dueDate");
            var points = ProfileValue(assignment, "max_points", "maxPoints", "points");
            var gradeItem = assignment;
            try
            {
                using var gradeItemDocument = await _api.GetAssignmentGradeItemAsync(session, sectionId, assignmentId);
                var gradeItemRoot = gradeItemDocument.RootElement;
                JsonElement gradeItemCandidate;
                if (gradeItemRoot.ValueKind == JsonValueKind.Object &&
                    (gradeItemRoot.TryGetProperty("assignment", out gradeItemCandidate) ||
                     gradeItemRoot.TryGetProperty("grade_item", out gradeItemCandidate) ||
                     gradeItemRoot.TryGetProperty("gradeItem", out gradeItemCandidate)))
                    gradeItem = gradeItemCandidate.Clone();
                else gradeItem = gradeItemRoot.Clone();
            }
            catch (Exception ex) { App.WriteStartupLog("Could not resolve the assignment grade-item metadata; using the assignment object.", ex); }
            var submissionsEnabled = JsonBoolean(assignment,
                "allow_dropbox", "allowDropbox", "allow_submission", "allowSubmission") ||
                JsonBoolean(gradeItem, "allow_dropbox", "allowDropbox", "allow_submission", "allowSubmission");
            var submissionLocked = JsonBoolean(assignment, "dropbox_locked", "dropboxLocked", "submission_locked", "submissionLocked") ||
                JsonBoolean(gradeItem, "dropbox_locked", "dropboxLocked", "submission_locked", "submissionLocked");
            submissionsEnabled &= !submissionLocked;
            var gradeItemId = JsonLong(gradeItem, "id", "grade_item_id", "gradeItemId", "gradeitem_id");
            if (gradeItemId <= 0) gradeItemId = JsonLong(assignment, "grade_item_id", "gradeItemId");
            if (gradeItemId <= 0) gradeItemId = assignmentId;
            var commentsEnabled = JsonBoolean(assignment,
                "allow_discussion", "allowDiscussion", "allow_comments", "allowComments",
                "comments_enabled", "commentsEnabled");
            _activeAssignmentSectionId = sectionId;
            _activeAssignmentId = assignmentId;
            _activeAssignmentGradeItemId = gradeItemId;
            _activeAssignmentAllowsComments = commentsEnabled;
            _activeAssignmentAllowsSubmission = submissionsEnabled;
            SetTitleBarCreateContext(string.Empty);
            var assignmentType = (ProfileValue(assignment, "assignment_type", "assignmentType", "type") ?? "").ToLowerInvariant();
            var launchUrl = ProfileValue(assignment, "web_url", "webUrl", "launch_url", "launchUrl", "url", "href", "location");
            if ((assignmentType is "lti" or "lti_submission") && !string.IsNullOrWhiteSpace(launchUrl))
            {
                await OpenSharedWebViewAsync(launchUrl, title, allowLtiExternal: true,
                    onBack: () => ReturnFromAssignment(sectionId));
                return;
            }

            UIElement BuildInfoView()
            {
                var info = new StackPanel { Spacing = 10, Margin = new Thickness(8) };
                if (!string.IsNullOrWhiteSpace(due)) AddProfileField(info, "Due date", FormatAssignmentDueDate(due));
                if (!string.IsNullOrWhiteSpace(description)) info.Children.Add(CreateRichContent(description, 14));
                if (!string.IsNullOrWhiteSpace(points)) AddProfileField(info, "Points", points);
                if (info.Children.Count == 0) info.Children.Add(EmptyState("No additional assignment information was provided."));
                return new ScrollViewer { Content = info, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            }

            IReadOnlyList<SchoologyApiRecord> commentRecords = [];
            string? commentLoadError = null;
            try
            {
                using var commentDocument = await _api.GetAssignmentCommentsAsync(session, sectionId, assignmentId);
                commentRecords = SchoologyApiClient.ReadRecords(commentDocument.RootElement, "comment", "comments");
                await ResolveCommentAuthorsAsync(commentRecords, session);
            }
            catch (Exception ex) { commentLoadError = ex.Message; }
            UIElement BuildCommentsView()
            {
                var comments = new StackPanel { Spacing = 8, Margin = new Thickness(8) };
                if (commentLoadError is not null) comments.Children.Add(EmptyState("Unable to load assignment comments.", commentLoadError));
                else if (commentRecords.Count == 0) comments.Children.Add(EmptyState("No comments yet."));
                else comments.Children.Add(BuildCommentThreadRows(commentRecords, assignmentId));
                return new ScrollViewer { Content = comments, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            }

            IReadOnlyList<SchoologyApiRecord> submissionRecords = [];
            string? submissionLoadError = null;
            if (submissionsEnabled && gradeItemId > 0)
            {
                try
                {
                    using var submissionDocument = await _api.GetAssignmentSubmissionAsync(session, sectionId, gradeItemId, session.UserId);
                    submissionRecords = SchoologyApiClient.ReadRecords(submissionDocument.RootElement, "revision", "revisions", "submission", "submissions");
                }
                catch (Exception ex) { submissionLoadError = ex.Message; }
            }
            JsonElement submissionGrade = default;
            if (submissionsEnabled && gradeItemId > 0)
            {
                try
                {
                    using var gradeDocument = await _api.GetAssignmentGradesAsync(session, sectionId, gradeItemId);
                    var gradeRecords = SchoologyApiClient.ReadRecords(gradeDocument.RootElement, "grade", "grades");
                    if (gradeRecords.Count > 0) submissionGrade = gradeRecords[0].Data;
                }
                catch (Exception ex) { App.WriteStartupLog("Could not load the assignment's overall grade for its submission view.", ex); }
            }

            UIElement BuildSubmissionsView()
            {
                var submissions = new StackPanel { Spacing = 12, Margin = new Thickness(8) };
                var gradeValue = ProfileValue(submissionGrade, "grade", "calculated_grade");
                var gradeLabel = string.IsNullOrWhiteSpace(gradeValue) ? "—" : gradeValue;
                if (!string.IsNullOrWhiteSpace(points)) gradeLabel += " / " + points;
                var submissionHeader = new Grid { ColumnSpacing = 12 };
                submissionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                submissionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                submissionHeader.Children.Add(Text("Your Submissions", 18, true));
                var overallGrade = Text(gradeLabel, 15, true);
                Grid.SetColumn(overallGrade, 1);
                submissionHeader.Children.Add(overallGrade);
                submissions.Children.Add(submissionHeader);
                var commentElement = FindJsonPropertyRecursive(submissionGrade, "comment", "teacher_comment", "teacherComment");
                var overallComment = commentElement.ValueKind == JsonValueKind.Object
                    ? ProfileValue(commentElement, "comment", "body", "text")
                    : commentElement.ValueKind == JsonValueKind.String ? commentElement.GetString()
                    : ProfileValue(submissionGrade, "comment", "teacher_comment", "teacherComment");
                if (!string.IsNullOrWhiteSpace(overallComment))
                {
                    var comment = new StackPanel { Spacing = 5 };
                    comment.Children.Add(Text("Overall Comment", 14, true));
                    comment.Children.Add(CreateRichContent(overallComment, 13));
                    submissions.Children.Add(comment);
                }
                if (gradeItemId <= 0)
                    submissions.Children.Add(EmptyState("ClassWire did not return a grade-item ID."));
                else if (submissionLoadError is not null)
                    submissions.Children.Add(EmptyState("Unable to load your submission.", submissionLoadError));
                else if (submissionRecords.Count == 0)
                    submissions.Children.Add(EmptyState("You haven’t made any submissions."));
                foreach (var submission in submissionRecords
                    .OrderByDescending(item => JsonLong(item.Data, "revision_id", "revisionId", "id")))
                {
                    var revisionId = ProfileValue(submission.Data, "revision_id", "revisionId", "id") ?? "—";
                    var created = ProfileValue(submission.Data, "created", "created_at", "timestamp");
                    var draft = JsonBoolean(submission.Data, "draft", "is_draft", "isDraft");
                    var late = JsonBoolean(submission.Data, "late", "is_late", "isLate");
                    var statusText = draft ? "Draft" : late ? "Late" : "On time";
                    var revision = new StackPanel { Spacing = 6, Padding = new Thickness(0, 10, 0, 12) };
                    var heading = new Grid { ColumnSpacing = 12 };
                    heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    heading.Children.Add(Text($"Revision {revisionId}", 14, true));
                    var stateLabel = Text(statusText, 12, true);
                    stateLabel.Foreground = ThemeBrush(draft ? "SchoologyMutedTextBrush" :
                        late ? "SchoologyWarningTextBrush" : "SchoologySuccessTextBrush");
                    Grid.SetColumn(stateLabel, 1);
                    heading.Children.Add(stateLabel);
                    revision.Children.Add(heading);
                    if (!string.IsNullOrWhiteSpace(created))
                    {
                        var date = Text(ParseSchoologyDate(created)?.ToString("g",
                            System.Globalization.CultureInfo.CurrentCulture) ?? created, 11);
                        date.Foreground = ThemeBrush("SchoologyMutedTextBrush");
                        revision.Children.Add(date);
                    }
                    var bodyText = ProfileValue(submission.Data, "body", "text", "comment", "description");
                    if (!string.IsNullOrWhiteSpace(bodyText)) revision.Children.Add(CreateRichContent(bodyText, 13));
                    var attachments = BuildAttachmentControls(submission.Data,
                        () => _ = ShowAssignmentDetailsAsync(sectionId, record));
                    if (attachments.Children.Count > 0) revision.Children.Add(attachments);
                    var filename = ProfileValue(submission.Data, "filename", "file_name", "fileName");
                    if (attachments.Children.Count == 0 && !string.IsNullOrWhiteSpace(filename))
                        revision.Children.Add(Text(filename, 12));
                    revision.Children.Add(new Border
                    {
                        Height = 1, Background = ThemeBrush("SchoologyDividerBrush"),
                        Margin = new Thickness(0, 6, 0, 0)
                    });
                    submissions.Children.Add(revision);
                }
                return new ScrollViewer { Content = submissions, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            }

            var assignmentAttachments = ReadAttachmentItems(assignment);
            var header = new Grid { ColumnSpacing = 12, Margin = new Thickness(16, 10, 16, 4) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(Action("‹ Back", (_, _) => ReturnFromAssignment(sectionId)));
            var heading = Text(title, 21, true);
            Grid.SetColumn(heading, 1);
            header.Children.Add(heading);
            if (assignmentAttachments.Count > 0)
            {
                var attachmentsButton = new Button
                {
                    Content = "Attachments (" + assignmentAttachments.Count + ")",
                    Padding = new Thickness(12, 7, 12, 7),
                    VerticalAlignment = VerticalAlignment.Center
                };
                AutomationProperties.SetAutomationId(attachmentsButton, "AssignmentAttachmentsButton");
                attachmentsButton.Click += async (_, _) =>
                    await ShowAssignmentAttachmentsDialogAsync(assignmentAttachments, sectionId);
                Grid.SetColumn(attachmentsButton, 2);
                header.Children.Add(attachmentsButton);
            }

            var wide = new Grid { RowSpacing = 8, ColumnSpacing = 16, Margin = new Thickness(16, 8, 16, 16) };
            wide.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (submissionsEnabled) wide.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            wide.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            wide.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var wideSelector = new SelectorBar();
            var wideInfo = new SelectorBarItem { Text = "Info" };
            var wideComments = new SelectorBarItem { Text = "Comments" };
            wideSelector.Items.Add(wideInfo);
            if (commentsEnabled) wideSelector.Items.Add(wideComments);
            Grid.SetRow(wideSelector, 0);
            wide.Children.Add(wideSelector);
            var wideFrame = new Frame { Background = new SolidColorBrush(Colors.Transparent) };
            Grid.SetRow(wideFrame, 1);
            wide.Children.Add(wideFrame);
            if (submissionsEnabled)
            {
                var pane = Surface(BuildSubmissionsView(), new Thickness(10));
                Grid.SetColumn(pane, 1);
                Grid.SetRowSpan(pane, 2);
                wide.Children.Add(pane);
            }
            void SetConnectedFrameContent(Frame frame, UIElement nextContent)
            {
                // Assignment content may be replaced after an asynchronous request,
                // leaving the prepared source detached before TryStart executes.
                // Keep the native WinUI navigation transition without a stale source.
                frame.ContentTransitions = AnimationsEnabled
                    ? new TransitionCollection { new NavigationThemeTransition() }
                    : null;
                frame.Content = nextContent;
            }
            var wideSelectedTab = "Info";
            var narrowSelectedTab = "Info";
            void UpdateAssignmentCreateContext(bool isWide, string selectedTab)
            {
                var context = isWide
                    ? submissionsEnabled && (selectedTab is "Info" or "Comments")
                        ? "assignment-actions"
                        : selectedTab == "Comments" && commentsEnabled
                            ? "assignment-comment"
                            : string.Empty
                    : selectedTab == "Comments" && commentsEnabled
                        ? "assignment-comment"
                        : selectedTab == "Submissions" && submissionsEnabled
                            ? "assignment-actions"
                            : string.Empty;
                SetTitleBarCreateContext(context);
            }
            void SelectWide(SelectorBarItem item)
            {
                wideSelectedTab = item == wideComments ? "Comments" : "Info";
                SetConnectedFrameContent(wideFrame, wideSelectedTab == "Comments" ? BuildCommentsView() : BuildInfoView());
                UpdateAssignmentCreateContext(true, wideSelectedTab);
            }
            wideSelector.SelectionChanged += (_, _) => { if (wideSelector.SelectedItem is SelectorBarItem item) SelectWide(item); };
            wideSelector.SelectedItem = wideInfo;
            SelectWide(wideInfo);

            var narrow = new Grid { RowSpacing = 8, Margin = new Thickness(12) };
            narrow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            narrow.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var narrowSelector = new SelectorBar { HorizontalAlignment = HorizontalAlignment.Stretch };
            var narrowInfo = new SelectorBarItem { Text = "Info" };
            var narrowComments = new SelectorBarItem { Text = "Comments" };
            var narrowSubmissions = new SelectorBarItem { Text = "Submissions" };
            narrowSelector.Items.Add(narrowInfo);
            if (commentsEnabled) narrowSelector.Items.Add(narrowComments);
            if (submissionsEnabled) narrowSelector.Items.Add(narrowSubmissions);
            Grid.SetRow(narrowSelector, 0);
            narrow.Children.Add(narrowSelector);
            var narrowFrame = new Frame { Background = new SolidColorBrush(Colors.Transparent) };
            Grid.SetRow(narrowFrame, 1);
            narrow.Children.Add(narrowFrame);
            void SelectNarrow(SelectorBarItem item)
            {
                narrowSelectedTab = item == narrowComments ? "Comments" :
                    item == narrowSubmissions && submissionsEnabled ? "Submissions" : "Info";
                var nextContent = narrowSelectedTab == "Comments" ? BuildCommentsView() :
                    narrowSelectedTab == "Submissions" ? BuildSubmissionsView() : BuildInfoView();
                SetConnectedFrameContent(narrowFrame, nextContent);
                UpdateAssignmentCreateContext(false, narrowSelectedTab);
            }
            narrowSelector.SelectionChanged += (_, _) => { if (narrowSelector.SelectedItem is SelectorBarItem item) SelectNarrow(item); };
            narrowSelector.SelectedItem = narrowInfo;
            SelectNarrow(narrowInfo);

            var page = new Grid();
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            page.Children.Add(header);
            var responsive = new Grid();
            responsive.Children.Add(wide);
            responsive.Children.Add(narrow);
            void UpdateLayout(double width)
            {
                var isWide = width >= 900;
                wide.Visibility = isWide ? Visibility.Visible : Visibility.Collapsed;
                narrow.Visibility = isWide ? Visibility.Collapsed : Visibility.Visible;
                UpdateAssignmentCreateContext(isWide, isWide ? wideSelectedTab : narrowSelectedTab);
            }
            responsive.SizeChanged += (_, args) => UpdateLayout(args.NewSize.Width);
            UpdateLayout(_root.ActualWidth);
            Grid.SetRow(responsive, 1);
            page.Children.Add(responsive);
            if (_sectionFrame is not null) NavigateMainSection(page, title);
            else Reset(BuildApplicationShell(title, page));
            // BuildApplicationShell initializes the generic section Create state.
            // Reapply the assignment action for the selector's initial active tab.
            var isWideNow = _root.ActualWidth >= 900;
            UpdateAssignmentCreateContext(isWideNow, isWideNow ? wideSelectedTab : narrowSelectedTab);
        }
        catch (Exception ex) { await ShowApiErrorAsync("Unable to load assignment", ex.Message); }
    }

    private async Task ShowCoursePageAsync(long sectionId, SchoologyApiRecord record)
    {
        try
        {
            var session = _session ?? throw new InvalidOperationException("Sign in to view this page.");
            var pageId = JsonLong(record.Data, "page_id", "pageId", "id", "template_id", "templateID");
            if (pageId <= 0) throw new InvalidOperationException("ClassWire did not return a valid page ID.");
            using var document = await _api.GetCoursePageAsync(session, sectionId, pageId);
            var pageData = document.RootElement;
            if (pageData.ValueKind == JsonValueKind.Object && pageData.TryGetProperty("page", out var nestedPage))
                pageData = nestedPage;
            var title = ProfileValue(pageData, "title", "name", "page_title") ?? record.Title;
            var markup = ProfileValue(pageData, "body", "body_richtext", "bodyRichtext",
                "description_richtext", "descriptionRichtext", "description", "content", "text");

            var header = new Grid { ColumnSpacing = 12, Margin = new Thickness(8, 4, 8, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.Children.Add(Action("‹ Back", (_, _) =>
                ShowThreePane("Courses", _courses.FirstOrDefault(course => course.Id == sectionId))));
            var heading = Text(title, 21, true);
            heading.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(heading, 1);
            header.Children.Add(heading);

            var page = new Grid { RowSpacing = 10, Margin = new Thickness(12) };
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            page.Children.Add(header);

            var body = new StackPanel { Spacing = 12, Margin = new Thickness(8) };
            if (string.IsNullOrWhiteSpace(markup))
                body.Children.Add(EmptyState("This page has no content."));
            else
                body.Children.Add(CreateRichContent(markup, 14));

            var pageAttachments = BuildAttachmentControls(pageData,
                () => ShowThreePane("Courses", _courses.FirstOrDefault(course => course.Id == sectionId)));
            if (pageAttachments.Children.Count > 0)
            {
                body.Children.Add(Text("Attachments", 14, true));
                body.Children.Add(pageAttachments);
            }
            var viewport = new ScrollViewer
            {
                Content = body,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                ZoomMode = ZoomMode.Disabled
            };
            Grid.SetRow(viewport, 1);
            page.Children.Add(viewport);
            if (_sectionFrame is not null) NavigateMainSection(page, title);
            else Reset(BuildApplicationShell(title, page));
        }
        catch (Exception ex) { await ShowApiErrorAsync("Unable to load ClassWire page", ex.Message); }
    }

    private async Task ShowDiscussionDetailsAsync(string kind, long entityId, SchoologyApiRecord record, Action? onBack = null) =>
        await ShowContentDetailsAsync(kind, entityId, record, false, onBack: onBack);
    private async Task ShowEventDetailsAsync(string kind, long entityId, SchoologyApiRecord record, Action? onBack = null) =>
        await ShowContentDetailsAsync(kind, entityId, record, true, onBack: onBack);

    private async Task ShowContentCommentDialogAsync(string kind, long entityId, SchoologyApiRecord record,
        bool isEvent, long parentCommentId = 0, string? replyAuthor = null, Action? onBack = null)
    {
        var session = _session;
        if (session is null) return;
        if (isEvent && JsonBooleanValue(record.Data, "comments_enabled", "commentsEnabled", "allow_comments", "allowComments") == false) return;
        var itemId = JsonLong(record.Data, isEvent ? "event_id" : "discussion_id", isEvent ? "eventId" : "discussionId", "id");
        if (itemId <= 0) return;
        var input = new TextBox { Header = parentCommentId > 0 ? $"Reply to {replyAuthor ?? "comment"}" : "Comment",
            PlaceholderText = parentCommentId > 0 ? "Write a reply…" : "Write a comment…",
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, MaxHeight = 220 };
        AutomationProperties.SetAutomationId(input, "ContentCommentInput");
        var dialog = new ContentDialog { Title = parentCommentId > 0 ? "Reply" : "Create comment",
            Content = input, PrimaryButtonText = "Post", CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary, XamlRoot = _root.XamlRoot, RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(input.Text)) return;
        try
        {
            using var posted = await _api.AddContentCommentAsync(session, kind, entityId, itemId, isEvent, input.Text.Trim(), parentCommentId);
            await ShowContentDetailsAsync(kind, entityId, record, isEvent, openComments: true, onBack: onBack);
        }
        catch (Exception ex) { await ShowApiErrorAsync("Unable to post comment", ex.Message); }
    }

    private async Task ShowContentDetailsAsync(string kind, long entityId, SchoologyApiRecord record,
        bool isEvent, bool openComments = false, Action? onBack = null)
    {
        try
        {
            var session = _session ?? throw new InvalidOperationException("Sign in to view this item.");
            var itemId = JsonLong(record.Data, isEvent ? "event_id" : "discussion_id", isEvent ? "eventId" : "discussionId", "id");
            if (itemId <= 0) throw new InvalidOperationException("ClassWire did not return a valid item ID.");
            using var detailDocument = isEvent ? await _api.GetEventAsync(session, kind, entityId, itemId)
                : await _api.GetDiscussionAsync(session, kind, entityId, itemId);
            var data = detailDocument.RootElement.Clone();
            var key = isEvent ? "event" : "discussion";
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty(key, out var nested)) data = nested;
            var title = ProfileValue(data, "title", "name") ?? record.Title;
            var description = ProfileValue(data, "description_richtext", "descriptionRichtext", "description", "body", "message");
            var date = ProfileValue(data, "start", "start_time", "date", "due", "due_date");
            var commentsEnabled = !isEvent ||
                (JsonBooleanValue(data, "comments_enabled", "commentsEnabled", "allow_comments", "allowComments") ?? true);
            IReadOnlyList<SchoologyApiRecord> comments = [];
            string? commentsError = null;
            if (commentsEnabled)
            {
                try
                {
                    using var commentsDocument = isEvent ? await _api.GetEventCommentsAsync(session, kind, entityId, itemId)
                        : await _api.GetDiscussionCommentsAsync(session, kind, entityId, itemId);
                    comments = SchoologyApiClient.ReadRecords(commentsDocument.RootElement, "comment", "comments");
                    await ResolveCommentAuthorsAsync(comments, session);
                }
                catch (Exception ex) { commentsError = ex.Message; }
            }
            UIElement BuildInfo()
            {
                var info = new StackPanel { Spacing = 10, Margin = new Thickness(12) };
                info.Children.Add(Text(title, 20, true));
                if (!string.IsNullOrWhiteSpace(date)) AddProfileField(info, isEvent ? "When" : "Due date", FormatAssignmentDueDate(date));
                if (!string.IsNullOrWhiteSpace(description)) info.Children.Add(CreateRichContent(description, 14));
                var attachmentPanel = BuildAttachmentControls(data, () => _ = ShowContentDetailsAsync(kind, entityId, record, isEvent, onBack: onBack));
                if (attachmentPanel.Children.Count > 0) info.Children.Add(attachmentPanel);
                if (info.Children.Count == 1) info.Children.Add(EmptyState("No additional information was provided."));
                return new ScrollViewer { Content = info, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            }
            UIElement BuildComments()
            {
                var commentList = new StackPanel { Spacing = 8 };
                if (commentsError is not null)
                    commentList.Children.Add(EmptyState("Unable to load comments.", commentsError));
                else if (comments.Count == 0)
                    commentList.Children.Add(EmptyState("No comments yet."));
                else
                    commentList.Children.Add(BuildCommentThreadRows(comments, itemId,
                        (parentId, author) => _ = ShowContentCommentDialogAsync(kind, entityId, record, isEvent, parentId, author, onBack)));
                return new ScrollViewer { Content = commentList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            }
            var header = new Grid { ColumnSpacing = 12, Margin = new Thickness(16, 10, 16, 4) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.Children.Add(Action("‹ Back", (_, _) => { if (onBack is not null) onBack(); else ReturnFromAssignment(entityId); }));
            var heading = Text(title, 21, true); Grid.SetColumn(heading, 1); header.Children.Add(heading);
            var page = new Grid { RowSpacing = 8, Margin = new Thickness(12) };
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            page.Children.Add(header);
            var selector = new SelectorBar { HorizontalAlignment = HorizontalAlignment.Stretch };
            var infoItem = new SelectorBarItem { Text = "Info" };
            var commentsItem = new SelectorBarItem { Text = isEvent ? "Comments" : "Discussion" };
            selector.Items.Add(infoItem);
            if (commentsEnabled) selector.Items.Add(commentsItem);
            Grid.SetRow(selector, 1); page.Children.Add(selector);
            var frame = new Frame { Background = new SolidColorBrush(Colors.Transparent) };
            Grid.SetRow(frame, 2); page.Children.Add(frame);
            void Select(SelectorBarItem item)
            {
                var showingComments = commentsEnabled && item == commentsItem;
                frame.Content = showingComments ? BuildComments() : BuildInfo();
                SetTitleBarCreateContext(showingComments ? "content-comment" : string.Empty,
                    showingComments ? () => ShowContentCommentDialogAsync(kind, entityId, record, isEvent, onBack: onBack) : null);
            }
            selector.SelectionChanged += (_, _) => { if (selector.SelectedItem is SelectorBarItem item) Select(item); };
            selector.SelectedItem = openComments && commentsEnabled ? commentsItem : infoItem;
            Select(openComments && commentsEnabled ? commentsItem : infoItem);
            if (_sectionFrame is not null) NavigateMainSection(page, title); else Reset(BuildApplicationShell(title, page));
            SetTitleBarCreateContext(openComments && commentsEnabled ? "content-comment" : string.Empty,
                openComments && commentsEnabled ? () => ShowContentCommentDialogAsync(kind, entityId, record, isEvent, onBack: onBack) : null);
        }
        catch (Exception ex) { await ShowApiErrorAsync(isEvent ? "Unable to load event" : "Unable to load discussion", ex.Message); }
    }

    

    private void ReturnFromAssignment(long sectionId)
    {
        if (_page == "Courses")
        {
            ShowThreePane("Courses", _courses.FirstOrDefault(course => course.Id == sectionId));
            return;
        }
        if (_page == "Groups")
        {
            ShowThreePane("Groups", _groups.FirstOrDefault(group => group.Id == _selectedEntityId));
            return;
        }
        ShowHome(_section is "Upcoming" or "Calendar" ? _section : "Recent Activity");
    }

    private async Task LoadEntityTabAsync(string kind, long entityId, string tab, StackPanel body, TextBlock status)
    {
        try
        {
            var session = _session;
            if (session is null || entityId <= 0) throw new InvalidOperationException("Select a ClassWire item after signing in.");
            using var document = tab == "Materials" && kind == "Courses"
                ? await _api.GetCourseFolderAsync(session, entityId, 0)
                : tab == "Resources" && kind == "Groups"
                    ? await _api.GetGroupResourcesAsync(session, entityId)
                    : tab == "Grades"
                        ? await _api.GetUserGradesAsync(session)
                        : await _api.GetEntityTabAsync(session, kind, entityId, tab);
            if (kind == "School Profile" && tab == "Info")
            {
                RemoveLoadingStatus(status);
                var school = document.RootElement;
                AddProfileField(body, "Address", string.Join(" ", new[]
                {
                    ProfileValue(school, "address1"), ProfileValue(school, "address2"),
                    ProfileValue(school, "city"), ProfileValue(school, "state"),
                    ProfileValue(school, "postal_code"), ProfileValue(school, "country")
                }.Where(value => !string.IsNullOrWhiteSpace(value))));
                AddProfileField(body, "Phone", ProfileValue(school, "phone"));
                if (body.Children.Count <= 2) body.Children.Add(Surface(Text("No additional school information was returned.")));
                return;
            }
            var records = SchoologyApiClient.ReadRecords(document.RootElement,
                tab == "Members" ? ["enrollment", "enrollments", "member", "members", "user", "users"] :
                tab == "Materials" ? ["folder-item", "folder_item", "folderItems", "items"] : []);
            RemoveLoadingStatus(status);
            if (tab == "Resources" && kind == "Groups")
            {
                var entries = records.Select(record =>
                {
                    var type = (ProfileValue(record.Data, "type", "resource_type", "document_type",
                        "documentType", "template_type", "templateType") ?? "").ToLowerInvariant();
                    var isFolder = type.Contains("folder", StringComparison.Ordinal) || IsFolderRecord(record.Data);
                    var id = JsonLong(record.Data, "id", "resource_id", "resourceId", "folder_id", "folderId");
                    var collectionId = JsonLong(record.Data, "collection_id", "collectionID", "collectionId");
                    var title = ProfileValue(record.Data, "resource_title", "resourceTitle", "title", "name",
                        "filename", "file_name") ?? record.Title;
                    return new ResourceTreeEntry(title, isFolder ? "folder" : "resource", id, collectionId, record.Data.Clone());
                }).ToArray();
                if (entries.Length == 0) body.Children.Add(EmptyState("No resources."));
                else body.Children.Add(BuildResourceEntriesView(
                    new[] { ("Resources", (IReadOnlyList<ResourceTreeEntry>)entries) },
                    "Groups", entityId,
                    () => ShowThreePane("Groups", _groups.FirstOrDefault(group => group.Id == entityId))));
                return;
            }
            if (tab == "Members") { body.Children.Add(BuildMembersList(records, kind)); return; }
            if (tab == "Materials") { body.Children.Add(BuildMaterialsList(entityId, records)); return; }
            if (tab == "Assignments")
            {
                var assignments = new ListView
                {
                    SelectionMode = ListViewSelectionMode.None,
                    IsItemClickEnabled = false,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Background = new SolidColorBrush(Colors.Transparent)
                };
                AutomationProperties.SetAutomationId(assignments, "CourseAssignmentsList");
                foreach (var assignmentRecord in records)
                {
                    var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 12, 10, 12) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.Children.Add(new SymbolIcon(Symbol.Edit) { Foreground = ThemeBrush("SchoologyBlueBrush"), Width = 26 });
                    var labels = new StackPanel { Spacing = 3 };
                    labels.Children.Add(Text(assignmentRecord.Title, 14, true));
                    var due = ProfileValue(assignmentRecord.Data, "due", "due_date", "dueDate");
                    if (!string.IsNullOrWhiteSpace(due)) labels.Children.Add(Text("Due " + FormatAssignmentDueDate(due), 12));
                    var description = ProfileValue(assignmentRecord.Data, "description", "body", "description_richtext");
                    if (!string.IsNullOrWhiteSpace(description)) labels.Children.Add(Text(StripMarkup(description), 12));
                    Grid.SetColumn(labels, 1);
                    row.Children.Add(labels);
                    var item = new ListViewItem { Content = row, Tag = assignmentRecord,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch };
                    AutomationProperties.SetName(item, assignmentRecord.Title);
                    item.Tapped += async (_, _) => await ShowAssignmentDetailsAsync(entityId, assignmentRecord);
                    assignments.Items.Add(item);
                }
                if (records.Count == 0)
                    assignments.Items.Add(new ListViewItem { Content = EmptyState("No assignments in this course.") });
                body.Children.Add(assignments);
                return;
            }
            if (tab == "Grades")
            {
                var sections = SchoologyApiClient.ReadRecords(document.RootElement, "section", "sections");
                var selectedSection = sections.FirstOrDefault(record =>
                    JsonLong(record.Data, "section_id", "sectionId", "id") == entityId);
                if (selectedSection is null)
                    body.Children.Add(EmptyState("No grades"));
                else body.Children.Add(BuildGradeDetails(selectedSection.Data, expandPeriods: true));
                return;
            }
            if (records.Count == 0)
            {
                body.Children.Add(tab switch
                {
                    "Updates" => EmptyState("No updates", kind == "Courses" ? "There are no updates in this course." : null),
                    "Discussions" => EmptyState("No discussions."),
                    "Resources" => EmptyState("No resources."),
                    "Members" => EmptyState("No people found."),
                    _ => EmptyState($"No {tab.ToLowerInvariant()}.")
                });
                return;
            }
            if (tab == "Updates")
            {
                await ResolvePostAuthorsAsync(records, session);
                foreach (var record in records)
                {
                    var authorId = JsonLong(record.Data, "user_id", "uid", "author_id", "authorId", "created_by_id");
                    JsonElement? profile = authorId > 0 && _userProfileCache.TryGetValue(authorId, out var cached) ? cached : null;
                    body.Children.Add(BuildUpdateCard(kind, entityId, record, profile));
                }
                return;
            }
            var list = new ListView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = false,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Colors.Transparent) };
            AutomationProperties.SetAutomationId(list, $"{kind}{tab}List");
            foreach (var record in records)
            {
                var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 10, 10, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var type = (ProfileValue(record.Data, "type", "template_type", "document_type") ?? "").ToLowerInvariant();
                var glyph = tab == "Discussions" || type == "discussion" ? "\uE8BD" :
                    type == "event" ? "\uE787" : type == "assignment" ? "\uE70F" :
                    tab == "Materials" ? MaterialGlyph(record.Data) : "\uE8A5";
                UIElement rowIcon = tab == "Materials" && IsFolderRecord(record.Data)
                    ? FolderIcons.CreateIcon(FolderColorName(record.Data), 32)
                    : new FontIcon { Glyph = glyph, FontSize = 22, Foreground = ThemeBrush("SchoologyBlueBrush") };
                row.Children.Add(rowIcon);
                var labels = new StackPanel { Spacing = 3 };
                labels.Children.Add(Text(record.Title, 14, true));
                if (!string.IsNullOrWhiteSpace(record.Subtitle)) labels.Children.Add(Text(record.Subtitle, 12));
                var date = ProfileValue(record.Data, "due", "due_date", "start", "start_time", "date");
                if (!string.IsNullOrWhiteSpace(date)) labels.Children.Add(Text(date, 11));
                Grid.SetColumn(labels, 1); row.Children.Add(labels);
                var item = new ListViewItem { Content = row, Tag = record, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                if (tab == "Discussions") item.Tapped += async (_, _) => await ShowDiscussionDetailsAsync(kind, entityId, record);
                else if (tab == "Events" || type == "event") item.Tapped += async (_, _) => await ShowEventDetailsAsync(kind, entityId, record);
                list.Items.Add(item);
            }
            body.Children.Add(list);
        }
        catch (Exception ex)
        {
            SetStatusText(status, $"Unable to load {tab.ToLowerInvariant()}: {ex.Message}");
        }
    }



    private static JsonElement FirstFileAttachment(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return default;
        JsonElement attachments = default;
        if (!data.TryGetProperty("attachments", out attachments) &&
            !data.TryGetProperty("attachment", out attachments)) return default;
        if (attachments.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in attachments.EnumerateArray())
            {
                var found = FirstFileAttachment(item);
                if (found.ValueKind == JsonValueKind.Object) return found;
            }
            return default;
        }
        if (attachments.ValueKind != JsonValueKind.Object) return default;
        if (!attachments.TryGetProperty("files", out var files))
            attachments.TryGetProperty("file", out files);
        if (files.ValueKind == JsonValueKind.Array)
            return files.GetArrayLength() > 0 ? files[0] : default;
        if (files.ValueKind == JsonValueKind.Object)
        {
            if (files.TryGetProperty("file", out var nested))
            {
                if (nested.ValueKind == JsonValueKind.Array) return nested.GetArrayLength() > 0 ? nested[0] : default;
                if (nested.ValueKind == JsonValueKind.Object) return nested;
            }
            return files;
        }
        return default;
    }

    private static JsonElement FirstLinkAttachment(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return default;
        JsonElement attachments = default;
        if (!data.TryGetProperty("attachments", out attachments) &&
            !data.TryGetProperty("attachment", out attachments)) return default;
        if (attachments.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in attachments.EnumerateArray())
            {
                var found = FirstLinkAttachment(item);
                if (found.ValueKind == JsonValueKind.Object) return found;
            }
            return default;
        }
        if (attachments.ValueKind != JsonValueKind.Object || !attachments.TryGetProperty("links", out var links))
            return default;
        if (links.ValueKind == JsonValueKind.Object && links.TryGetProperty("link", out var nested))
            links = nested;
        if (links.ValueKind == JsonValueKind.Array)
            return links.GetArrayLength() > 0 ? links[0] : default;
        return links.ValueKind == JsonValueKind.Object ? links : default;
    }

    private static string SafeMaterialFileName(string? value, string fallback = "ClassWire file")
    {
        var name = string.IsNullOrWhiteSpace(value) ? fallback : Path.GetFileName(value.Trim());
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        name = name.Trim().Trim('.');
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private static string MaterialExtension(string filename, string mime)
    {
        var extension = Path.GetExtension(filename);
        if (!string.IsNullOrWhiteSpace(extension)) return extension.ToLowerInvariant();
        return mime.ToLowerInvariant().Split(';')[0].Trim() switch
        {
            "application/pdf" => ".pdf",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "text/plain" => ".txt",
            "text/csv" => ".csv",
            _ => ".bin"
        };
    }

    private static bool IsAudioMaterial(string filename, string mime, string? url = null)
    {
        var normalizedMime = mime.Split(';')[0].Trim();
        if (normalizedMime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return true;
        var extension = Path.GetExtension(filename);
        if (IsAudioExtension(extension)) return true;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            IsAudioExtension(Path.GetExtension(uri.AbsolutePath));
    }

    private static bool IsAudioExtension(string extension) =>
        extension.ToLowerInvariant() is
            ".mp3" or ".m4a" or ".aac" or ".wav" or ".wma" or ".flac" or ".ogg" or ".oga" or
            ".opus" or ".aiff" or ".aif" or ".mid" or ".midi";

    private static bool IsPlayableMedia(string filename, string mime, string? url = null)
    {
        var normalizedMime = mime.Split(';')[0].Trim();
        if (normalizedMime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ||
            normalizedMime.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return true;
        if (IsKnownPlayableMediaExtension(Path.GetExtension(filename))) return true;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            IsKnownPlayableMediaExtension(Path.GetExtension(uri.AbsolutePath));
    }

    private static bool IsKnownPlayableMediaExtension(string extension) =>
        extension.ToLowerInvariant() is
            ".mp4" or ".m4v" or ".mov" or ".avi" or ".wmv" or ".mkv" or ".webm" or
            ".mpeg" or ".mpg" or ".3gp" or ".ts" or ".m2ts" or ".ogv" or
            ".mp3" or ".m4a" or ".aac" or ".wav" or ".wma" or ".flac" or ".ogg" or
            ".oga" or ".opus" or ".aiff" or ".aif" or ".mid" or ".midi";

    private static string GetPlayableMediaExtension(string filename, string mime, string? url = null)
    {
        var extension = Path.GetExtension(filename);
        if (IsKnownPlayableMediaExtension(extension)) return extension.ToLowerInvariant();
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            extension = Path.GetExtension(uri.AbsolutePath);
            if (IsKnownPlayableMediaExtension(extension)) return extension.ToLowerInvariant();
        }
        return mime.Split(';')[0].Trim().ToLowerInvariant() switch
        {
            "video/quicktime" => ".mov",
            "video/webm" => ".webm",
            "video/x-msvideo" => ".avi",
            "video/x-ms-wmv" => ".wmv",
            "audio/mpeg" => ".mp3",
            "audio/mp4" => ".m4a",
            "audio/aac" => ".aac",
            "audio/wav" or "audio/x-wav" => ".wav",
            "audio/flac" or "audio/x-flac" => ".flac",
            "audio/ogg" => ".ogg",
            _ when mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase) => ".mp4",
            _ when mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) => ".mp3",
            _ => ".bin"
        };
    }

    private static string FormatFileSize(long bytes) => bytes < 1024 * 1024
        ? $"{bytes / 1024d:0.#} KB"
        : $"{bytes / (1024d * 1024d):0.##} MB";

    private async Task<byte[]?> DownloadSchoologyBytesWithProgressAsync(string url, string filename)
    {
        while (true)
        {
            using var cancellation = new CancellationTokenSource();
            var progressBar = new ProgressBar { IsIndeterminate = true, Minimum = 0, Maximum = 100 };
            var status = Text("Connecting to ClassWire…", 12);
            status.Foreground = ThemeBrush("SchoologyMutedTextBrush");
            var content = new StackPanel { Spacing = 10, MinWidth = 320 };
            content.Children.Add(status);
            content.Children.Add(progressBar);
            var dialog = new ContentDialog
            {
                Title = $"Downloading {filename}",
                Content = content,
                PrimaryButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = _root.XamlRoot,
                RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
            };
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished = false;
            dialog.PrimaryButtonClick += (_, _) =>
            {
                cancellation.Cancel();
                cancelled.TrySetResult(true);
            };
            dialog.Closed += (_, _) =>
            {
                if (!finished)
                {
                    cancellation.Cancel();
                    cancelled.TrySetResult(true);
                }
            };
            var showOperation = dialog.ShowAsync();
            var progress = new Progress<(long Received, long? Total)>(value =>
            {
                if (value.Total is > 0)
                {
                    progressBar.IsIndeterminate = false;
                    progressBar.Maximum = value.Total.Value;
                    progressBar.Value = Math.Min(value.Received, value.Total.Value);
                    SetStatusText(status, $"{FormatFileSize(value.Received)} of {FormatFileSize(value.Total.Value)}");
                }
                else
                {
                    progressBar.IsIndeterminate = true;
                    SetStatusText(status, $"Downloaded {FormatFileSize(value.Received)}");
                }
            });
            var download = _auth.GetSchoologyFileBytesAsync(
                _session ?? throw new InvalidOperationException("Sign in to download ClassWire files."),
                url, progress, cancellation.Token);
            var completed = await Task.WhenAny(download, cancelled.Task);
            if (completed == cancelled.Task)
            {
                try { await download; } catch { }
                await showOperation;
                return null;
            }

            finished = true;
            dialog.Hide();
            await showOperation;
            try
            {
                return await download;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                var retry = new ContentDialog
                {
                    Title = "Download failed",
                    Content = Text($"{ex.Message}\n\nWould you like to try again?", 13),
                    PrimaryButtonText = "Retry",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = _root.XamlRoot,
                    RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
                };
                if (await retry.ShowAsync() != ContentDialogResult.Primary) throw;
            }
        }
    }

    private async Task SaveOriginalMaterialAsync(string url, string filename, string mime, bool openAfterSave = false)
    {
        var safeName = SafeMaterialFileName(filename);
        var extension = MaterialExtension(safeName, mime);
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
            SuggestedFileName = Path.GetFileNameWithoutExtension(safeName)
        };
        picker.FileTypeChoices.Add("ClassWire file", new List<string> { extension });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(AppWindow.Id));
        var destination = await picker.PickSaveFileAsync();
        if (destination is null) return;
        var bytes = await DownloadSchoologyBytesWithProgressAsync(url, safeName);
        if (bytes is null) return;
        await File.WriteAllBytesAsync(destination.Path, bytes);
        if (openAfterSave)
        {
            var opened = await Windows.System.Launcher.LaunchFileAsync(destination);
            if (!opened) await ShowApiErrorAsync("Unable to open downloaded file", "Windows could not find an application for this file type.");
        }
    }

    private async Task OpenNativeMaterialFileAsync(
        string previewUrl, string previewMime, string originalUrl, string filename, string originalMime,
        string title, Action onBack)
    {
        try
        {
            var extension = MaterialExtension(filename, previewMime);
            if (string.IsNullOrWhiteSpace(Path.GetExtension(filename)) &&
                Uri.TryCreate(previewUrl, UriKind.Absolute, out var previewUri))
            {
                var urlExtension = Path.GetExtension(previewUri.AbsolutePath).ToLowerInvariant();
                if (urlExtension.Length > 0) extension = urlExtension;
            }
            var previewIsMedia = IsPlayableMedia(filename, previewMime, previewUrl) ||
                IsPlayableMedia(filename, originalMime, originalUrl);
            if (previewIsMedia && IsPlayableMedia(filename, originalMime, originalUrl))
            {
                previewUrl = originalUrl;
                previewMime = originalMime;
            }
            if (previewIsMedia)
            {
                var mediaName = SafeMaterialFileName(filename);
                var mediaExtension = GetPlayableMediaExtension(filename, previewMime, previewUrl);
                var currentExtension = Path.GetExtension(mediaName);
                if (string.IsNullOrWhiteSpace(currentExtension) ||
                    !IsKnownPlayableMediaExtension(currentExtension))
                    mediaName = Path.ChangeExtension(mediaName, mediaExtension);
                var mediaBytes = await DownloadSchoologyBytesWithProgressAsync(previewUrl, mediaName);
                if (mediaBytes is null) return;
                var mediaCache = Path.Combine(Path.GetTempPath(), "Schoology", "MaterialPreviews");
                Directory.CreateDirectory(mediaCache);
                var mediaPath = Path.Combine(mediaCache, Guid.NewGuid().ToString("N") + "_" + mediaName);
                await File.WriteAllBytesAsync(mediaPath, mediaBytes);
                await ShowNativeMediaFileViewerAsync(mediaPath, title, originalUrl, filename, originalMime, onBack);
                return;
            }

            var previewIsPdf = previewMime.Contains("pdf", StringComparison.OrdinalIgnoreCase) ||
                extension == ".pdf" || previewUrl.Contains(".pdf", StringComparison.OrdinalIgnoreCase);
            var previewIsImage = previewMime.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp";
            if (!previewIsPdf && !previewIsImage)
            {
                await SaveOriginalMaterialAsync(originalUrl, filename, originalMime, openAfterSave: true);
                return;
            }

            var previewName = previewIsPdf
                ? Path.GetFileNameWithoutExtension(SafeMaterialFileName(filename)) + ".pdf"
                : SafeMaterialFileName(filename);
            var bytes = await DownloadSchoologyBytesWithProgressAsync(previewUrl, previewName);
            if (bytes is null) return;
            var cache = Path.Combine(Path.GetTempPath(), "Schoology", "MaterialPreviews");
            Directory.CreateDirectory(cache);
            var previewPath = Path.Combine(cache, Guid.NewGuid().ToString("N") + "_" + SafeMaterialFileName(previewName));
            await File.WriteAllBytesAsync(previewPath, bytes);
            await ShowNativeFileViewerAsync(previewPath, title, previewIsPdf, originalUrl, filename, originalMime, onBack);
        }
        catch (Exception ex)
        {
            await ShowApiErrorAsync("Unable to open material", ex.Message);
        }
    }

    private async Task ShowNativeMediaFileViewerAsync(
        string previewPath, string title, string originalUrl, string originalFilename,
        string originalMime, Action onBack)
    {
        var isAudio = IsAudioMaterial(Path.GetFileName(previewPath), originalMime, previewPath) ||
            IsAudioMaterial(originalFilename, originalMime, previewPath);
        var viewer = new Grid
        {
            Background = new SolidColorBrush(Colors.Transparent),
            RowSpacing = 8
        };
        viewer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        viewer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        viewer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var toolbar = new Grid { ColumnSpacing = 8 };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var closing = false;
        var player = new MediaPlayer();
        Button back = null!;
        back = Action("‹ Back", async (_, _) =>
        {
            if (closing) return;
            closing = true;
            back.IsEnabled = false;
            player.Pause();
            player.Source = null;
            player.Dispose();
            _activeNativeFileViewerRoot = null;
            _refreshNativeFileViewer = null;
            try { File.Delete(previewPath); } catch { }
            await Task.Yield();
            onBack();
        });
        var mediaElement = new MediaPlayerElement
        {
            AreTransportControlsEnabled = true,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            MinHeight = isAudio ? 180 : 360,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(mediaElement, isAudio ? "Audio player" : "Video player");
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(previewPath);
            player.Source = MediaSource.CreateFromStorageFile(file);
            mediaElement.SetMediaPlayer(player);
            player.Play();
        }
        catch
        {
            player.Dispose();
            try { File.Delete(previewPath); } catch { }
            throw;
        }

        var heading = Text(title, 16, true);
        heading.TextTrimming = TextTrimming.CharacterEllipsis;
        heading.VerticalAlignment = VerticalAlignment.Center;
        var typeLabel = Text(isAudio ? "Audio" : "Video", 12);
        typeLabel.Foreground = ThemeBrush("SchoologyMutedTextBrush");
        typeLabel.VerticalAlignment = VerticalAlignment.Center;
        var download = Action(string.Empty, async (_, _) =>
            await SaveOriginalMaterialAsync(originalUrl, originalFilename, originalMime));
        download.Content = new FontIcon
        {
            Glyph = "\uE896",
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 16
        };
        AutomationProperties.SetName(download, "Download original file");
        ToolTipService.SetToolTip(download, "Download original");

        Grid.SetColumn(back, 0);
        toolbar.Children.Add(back);
        var titleStack = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(heading);
        titleStack.Children.Add(typeLabel);
        Grid.SetColumn(titleStack, 1);
        toolbar.Children.Add(titleStack);
        Grid.SetColumn(download, 2);
        toolbar.Children.Add(download);

        var status = Text("", 12);
        status.Foreground = ThemeBrush("SchoologyMutedTextBrush");
        Grid.SetRow(toolbar, 0);
        viewer.Children.Add(toolbar);
        Grid.SetRow(mediaElement, 1);
        viewer.Children.Add(mediaElement);
        Grid.SetRow(status, 2);
        viewer.Children.Add(status);

        back.IsEnabled = true;
        _activeNativeFileViewerRoot = viewer;
        _refreshNativeFileViewer = () =>
        {
            viewer.Background = new SolidColorBrush(Colors.Transparent);
            status.Foreground = ThemeBrush("SchoologyMutedTextBrush");
            typeLabel.Foreground = ThemeBrush("SchoologyMutedTextBrush");
        };
        if (_sectionFrame is not null) NavigateMainSection(viewer, title);
        else Reset(BuildApplicationShell(title, viewer));
    }

    private async Task ShowNativeFileViewerAsync(
        string previewPath, string title, bool isPdf, string originalUrl, string originalFilename,
        string originalMime, Action onBack)
    {
        // Keep the document canvas transparent so the app's normal shell surface shows through.
        var viewer = new Grid { Background = new SolidColorBrush(Colors.Transparent), RowSpacing = 8 };
        viewer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        viewer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        viewer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var toolbar = new Grid { ColumnSpacing = 8 };
        for (var i = 0; i < 8; i++)
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        var pdfPagesStack = new StackPanel
        {
            Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var pdfPageImages = new List<Image>();
        var viewerScroll = new ScrollViewer
        {
            ZoomMode = ZoomMode.Enabled,
            MinZoomFactor = 0.5f,
            MaxZoomFactor = 4f,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Colors.Transparent)
        };
        var image = new Image
        {
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 1500,
            Margin = new Thickness(12)
        };
        viewerScroll.Content = image;

        PdfDocument? pdf = null;
        InMemoryRandomAccessStream? pdfStream = null;
        uint currentPage = 0;
        var renderGate = new SemaphoreSlim(1, 1);
        var viewerClosing = false;
        viewerScroll.SizeChanged += (_, args) =>
        {
            var pageWidth = Math.Max(240, args.NewSize.Width - 48);
            pdfPagesStack.Width = Math.Max(240, args.NewSize.Width);
            foreach (var pageImage in pdfPageImages) pageImage.Width = pageWidth;
        };
        Button previous = null!;
        Button next = null!;
        var status = Text("", 12);
        status.Foreground = ThemeBrush("SchoologyMutedTextBrush");
        Button back = null!;
        back = Action("‹ Back", async (_, _) =>
        {
            if (viewerClosing) return;
            viewerClosing = true;
            back.IsEnabled = false;
            previous.IsEnabled = false;
            next.IsEnabled = false;
            _activeNativeFileViewerRoot = null;
            _refreshNativeFileViewer = null;
            await renderGate.WaitAsync();
            try
            {
                pdf = null;
                image.Source = null;
                foreach (var pageImage in pdfPageImages) pageImage.Source = null;
                pdfPageImages.Clear();
                pdfPagesStack.Children.Clear();
                viewerScroll.Content = null;
                pdfStream?.Dispose();
                pdfStream = null;
            }
            finally { renderGate.Release(); }
            try { File.Delete(previewPath); } catch { }
            await Task.Yield();
            onBack();
        });
        var heading = Text(title, 16, true);
        heading.TextTrimming = TextTrimming.CharacterEllipsis;
        heading.VerticalAlignment = VerticalAlignment.Center;
        var pageLabel = Text(isPdf ? "Loading PDF…" : "Image", 12);
        pageLabel.Foreground = ThemeBrush("SchoologyMutedTextBrush");
        pageLabel.VerticalAlignment = VerticalAlignment.Center;
        void ScrollToCurrentPdfPage()
        {
            if (pdf is null || currentPage >= pdfPageImages.Count) return;
            pageLabel.Text = (currentPage + 1) + " / " + pdf.PageCount;
            pdfPageImages[(int)currentPage].StartBringIntoView();
            previous.IsEnabled = currentPage > 0;
            next.IsEnabled = currentPage + 1 < pdf.PageCount;
        }
        viewerScroll.ViewChanged += (_, _) =>
        {
            if (viewerClosing || pdf is null || pdfPageImages.Count == 0) return;
            var viewportCenter = viewerScroll.VerticalOffset + viewerScroll.ViewportHeight / 2;
            var closestPage = currentPage;
            var closestDistance = double.MaxValue;
            for (var index = 0; index < pdfPageImages.Count; index++)
            {
                var pageImage = pdfPageImages[index];
                if (pageImage.ActualHeight <= 0) continue;
                var top = pageImage.TransformToVisual(pdfPagesStack).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                var distance = Math.Abs(top + pageImage.ActualHeight / 2 - viewportCenter);
                if (distance < closestDistance) { closestDistance = distance; closestPage = (uint)index; }
            }
            currentPage = closestPage;
            pageLabel.Text = (currentPage + 1) + " / " + pdf.PageCount;
            previous.IsEnabled = currentPage > 0;
            next.IsEnabled = currentPage + 1 < pdf.PageCount;
        };
        previous = Action("‹", (_, _) =>
        {
            if (viewerClosing || pdf is null || currentPage == 0) return;
            currentPage--;
            ScrollToCurrentPdfPage();
        });
        next = Action("›", (_, _) =>
        {
            if (viewerClosing || pdf is null || currentPage + 1 >= pdf.PageCount) return;
            currentPage++;
            ScrollToCurrentPdfPage();
        });
        var zoomOut = Action("−", (_, _) => viewerScroll.ChangeView(null, null, Math.Max(0.5f, viewerScroll.ZoomFactor - 0.2f)));
        var zoomIn = Action("+", (_, _) => viewerScroll.ChangeView(null, null, Math.Min(4f, viewerScroll.ZoomFactor + 0.2f)));
        var download = Action(string.Empty, async (_, _) =>
            await SaveOriginalMaterialAsync(originalUrl, originalFilename, originalMime));
        download.Content = new FontIcon
        {
            Glyph = "\uE896",
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 16
        };
        AutomationProperties.SetName(download, "Download original file");
        ToolTipService.SetToolTip(download, "Download original");
        Grid.SetColumn(back, 0); toolbar.Children.Add(back);
        Grid.SetColumn(heading, 1); toolbar.Children.Add(heading);
        Grid.SetColumn(pageLabel, 2); toolbar.Children.Add(pageLabel);
        Grid.SetColumn(previous, 3); toolbar.Children.Add(previous);
        Grid.SetColumn(next, 4); toolbar.Children.Add(next);
        Grid.SetColumn(zoomOut, 5); toolbar.Children.Add(zoomOut);
        Grid.SetColumn(zoomIn, 6); toolbar.Children.Add(zoomIn);
        Grid.SetColumn(download, 7); toolbar.Children.Add(download);
        if (!isPdf)
        {
            previous.Visibility = Visibility.Collapsed;
            next.Visibility = Visibility.Collapsed;
        }

        Grid.SetRow(toolbar, 0); viewer.Children.Add(toolbar);
        Grid.SetRow(viewerScroll, 1); viewer.Children.Add(viewerScroll);
        Grid.SetRow(status, 2); viewer.Children.Add(status);

        if (isPdf)
        {
            try
            {
                var pdfBytes = await File.ReadAllBytesAsync(previewPath);
                pdfStream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(pdfStream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(pdfBytes);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                    writer.DetachStream();
                }
                pdfStream.Seek(0);
                pdf = await PdfDocument.LoadFromStreamAsync(pdfStream);
                if (pdf.PageCount == 0) throw new InvalidOperationException("The PDF does not contain any pages.");
                viewerScroll.Content = pdfPagesStack;
                var pageWidth = Math.Max(240, viewerScroll.ActualWidth - 48);
                pdfPagesStack.Width = Math.Max(240, viewerScroll.ActualWidth);
                for (uint pageIndex = 0; pageIndex < pdf.PageCount; pageIndex++)
                {
                    var pageImage = new Image
                    {
                        Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Width = pageWidth,
                        MaxWidth = 1500
                    };
                    pdfPageImages.Add(pageImage);
                    pdfPagesStack.Children.Add(new Border
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Background = new SolidColorBrush(Colors.Transparent),
                        BorderBrush = ThemeBrush("SchoologyDividerBrush"),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(8),
                        Child = pageImage
                    });
                    await renderGate.WaitAsync();
                    try { await RenderPdfPageAsync(pdf, pageIndex, pageImage, pageLabel, status); }
                    finally { renderGate.Release(); }
                }
                currentPage = 0;
                pageLabel.Text = "1 / " + pdf.PageCount;
                SetStatusText(status, "");
                previous.IsEnabled = false;
                next.IsEnabled = pdf.PageCount > 1;
                viewerScroll.ChangeView(null, 0, 1f, true);
            }
            catch (Exception ex)
            {
                pdf = null;
                pdfStream?.Dispose();
                pdfStream = null;
                SetStatusText(status, $"Unable to render this PDF: {ex.Message}");
                pageLabel.Text = "Preview unavailable";
            }
        }
        else
        {
            try
            {
                var storageFile = await StorageFile.GetFileFromPathAsync(previewPath);
                using var stream = await storageFile.OpenAsync(FileAccessMode.Read);
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                image.Source = bitmap;
            }
            catch (Exception ex)
            {
                SetStatusText(status, $"Unable to render this image: {ex.Message}");
            }
        }

        _activeNativeFileViewerRoot = viewer;
        _refreshNativeFileViewer = () =>
        {
            viewer.Background = new SolidColorBrush(Colors.Transparent);
            viewerScroll.Background = new SolidColorBrush(Colors.Transparent);
            status.Foreground = ThemeBrush("SchoologyMutedTextBrush");
            pageLabel.Foreground = ThemeBrush("SchoologyMutedTextBrush");
        };
        if (_sectionFrame is not null) NavigateMainSection(viewer, title);
        else Reset(BuildApplicationShell(title, viewer));
    }

    private static async Task RenderPdfPageAsync(
        PdfDocument pdf, uint pageIndex, Image image, TextBlock pageLabel, TextBlock status)
    {
        PdfPage? page = null;
        try
        {
            page = pdf.GetPage(pageIndex);
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream);
            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            image.Source = bitmap;
            pageLabel.Text = $"{pageIndex + 1} / {pdf.PageCount}";
            SetStatusText(status, "");
        }
        catch (Exception ex)
        {
            SetStatusText(status, $"Unable to render PDF page {pageIndex + 1}: {ex.Message}");
        }
        finally { page?.Dispose(); }
    }

    private StackPanel BuildMaterialsList(long sectionId, IReadOnlyList<SchoologyApiRecord> initialRecords)
    {
        var panel = new StackPanel { Spacing = 8 };
        var materialRows = new StackPanel { Spacing = 2 };
        AutomationProperties.SetAutomationId(materialRows, "CourseMaterialsList");

        void AnimateFolderArrow(FontIcon arrow, bool expanded)
        {
            var transform = arrow.RenderTransform as RotateTransform;
            if (transform is null)
            {
                transform = new RotateTransform { Angle = 0 };
                arrow.RenderTransform = transform;
                arrow.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            }
            var targetAngle = expanded ? 180d : 0d;
            if (!AnimationsEnabled)
            {
                transform.Angle = targetAngle;
                return;
            }
            var animation = new DoubleAnimation
            {
                To = targetAngle,
                Duration = TimeSpan.FromMilliseconds(180),
                EnableDependentAnimation = true
            };
            var storyboard = new Storyboard();
            Storyboard.SetTarget(animation, transform);
            Storyboard.SetTargetProperty(animation, "Angle");
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }

        Storyboard? AnimateFolderChildren(StackPanel children, TranslateTransform offset, bool expanded,
            Action? completed = null)
        {
            if (!AnimationsEnabled)
            {
                children.Opacity = 1;
                offset.Y = 0;
                children.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                completed?.Invoke();
                return null;
            }

            var opacity = new DoubleAnimation
            {
                From = expanded ? 0 : 1,
                To = expanded ? 1 : 0,
                Duration = TimeSpan.FromMilliseconds(160),
                EnableDependentAnimation = true
            };
            var slide = new DoubleAnimation
            {
                From = expanded ? -6 : 0,
                To = expanded ? 0 : -6,
                Duration = TimeSpan.FromMilliseconds(180),
                EnableDependentAnimation = true
            };
            var storyboard = new Storyboard();
            Storyboard.SetTarget(opacity, children);
            Storyboard.SetTargetProperty(opacity, "Opacity");
            Storyboard.SetTarget(slide, offset);
            Storyboard.SetTargetProperty(slide, "Y");
            storyboard.Children.Add(opacity);
            storyboard.Children.Add(slide);
            storyboard.Completed += (_, _) => completed?.Invoke();
            storyboard.Begin();
            return storyboard;
        }

        void AddMaterialRows(StackPanel destination, IReadOnlyList<SchoologyApiRecord> records, int depth)
        {
            foreach (var record in records)
            {
                var title = ProfileValue(record.Data, "title", "name") ?? record.Title;
                var isFolder = IsFolderRecord(record.Data);
                var row = new Grid
                {
                    ColumnSpacing = 12,
                    Padding = new Thickness(8 + (depth * 24), 9, 8, 9),
                    MinHeight = 52
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                if (isFolder)
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

                if (isFolder)
                    row.Children.Add(FolderIcons.CreateIcon(FolderColorName(record.Data), 25));
                else
                    row.Children.Add(new FontIcon
                    {
                        Glyph = MaterialGlyph(record.Data),
                        FontSize = 23,
                        Foreground = ThemeBrush("SchoologyBlueBrush"),
                        VerticalAlignment = VerticalAlignment.Center
                    });

                var labels = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
                labels.Children.Add(Text(title, 14, true));
                var materialType = (ProfileValue(record.Data, "type", "resource_type") ?? "").ToLowerInvariant();
                var templateType = (ProfileValue(record.Data, "template_type", "templateType") ?? "").ToLowerInvariant();
                var documentType = (ProfileValue(record.Data, "document_type", "documentType") ?? "").ToLowerInvariant();
                var isPage = materialType == "page" || templateType == "page" || documentType == "page";
                var isAssignment = materialType is "assignment" or "assignment_v2";
                // Schoology's native Materials rows show a page title only; its HTML body
                // belongs in the page viewer, not flattened into a subtitle preview.
                var subtitle = isPage || isAssignment ? null :
                    ProfileValue(record.Data, "description", "body", "due", "due_date");
                if (!string.IsNullOrWhiteSpace(subtitle)) labels.Children.Add(Text(subtitle, 12));
                Grid.SetColumn(labels, 1);
                row.Children.Add(labels);

                FontIcon? arrow = null;
                if (isFolder)
                {
                    arrow = new FontIcon
                    {
                        Glyph = "\uE70D",
                        FontSize = 14,
                        Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        RenderTransform = new RotateTransform { Angle = 0 }
                    };
                    arrow.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
                    Grid.SetColumn(arrow, 2);
                    row.Children.Add(arrow);
                }

                var wrapper = new StackPanel { Spacing = 0 };
                var childOffset = new TranslateTransform();
                StackPanel? children = null;
                Storyboard? childrenStoryboard = null;
                var expanded = false;
                var loaded = false;
                var loading = false;
                var item = new ListViewItem
                {
                    Content = row,
                    Tag = record,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(0)
                };
                wrapper.Children.Add(item);
                item.Tapped += async (_, args) =>
                {
                    args.Handled = true;
                    if (!isFolder)
                    {
                        await OpenMaterialAsync(record);
                        return;
                    }
                    if (expanded)
                    {
                        expanded = false;
                        if (children is not null)
                        {
                            childrenStoryboard?.Stop();
                            childrenStoryboard = null;
                            childrenStoryboard = AnimateFolderChildren(children, childOffset, false, () =>
                            {
                                if (!expanded) children.Visibility = Visibility.Collapsed;
                            });
                        }
                        if (arrow is not null) AnimateFolderArrow(arrow, false);
                        return;
                    }
                    expanded = true;
                    if (children is null)
                    {
                        children = new StackPanel
                        {
                            Spacing = 2,
                            Visibility = Visibility.Visible,
                            Opacity = 1,
                            RenderTransform = childOffset
                        };
                        wrapper.Children.Add(children);
                    }
                    childrenStoryboard?.Stop();
                    childrenStoryboard = null;
                    children.Visibility = Visibility.Visible;
                    if (arrow is not null) AnimateFolderArrow(arrow, true);

                    // Loaded folders animate immediately. During the first request,
                    // keep the loading indicator stable and animate the real contents
                    // only after they have been inserted.
                    if (loaded)
                    {
                        childrenStoryboard = AnimateFolderChildren(children, childOffset, true);
                        return;
                    }
                    if (loading)
                    {
                        children.Opacity = 1;
                        childOffset.Y = 0;
                        return;
                    }

                    var folderId = JsonLong(record.Data, "id", "folder_id", "folderId");
                    if (folderId <= 0 || _session is null)
                    {
                        expanded = false;
                        children.Visibility = Visibility.Collapsed;
                        if (arrow is not null) AnimateFolderArrow(arrow, false);
                        return;
                    }

                    loading = true;
                    children.Opacity = 1;
                    childOffset.Y = 0;
                    var loadingStatus = new TextBlock
                    {
                        Text = "Loading folder…",
                        Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                        Margin = new Thickness(24, 8, 8, 8),
                        Foreground = ThemeBrush("TextFillColorSecondaryBrush")
                    };
                    children.Children.Add(loadingStatus);
                    try
                    {
                        using var folder = await _api.GetCourseFolderAsync(_session, sectionId, folderId);
                        var nestedRecords = SchoologyApiClient.ReadRecords(folder.RootElement,
                            "folder-item", "folder_item", "folderItems", "items");
                        children.Children.Clear();
                        if (nestedRecords.Count == 0)
                        {
                            children.Children.Add(new TextBlock
                            {
                                Text = "This folder is empty.",
                                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                                Margin = new Thickness(24, 8, 8, 8),
                                Foreground = ThemeBrush("TextFillColorSecondaryBrush")
                            });
                        }
                        else AddMaterialRows(children, nestedRecords, depth + 1);
                        loaded = true;
                    }
                    catch (Exception ex)
                    {
                        children.Children.Clear();
                        children.Children.Add(new TextBlock
                        {
                            Text = $"Unable to load folder: {ex.Message}",
                            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                            Margin = new Thickness(24, 8, 8, 8),
                            Foreground = ThemeBrush("SystemFillColorCriticalBrush"),
                            TextWrapping = TextWrapping.Wrap
                        });
                    }
                    finally
                    {
                        loading = false;
                        if (expanded)
                        {
                            children.Opacity = 0;
                            childOffset.Y = -6;
                            children.Visibility = Visibility.Visible;
                            childrenStoryboard?.Stop();
                            childrenStoryboard = AnimateFolderChildren(children, childOffset, true);
                        }
                        else
                        {
                            children.Visibility = Visibility.Collapsed;
                            children.Opacity = 0;
                            childOffset.Y = -6;
                        }
                    }
                };
                destination.Children.Add(wrapper);
            }
        }

        async Task OpenMaterialAsync(SchoologyApiRecord record)
        {
            var title = ProfileValue(record.Data, "title", "name", "filename", "file_name") ?? record.Title;
            var materialType = (ProfileValue(record.Data, "type", "resource_type", "template_type", "templateType",
                "document_type", "documentType") ?? "").ToLowerInvariant();
            var materialId = JsonLong(record.Data, "assignment_id", "assignmentId", "page_id", "pageId",
                "id", "document_id", "documentId", "template_id", "templateID");
            if (materialType == "page" && materialId > 0)
            {
                await ShowCoursePageAsync(sectionId, record);
                return;
            }
            if (materialType == "discussion")
            {
                var discussionId = JsonLong(record.Data, "discussion_id", "discussionId", "id");
                if (discussionId > 0)
                {
                    await ShowDiscussionDetailsAsync("Courses", sectionId, record);
                    return;
                }
            }
            if ((materialType is "assignment" or "assignment_v2") && materialId > 0 && _session is not null)
            {
                await ShowAssignmentDetailsAsync(sectionId, record);
                return;
            }
            if (materialType is "assessment" or "assessment_v2" or "managed_assessment" or "quiz" or "test")
            {
                if (materialId > 0)
                {
                    await OpenSharedWebViewAsync($"https://app.schoology.com/assignment/{materialId}", title,
                        allowLtiExternal: true,
                        onBack: () => ShowThreePane("Courses", _courses.FirstOrDefault(course => course.Id == sectionId)));
                    return;
                }
            }
            if ((materialType is "lti" or "external_tool" or "scorm") && materialId > 0)
            {
                var route = materialType == "external_tool" ? "external-tool" : materialType;
                await OpenSharedWebViewAsync($"https://app.schoology.com/section/{sectionId}/{route}/{materialId}",
                    title, allowLtiExternal: true,
                    onBack: () => ShowThreePane("Courses", _courses.FirstOrDefault(course => course.Id == sectionId)));
                return;
            }
            var fileAttachment = FirstFileAttachment(record.Data);
            var originalFileUrl = ProfileValue(fileAttachment, "file_download_url", "fileDownloadURL",
                "download_path", "downloadPath", "download_url", "downloadUrl", "resolveDownloadUrl", "url") ??
                ProfileValue(record.Data, "file_download_url", "fileDownloadURL", "download_path", "downloadPath");
            var convertedFileUrl = ProfileValue(fileAttachment, "file_converted_download_url", "fileConvertedDownloadURL",
                "converted_download_path", "convertedDownloadPath") ??
                ProfileValue(record.Data, "file_converted_download_url", "fileConvertedDownloadURL", "converted_download_path", "convertedDownloadPath");
            var convertedStatus = ProfileValue(fileAttachment, "converted_status", "convertedStatus")?.ToLowerInvariant() ?? "";
            var originalFileName = ProfileValue(fileAttachment, "filename", "fileName", "title") ??
                ProfileValue(record.Data, "filename", "file_name", "fileName", "title", "name") ?? title;
            var originalFileMime = ProfileValue(fileAttachment, "filemime", "fileMIME", "mime", "mime_type") ??
                ProfileValue(record.Data, "filemime", "fileMIME", "mime_type") ?? "application/octet-stream";
            var documentType = ProfileValue(record.Data, "document_type", "documentType",
                "template_document_type", "templateDocumentType")?.ToLowerInvariant() ?? "";
            if (!string.IsNullOrWhiteSpace(originalFileUrl) &&
                (materialType is "file" or "document" || documentType == "file" || fileAttachment.ValueKind == JsonValueKind.Object))
            {
                var useConverted = !string.IsNullOrWhiteSpace(convertedFileUrl) &&
                    (convertedStatus.Length == 0 || convertedStatus is "success" or "converted" or "1");
                await ShowAttachmentOptionsAsync(
                    fileAttachment.ValueKind == JsonValueKind.Object ? fileAttachment : record.Data,
                    () => ShowThreePane("Courses", _courses.FirstOrDefault(course => course.Id == sectionId)));
                return;
            }

            var rawUrl = ProfileValue(record.Data, "web_url", "webUrl", "launch_url", "launchUrl",
                "external_url", "externalUrl", "url", "href", "link", "link_url", "linkURL",
                "location", "download_url", "downloadUrl", "file_download_url", "fileDownloadURL",
                "download_path", "downloadPath", "file_url", "fileUrl");
            var linkAttachment = FirstLinkAttachment(record.Data);
            if (string.IsNullOrWhiteSpace(rawUrl))
                rawUrl = ProfileValue(linkAttachment, "link_url", "linkURL", "linkUrl", "url", "href");
            if (((materialType is "link" or "video") || (documentType is "link" or "external_tool")) &&
                materialId > 0 && _session is not null)
            {
                using var documentDetails = await _api.GetDocumentAsync(_session, sectionId, materialId);
                var documentData = documentDetails.RootElement.TryGetProperty("document", out var nestedDocument)
                    ? nestedDocument : documentDetails.RootElement;
                if (string.IsNullOrWhiteSpace(rawUrl))
                    rawUrl = ProfileValue(documentData, "web_url", "webUrl", "url", "href",
                        "link_url", "linkURL", "linkUrl", "launch_url", "launchUrl", "location");
                if (string.IsNullOrWhiteSpace(rawUrl))
                {
                    linkAttachment = FirstLinkAttachment(documentData);
                    rawUrl = ProfileValue(linkAttachment, "link_url", "linkURL", "linkUrl", "url", "href");
                }
            }
            if (!string.IsNullOrWhiteSpace(rawUrl))
            {
                if (await TryOpenDocumentAttachmentAsync(rawUrl,
                    () => ShowThreePane("Courses", _courses.FirstOrDefault(course => course.Id == sectionId))))
                    return;
                if (!rawUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                    !rawUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    rawUrl = "https://app.schoology.com/" + rawUrl.TrimStart('/');
                if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                {
                    var type = (ProfileValue(record.Data, "type", "resource_type", "document_type", "documentType") ?? "").ToLowerInvariant();
                    var isLtiOrAssessment = type.Contains("lti") || type.Contains("external_tool") ||
                        documentType.Contains("external_tool", StringComparison.Ordinal) ||
                        type.Contains("assessment") || type.Contains("quiz") || type.Contains("test") ||
                type.Contains("embed") || type.Contains("scorm");
                    if (isLtiOrAssessment)
                        await OpenSharedWebViewAsync(uri.AbsoluteUri, title, allowLtiExternal: true,
                            onBack: () => ShowThreePane("Courses", _courses.FirstOrDefault(course => course.Id == sectionId)));
                    else
                        await OpenExternalLinkAsync(uri.AbsoluteUri);
                    return;
                }
            }
            var details = new StackPanel { Spacing = 8 };
            details.Children.Add(Text(title, 18, true));
            var description = ProfileValue(record.Data, "description", "body", "due", "due_date", "type");
            if (!string.IsNullOrWhiteSpace(description)) details.Children.Add(Text(StripMarkup(description), 13));
            details.Children.Add(Text("ClassWire did not provide an embeddable HTTPS link for this material.", 12));
            await new ContentDialog { Title = "Material details", Content = CreateDialogScrollViewport(details, 560),
                CloseButtonText = "Close", XamlRoot = _root.XamlRoot, RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme }.ShowAsync();
        }

        AddMaterialRows(materialRows, initialRecords, 0);
        if (initialRecords.Count == 0)
            materialRows.Children.Add(EmptyState("This folder is empty"));
        panel.Children.Add(materialRows);
        return panel;
    }

    private StackPanel BuildMembersList(IReadOnlyList<SchoologyApiRecord> records, string kind)
    {
        var people = records.Select(record =>
        {
            var data = record.Data;
            var user = data.ValueKind == System.Text.Json.JsonValueKind.Object && data.TryGetProperty("user", out var nested) ? nested : data;
            var id = JsonLong(data, "uid", "user_id", "userId", "id");
            if (id <= 0) id = JsonLong(user, "id", "uid", "user_id");
            var first = ProfileValue(user, "name_first", "first_name") ?? ProfileValue(data, "name_first", "first_name") ?? "";
            var last = ProfileValue(user, "name_last", "last_name") ?? ProfileValue(data, "name_last", "last_name") ?? "";
            var name = ProfileValue(user, "name_display", "display_name", "name") ?? ProfileValue(data, "name_display", "display_name", "name")
                ?? string.Join(" ", new[] { first, last }.Where(value => !string.IsNullOrWhiteSpace(value)));
            var picture = ProfileValue(user, "picture_url", "pictureUrl", "picture") ?? ProfileValue(data, "picture_url", "pictureUrl", "picture");
            var role = ProfileValue(data, "role_name", "roleName", "role", "position") ?? ProfileValue(user, "role_name", "roleName", "role");
            var admin = JsonBoolean(data, "admin", "is_admin", "isAdmin", "owner", "is_owner") ||
                JsonBoolean(user, "admin", "is_admin", "isAdmin", "owner", "is_owner") ||
                (!string.IsNullOrWhiteSpace(role) && System.Text.RegularExpressions.Regex.IsMatch(role, @"\b(admin|administrator|owner)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
            return new { Id = id, First = first, Last = last, Name = string.IsNullOrWhiteSpace(name) ? "ClassWire member" : name, Picture = picture, Role = role, Admin = admin };
        }).Where(person => person.Id > 0).ToList();
        var root = new StackPanel { Spacing = 8 };
        var filters = new SelectorBar { HorizontalAlignment = HorizontalAlignment.Left };
        var all = new SelectorBarItem { Text = "All" };
        var members = new SelectorBarItem { Text = "Members" };
        var admins = new SelectorBarItem { Text = "Admins" };
        filters.Items.Add(all); filters.Items.Add(members); filters.Items.Add(admins); filters.SelectedItem = all;
        var search = new TextBox { PlaceholderText = $"Search {kind.ToLowerInvariant()} members" };
        var list = new ListView { SelectionMode = ListViewSelectionMode.None,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            Height = Math.Max(300, (_root.ActualHeight > 0 ? _root.ActualHeight : 820) - 190) };
        SizeChangedEventHandler memberListResize = (_, args) => list.Height = Math.Max(300, args.NewSize.Height - 190);
        _root.SizeChanged += memberListResize;
        root.Unloaded += (_, _) => _root.SizeChanged -= memberListResize;
        AutomationProperties.SetAutomationId(list, kind + "MembersList");
        void Render()
        {
            list.Items.Clear();
            var query = search.Text.Trim();
            var visible = people.Where(person =>
                (filters.SelectedItem == all || (filters.SelectedItem == admins ? person.Admin : !person.Admin)) &&
                (query.Length == 0 || person.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                 person.Last.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
                .OrderByDescending(person => person.Admin).ThenBy(person => person.Last, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(person => person.First, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (visible.Count == 0)
            {
                list.Items.Add(new ListViewItem { Content = Text(filters.SelectedItem == admins ? "No admins found." :
                    filters.SelectedItem == members ? "No members found." : "No members were returned by ClassWire.", 13) });
                return;
            }
            var lastHeader = "";
            foreach (var person in visible)
            {
                var header = filters.SelectedItem == all && person.Admin ? "Admins" :
                    (string.IsNullOrWhiteSpace(person.Last) ? "#" : person.Last[..1].ToUpperInvariant());
                if (header != lastHeader)
                {
                    list.Items.Add(new ListViewItem { IsEnabled = false, Content = Text(header, 12, true),
                        Background = ThemeBrush("SchoologySubtleBrush"), Padding = new Thickness(10, 6, 10, 6) });
                    lastHeader = header;
                }
                var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(8, 7, 8, 7) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var avatar = new Image { Width = 40, Height = 40, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
                if (!string.IsNullOrWhiteSpace(person.Picture))
                    _ = LoadPostImageAsync(avatar, person.Picture);
                else _ = LoadMemberAvatarAsync(person.Id, avatar);
                row.Children.Add(new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20),
                    Background = ThemeBrush("SchoologySubtleBrush"), Child = avatar });
                var labels = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                labels.Children.Add(Text(person.Name, 14, true));
                if (!string.IsNullOrWhiteSpace(person.Role)) labels.Children.Add(Text(person.Role, 11));
                Grid.SetColumn(labels, 1); row.Children.Add(labels);
                var memberItem = new ListViewItem { Content = row, Tag = person.Id,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch };
                memberItem.Tapped += (_, _) => ShowUserProfile(person.Id);
                list.Items.Add(memberItem);
            }
        }
        filters.SelectionChanged += (_, _) => Render();
        search.TextChanged += (_, _) => Render();
        Render();
        root.Children.Add(filters); root.Children.Add(search); root.Children.Add(list);
        if (people.Count == 0) root.Children.Add(Text("The enrollment response contained no recognizable user IDs.", 12));
        return root;
    }



    private static string NormalizeSchoologyImageUrl(string value)
    {
        var url = value.Trim();
        if (url.StartsWith("//", StringComparison.Ordinal)) return "https:" + url;
        if (url.StartsWith('/')) return "https://app.schoology.com" + url;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return "https://" + url[7..];
        return url;
    }

    private async Task LoadPostImageAsync(Image image, string rawUrl, UIElement? fallback = null)
    {
        var session = _session;
        if (session is null || string.IsNullOrWhiteSpace(rawUrl)) return;

        var normalizedUrl = NormalizeSchoologyImageUrl(rawUrl);
        var cacheKey = $"{session.UserId}:{normalizedUrl}";
        var cachedLoad = _postImageCache.GetOrAdd(cacheKey, _ =>
            new Lazy<Task<BitmapImage>>(
                () => DownloadAndDecodePostImageAsync(session, normalizedUrl),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            var bitmap = await cachedLoad.Value;
            if (_session?.UserId != session.UserId) return;
            image.Source = bitmap;
            image.Visibility = Visibility.Visible;
            if (fallback is not null) fallback.Visibility = Visibility.Collapsed;

            // Keep the decoded-image cache bounded for long-lived sessions.
            if (_postImageCache.Count > 128)
            {
                foreach (var staleKey in _postImageCache.Keys.Take(24))
                {
                    if (!string.Equals(staleKey, cacheKey, StringComparison.Ordinal))
                        _postImageCache.TryRemove(staleKey, out _);
                }
            }
        }
        catch
        {
            _postImageCache.TryRemove(cacheKey, out _);
            image.Visibility = Visibility.Collapsed;
            if (fallback is not null) fallback.Visibility = Visibility.Visible;
        }
    }

    private async Task<BitmapImage> DownloadAndDecodePostImageAsync(
        SchoologyAuthService.Session session, string normalizedUrl)
    {
        await _postImageDownloadGate.WaitAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            var bytes = await _auth.GetSchoologyFileBytesAsync(
                session, normalizedUrl, cancellationToken: timeout.Token);
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        finally
        {
            _postImageDownloadGate.Release();
        }
    }

    private async Task LoadMemberAvatarAsync(long userId, Image image)
    {
        try
        {
            if (_session is null || userId <= 0) return;
            using var user = await _api.GetUserAsync(_session, userId);
            var data = user.RootElement.TryGetProperty("user", out var nested) ? nested : user.RootElement;
            var picture = ProfileValue(data, "picture_url", "pictureUrl", "picture", "photo_url", "avatar_url");
            if (string.IsNullOrWhiteSpace(picture)) return;
            var bytes = await _auth.GetSchoologyFileBytesAsync(_session, NormalizeSchoologyImageUrl(picture));
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            image.Source = bitmap;
        }
        catch { }
    }

    private Border BuildProfileAvatar(JsonElement data, long userId, double size = 38)
    {
        var profile = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("user", out var nested)
            ? nested : data;
        var picture = ProfileValue(profile, "picture_url", "pictureUrl", "picture", "photo_url", "avatar_url") ??
            ProfileValue(data, "picture_url", "pictureUrl", "picture", "photo_url", "avatar_url");
        var image = new Image { Width = size, Height = size,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
        if (!string.IsNullOrWhiteSpace(picture))
        {
            _ = LoadPostImageAsync(image, picture);
        }
        else if (userId > 0) _ = LoadMemberAvatarAsync(userId, image);
        else image.Source = null;
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2),
            Background = ThemeBrush("SchoologySubtleBrush"), Clip = null, Child = image };
    }

    private StackPanel BuildCommentThreadRows(IReadOnlyList<SchoologyApiRecord> comments,
        long parentEntityId, Action<long, string>? onReply = null)
    {
        var root = new StackPanel { Spacing = 8 };
        if (comments.Count == 0) return root;

        var byId = comments
            .Select(comment => (Comment: comment, Id: JsonLong(comment.Data, "id", "comment_id", "commentId"),
                ParentId: JsonLong(comment.Data, "parent_id", "parentId", "parent_comment_id", "parentCommentId")))
            .Where(item => item.Id > 0)
            .ToArray();
        var knownIds = byId.Select(item => item.Id).ToHashSet();
        var children = byId.GroupBy(item => item.ParentId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Comment).ToArray());
        var rendered = new HashSet<long>();

        void AppendComment(StackPanel destination, SchoologyApiRecord comment)
        {
            var id = JsonLong(comment.Data, "id", "comment_id", "commentId");
            if (id > 0 && !rendered.Add(id)) return;

            var item = new StackPanel { Spacing = 6 };
            item.Children.Add(BuildCommentRow(comment, parentEntityId, onReply));
            if (id > 0 && children.TryGetValue(id, out var replies) && replies.Length > 0)
            {
                var replyStack = new StackPanel { Spacing = 8, Margin = new Thickness(24, 0, 0, 0) };
                foreach (var reply in replies) AppendComment(replyStack, reply);
                if (replyStack.Children.Count > 0) item.Children.Add(replyStack);
            }
            destination.Children.Add(item);
        }

        if (children.TryGetValue(0, out var topLevel))
            foreach (var comment in topLevel) AppendComment(root, comment);

        // Keep orphaned replies visible and guard against malformed parent cycles.
        foreach (var item in byId)
            if (!rendered.Contains(item.Id) &&
                (item.ParentId <= 0 || !knownIds.Contains(item.ParentId)))
                AppendComment(root, item.Comment);
        foreach (var item in byId)
            if (!rendered.Contains(item.Id)) AppendComment(root, item.Comment);

        return root;
    }

    private Border BuildCommentRow(SchoologyApiRecord comment, long parentEntityId = 0, Action<long, string>? onReply = null)
    {
        var data = comment.Data;
        var user = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("user", out var nested)
            ? nested : data;
        var authorId = JsonLong(data, "uid", "user_id", "userId", "author_id", "authorId");
        if (authorId <= 0) authorId = JsonLong(user, "id", "uid", "user_id");
        var author = ProfileValue(user, "name_display", "display_name", "name", "user_name") ??
            ProfileValue(data, "user_name", "author_name", "name_display", "display_name") ??
            (authorId > 0 && _userProfileCache.TryGetValue(authorId, out var cachedAuthor)
                ? ProfileValue(cachedAuthor, "name_display", "display_name", "name", "user_name") : null) ??
            (authorId > 0 ? "ClassWire user" : "Unknown author");

        // Match the Tauri comment layout: avatar and author share a compact header,
        // while the comment body, timestamp, and actions use the full row width below.
        var content = new StackPanel { Spacing = 6, Padding = new Thickness(10, 8, 10, 8) };
        var header = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Top };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var avatar = BuildProfileAvatar(data, authorId);
        avatar.VerticalAlignment = VerticalAlignment.Top;
        header.Children.Add(avatar);
        var authorText = Text(author, 12, true);
        authorText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(authorText, 1);
        header.Children.Add(authorText);
        content.Children.Add(header);

        var body = ProfileValue(data, "comment_richtext", "commentRichtext", "richtext_comment", "richtextComment", "comment", "body", "text", "message") ?? comment.Subtitle;
        if (!string.IsNullOrWhiteSpace(body))
            content.Children.Add(CreateRichContentWithEmbeds(body, 12));

        var created = ProfileValue(data, "created", "created_at", "timestamp");
        if (!string.IsNullOrWhiteSpace(created))
        {
            var date = Text(ParseSchoologyDate(created)?.ToString("g",
                System.Globalization.CultureInfo.CurrentCulture) ?? created, 10);
            date.Foreground = ThemeBrush("SchoologyMutedTextBrush");
            content.Children.Add(date);
        }

        var commentId = JsonLong(data, "id", "comment_id", "commentId");
        if (parentEntityId > 0 && commentId > 0)
        {
            var commentActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            if (onReply is not null)
                commentActions.Children.Add(Action("Reply", (_, _) => onReply(commentId, author)));
            var liked = JsonBoolean(data, "user_like_action", "user_liked", "userLiked", "liked", "is_liked", "isLiked");
            var likes = JsonLong(data, "likes", "like_count", "likeCount");
            var likeSplit = new SplitButton
            {
                Content = liked ? "Unlike" : "Like",
                Padding = new Thickness(8, 4, 8, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = ThemeBrush("SchoologySubtleBrush"),
                BorderBrush = ThemeBrush("SchoologyDividerBrush")
            };
            var likeMenu = new MenuFlyout();
            var countItem = new MenuFlyoutItem
            {
                Text = likes > 0 ? $"View {likes} likes" : "No likes yet",
                IsEnabled = likes > 0
            };
            countItem.Click += async (_, _) => await ShowCommentLikesDialogAsync(parentEntityId, commentId);
            likeMenu.Items.Add(countItem);
            likeSplit.Flyout = likeMenu;
            likeSplit.Click += async (_, _) =>
            {
                if (_session is null) return;
                likeSplit.IsEnabled = false;
                try
                {
                    using var result = await _api.SetCommentLikeAsync(_session, parentEntityId, commentId, !liked);
                    liked = !liked;
                    likes = Math.Max(0, likes + (liked ? 1 : -1));
                    likeSplit.Content = liked ? "Unlike" : "Like";
                    countItem.Text = likes > 0 ? $"View {likes} likes" : "No likes yet";
                    countItem.IsEnabled = likes > 0;
                }
                catch (Exception ex) { await ShowApiErrorAsync("Unable to update comment like", ex.Message); }
                finally { likeSplit.IsEnabled = true; }
            };
            commentActions.Children.Add(likeSplit);
            content.Children.Add(commentActions);
        }

        return Surface(content, new Thickness(0));
    }

    private static bool IsFolderRecord(JsonElement data)
    {
        var type = ProfileValue(data, "type", "templateDocumentType") ?? string.Empty;
        if (type.Equals("folder", StringComparison.OrdinalIgnoreCase)) return true;
        return data.TryGetProperty("template_fields", out var fields) &&
            (ProfileValue(fields, "document_type", "documentType") ?? string.Empty).Equals("folder", StringComparison.OrdinalIgnoreCase);
    }
    private static string FolderColorName(JsonElement data)
    {
        var color = (ProfileValue(data, "color", "folder_color", "folderColor") ?? "gray").Trim().ToLowerInvariant();
        return color is "black" or "blue" or "gray" or "green" or "orange" or "pink" or "purple" or "red" or "yellow"
            ? color : "gray";
    }
    private static JsonElement FindJsonPropertyRecursive(JsonElement root, params string[] keys)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
                if (keys.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                    return property.Value;
            foreach (var property in root.EnumerateObject())
            {
                var found = FindJsonPropertyRecursive(property.Value, keys);
                if (found.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)) return found;
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var found = FindJsonPropertyRecursive(item, keys);
                if (found.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)) return found;
            }
        }
        return default;
    }

    private static JsonElement[] ReadJsonItems(JsonElement value, params string[] collectionKeys)
    {
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().Select(item => item.Clone()).ToArray();
        if (value.ValueKind != JsonValueKind.Object) return Array.Empty<JsonElement>();

        foreach (var key in collectionKeys)
            if (value.TryGetProperty(key, out var nested) &&
                (nested.ValueKind is JsonValueKind.Array or JsonValueKind.Object))
            {
                var items = ReadJsonItems(nested, collectionKeys);
                if (items.Length > 0) return items;
            }

        return JsonLong(value, "id", "poll_option_id", "pollOptionId", "pollOptionID") > 0 ||
            ProfileValue(value, "title", "name", "text") is not null
            ? new[] { value.Clone() }
            : Array.Empty<JsonElement>();
    }

    private static List<(JsonElement Item, string Kind)> ReadAttachmentItems(JsonElement root)
    {
        var result = new List<(JsonElement Item, string Kind)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(JsonElement node, string kind, int depth)
        {
            if (depth > 8 || node.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return;
            if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in node.EnumerateArray()) Visit(child, kind, depth + 1);
                return;
            }
            if (node.ValueKind != JsonValueKind.Object) return;

            var declaredType = ProfileValue(node, "type", "attachment_type", "attachmentType")?.ToLowerInvariant() ?? "";
            var mime = ProfileValue(node, "filemime", "fileMIME", "mime", "mime_type", "mimeType") ?? "";
            var fileUrl = ProfileValue(node, "file_download_url", "fileDownloadURL", "download_path",
                "downloadPath", "download_url", "downloadUrl", "resolveDownloadUrl", "resolve_download_url");
            var convertedUrl = ProfileValue(node, "file_converted_download_url", "fileConvertedDownloadURL",
                "converted_download_path", "convertedDownloadPath");
            var url = ProfileValue(node, "url", "href", "link_url", "linkURL", "linkUrl", "video_url", "videoURL", "videoUrl");
            var thumbnail = ProfileValue(node, "thumbnail", "thumbnail_url", "thumbnailUrl", "preview_url", "previewUrl", "fileThumbnailURL", "file_thumbnail_url", "videoThumbnailURL", "video_thumbnail_url");
            var itemKind = kind;
            if (itemKind == "container")
            {
                if (declaredType.Contains("file") || declaredType is "image" or "photo" ||
                    fileUrl is not null || convertedUrl is not null ||
                    mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    itemKind = "file";
                else if (declaredType.Contains("embed") || ProfileValue(node, "embed_code", "embedCode") is not null) itemKind = "embed";
                else if (declaredType.Contains("video")) itemKind = "video";
                else if (declaredType.Contains("link") || url is not null) itemKind = "link";
                else if (thumbnail is not null) itemKind = "file";
            }

            if (itemKind is "file" or "link" or "video" or "embed")
            {
                var key = string.Join("|", ProfileValue(node, "id", "fid", "file_id", "fileId") ?? "",
                    fileUrl ?? convertedUrl ?? url ?? thumbnail ?? "",
                    ProfileValue(node, "filename", "fileName", "title", "name") ?? "");
                if (key != "||" && seen.Add(itemKind + "|" + key))
                    result.Add((node.Clone(), itemKind));
            }

            foreach (var property in node.EnumerateObject())
            {
                var key = property.Name.Replace("_", "").Replace("-", "").ToLowerInvariant();
                switch (key)
                {
                    case "attachments":
                    case "attachment": Visit(property.Value, "container", depth + 1); break;
                    case "files":
                    case "file":
                    case "fileattachment":
                    case "fileattachments": Visit(property.Value, "file", depth + 1); break;
                    case "links":
                    case "link": Visit(property.Value, "link", depth + 1); break;
                    case "videos":
                    case "video": Visit(property.Value, "video", depth + 1); break;
                    case "embeds":
                    case "embed": Visit(property.Value, "embed", depth + 1); break;
                    case "externaltools":
                    case "externaltool": Visit(property.Value, "link", depth + 1); break;
                    case "annotationfiles":
                    case "images":
                    case "image":
                    case "photos":
                    case "photo": Visit(property.Value, "file", depth + 1); break;
                }
            }
        }
        Visit(root, "container", 0);
        return result;
    }

    private async Task OpenExternalLinkAsync(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)) return;
        var normalized = NormalizeSchoologyWebUrl(rawUrl);
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("ClassWire returned an invalid external link.");
        var opened = await Windows.System.Launcher.LaunchUriAsync(uri);
        if (!opened) await ShowApiErrorAsync("Unable to open link", "Windows could not open this link in the default browser.");
    }

    private async Task OpenAttachmentAsync(JsonElement attachment, string kind, Action onBack)
    {
        if (kind == "link")
        {
            var rawUrl = ProfileValue(attachment, "link_url", "linkURL", "linkUrl", "href", "url",
                "launch_url", "launchUrl");
            if (string.IsNullOrWhiteSpace(rawUrl))
                throw new InvalidOperationException("ClassWire did not provide a URL for this link.");
            await OpenExternalLinkAsync(rawUrl);
            return;
        }
        if (kind == "video")
        {
            var rawUrl = ProfileValue(attachment, "video_url", "videoURL", "videoUrl", "url", "href");
            var filename = ProfileValue(attachment, "filename", "fileName", "file_title", "fileTitle", "title", "name") ?? "";
            var mime = ProfileValue(attachment, "filemime", "fileMIME", "mime", "mime_type", "mimeType") ?? "";
            if (IsPlayableMedia(filename, mime, rawUrl))
            {
                await ShowAttachmentOptionsAsync(attachment, onBack);
                return;
            }
            if (string.IsNullOrWhiteSpace(rawUrl))
                throw new InvalidOperationException("ClassWire did not provide a URL for this video.");
            await OpenExternalLinkAsync(rawUrl);
            return;
        }

        await ShowAttachmentOptionsAsync(attachment, onBack);
    }

    private async Task<bool> TryOpenDocumentAttachmentAsync(string? rawUrl, Action onBack)
    {
        if (_session is null || string.IsNullOrWhiteSpace(rawUrl)) return false;
        var candidate = rawUrl.Trim();
        if (candidate.StartsWith('/'))
            candidate = "https://api.schoology.com" + candidate;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return false;
        var match = Regex.Match(uri.AbsolutePath, @"/(?:v1/)?sections/(\d+)/documents/(\d+)(?:/|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success ||
            !long.TryParse(match.Groups[1].Value, out var sectionId) ||
            !long.TryParse(match.Groups[2].Value, out var documentId)) return false;

        using var response = await _api.GetDocumentAsync(_session, sectionId, documentId);
        var documentData = response.RootElement.TryGetProperty("document", out var nested)
            ? nested : response.RootElement;
        var file = FirstFileAttachment(documentData);
        if (file.ValueKind != JsonValueKind.Object)
        {
            var fileItem = ReadAttachmentItems(documentData).FirstOrDefault(item => item.Kind == "file");
            file = fileItem.Item;
        }
        if (file.ValueKind != JsonValueKind.Object) return false;
        await ShowAttachmentOptionsAsync(file, onBack);
        return true;
    }

    private async Task ShowAttachmentOptionsAsync(JsonElement attachment, Action onBack)
    {
        var name = SafeMaterialFileName(ProfileValue(attachment, "filename", "fileName", "file_title",
            "fileTitle", "title", "name"), "ClassWire attachment");
        var mime = ProfileValue(attachment, "filemime", "fileMIME", "mime", "mime_type", "mimeType") ??
            "application/octet-stream";
        var originalUrl = ProfileValue(attachment, "file_download_url", "fileDownloadURL", "download_path",
            "downloadPath", "download_url", "downloadUrl", "resolveDownloadUrl", "resolve_download_url");
        var convertedUrl = ProfileValue(attachment, "file_converted_download_url", "fileConvertedDownloadURL",
            "converted_download_path", "convertedDownloadPath");
        var convertedStatus = ProfileValue(attachment, "converted_status", "convertedStatus")?.ToLowerInvariant() ?? "";
        var thumbnail = ProfileValue(attachment, "thumbnail", "thumbnail_url", "thumbnailUrl", "preview_url", "previewUrl", "fileThumbnailURL", "file_thumbnail_url", "videoThumbnailURL", "video_thumbnail_url");
        originalUrl ??= ProfileValue(attachment, "url", "href", "video_url", "videoURL", "videoUrl") ?? convertedUrl ?? thumbnail;
        var declaredMediaType = ProfileValue(attachment, "type", "attachment_type", "attachmentType")?.ToLowerInvariant();
        if (declaredMediaType == "video" && !mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            mime = "video/mp4";
        else if (declaredMediaType == "audio" && !mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            mime = "audio/mpeg";
        // Schoology may expose a PDF conversion for a media attachment. Never
        // route audio/video through that conversion: keep the original stream.
        var convertedReady = !IsPlayableMedia(name, mime, originalUrl) &&
            !string.IsNullOrWhiteSpace(convertedUrl) &&
            (convertedStatus.Length == 0 || convertedStatus is "success" or "converted" or "1");
        if (!IsPlayableMedia(name, mime, originalUrl) && !string.IsNullOrWhiteSpace(originalUrl) &&
            await TryOpenDocumentAttachmentAsync(originalUrl, onBack)) return;
        if (string.IsNullOrWhiteSpace(originalUrl))
        {
            var link = ProfileValue(attachment, "link_url", "linkURL", "linkUrl", "href", "url");
            if (!string.IsNullOrWhiteSpace(link)) { await OpenExternalLinkAsync(link); return; }
            throw new InvalidOperationException("ClassWire did not provide a download link for this attachment.");
        }

        var previewUrl = convertedReady ? convertedUrl! : originalUrl;
        var previewMime = convertedReady ? "application/pdf" : mime;
        var extension = MaterialExtension(name, previewMime);
        var details = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, MinWidth = 300 };
        details.Children.Add(new FontIcon { Glyph = MaterialGlyph(attachment), FontSize = 28,
            Foreground = ThemeBrush("SchoologyMutedTextBrush"), VerticalAlignment = VerticalAlignment.Center });
        var fileDetails = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        fileDetails.Children.Add(Text(name, 14, true));
        fileDetails.Children.Add(Text(string.IsNullOrWhiteSpace(mime) ? extension.TrimStart('.').ToUpperInvariant() : mime, 11));
        details.Children.Add(fileDetails);
        var dialog = new ContentDialog
        {
            Title = "View or Download",
            Content = details,
            PrimaryButtonText = "View",
            SecondaryButtonText = "Download",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _root.XamlRoot,
            RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
        };
        var choice = await dialog.ShowAsync();
        if (choice == ContentDialogResult.Primary)
            await OpenNativeMaterialFileAsync(previewUrl, previewMime, originalUrl, name, mime, name, onBack);
        else if (choice == ContentDialogResult.Secondary)
            await SaveOriginalMaterialAsync(originalUrl, name, mime);
    }

    private static string AttachmentGlyph(JsonElement attachment)
    {
        var filename = ProfileValue(attachment, "filename", "fileName", "file_title", "fileTitle",
            "title", "name") ?? string.Empty;
        var mime = ProfileValue(attachment, "filemime", "fileMIME", "mime", "mime_type", "mimeType") ?? string.Empty;
        var extension = Path.GetExtension(filename).ToLowerInvariant();
        if (extension == ".pptx" || mime.Contains("presentation", StringComparison.OrdinalIgnoreCase))
            return "\uE786"; // Segoe Fluent Icons: Slideshow
        if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
            extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff" or ".heic")
            return "\uE91B"; // Segoe Fluent Icons: Photo
        return MaterialGlyph(attachment);
    }

    private StackPanel BuildAttachmentControls(JsonElement attachmentRoot, Action onBack)
    {
        var panel = new StackPanel { Spacing = 4 };
        foreach (var (attachment, kind) in ReadAttachmentItems(attachmentRoot))
        {
            var name = ProfileValue(attachment, "filename", "fileName", "file_title", "fileTitle",
                "title", "name", "link_title", "linkTitle") ?? (kind == "link" ? "Open link" : "Attachment");
            var mime = ProfileValue(attachment, "filemime", "fileMIME", "mime", "mime_type", "mimeType") ?? "";
            var originalUrl = ProfileValue(attachment, "file_download_url", "fileDownloadURL", "download_path",
                "downloadPath", "download_url", "downloadUrl", "resolveDownloadUrl", "resolve_download_url");
            var convertedUrl = ProfileValue(attachment, "file_converted_download_url", "fileConvertedDownloadURL",
                "converted_download_path", "convertedDownloadPath");
            var thumbnail = ProfileValue(attachment, "thumbnail", "thumbnail_url", "thumbnailUrl", "preview_url", "previewUrl", "fileThumbnailURL", "file_thumbnail_url", "videoThumbnailURL", "video_thumbnail_url");
            var url = kind == "link"
                ? ProfileValue(attachment, "link_url", "linkURL", "linkUrl", "href", "url",
                    "launch_url", "launchUrl")
                : originalUrl ?? convertedUrl ?? ProfileValue(attachment, "url", "href", "video_url", "videoURL", "videoUrl") ?? thumbnail;

            if (kind == "embed" && ProfileValue(attachment, "embed_code", "embedCode") is { Length: > 0 } embedCode)
            {
                var embedButton = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(4, 6, 4, 6)
                };
                var embedContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                embedContent.Children.Add(new SymbolIcon(Symbol.OpenWith) { Foreground = ThemeBrush("SchoologyBlueBrush") });
                embedContent.Children.Add(Text(name, 13, true));
                embedButton.Content = embedContent;
                AutomationProperties.SetName(embedButton, $"Open embedded content {name}");
                var embeddedUrls = ExtractEmbeddedContentUrls(embedCode);
                var embeddedUrl = embeddedUrls.Length > 0 ? embeddedUrls[0] : null;
                if (embeddedUrl is null)
                {
                    embedButton.IsEnabled = false;
                    embedContent.Children.Add(Text("No secure embedded URL was provided.", 11));
                }
                else
                {
                    embedButton.Click += async (_, _) =>
                        await OpenSharedWebViewAsync(embeddedUrl, name, allowLtiExternal: true);
                }
                panel.Children.Add(embedButton);
                continue;
            }
            if (string.IsNullOrWhiteSpace(url)) continue;

            var declaredType = ProfileValue(attachment, "type", "attachment_type", "attachmentType")?.ToLowerInvariant() ?? "";
            var isImage = declaredType is "image" or "photo" ||
                mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                MaterialExtension(name, mime) is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" or ".heic" or ".heif" or ".avif";
            // The classic renderer displays attachment thumbnails even when they
            // represent a preview of a non-image file. Preserve those previews in
            // the shared Recent Activity and entity-post renderer as well.
            if (!string.IsNullOrWhiteSpace(thumbnail) &&
                (kind == "video" || kind == "file" && !isImage))
            {
                var attachmentPreview = new Image
                {
                    MaxHeight = 240, MaxWidth = 520,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                _ = LoadPostImageAsync(attachmentPreview, thumbnail);
                panel.Children.Add(attachmentPreview);
            }
            if (isImage && kind == "file")
            {
                var preview = new Image
                {
                    MaxHeight = 300,
                    MaxWidth = 680,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                var imageButton = new Button
                {
                    Content = preview,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0)
                };
                AutomationProperties.SetName(imageButton, $"View image attachment {name}");
                _ = LoadPostImageAsync(preview, thumbnail ?? url);
                imageButton.Click += async (_, _) => await ShowAttachmentOptionsAsync(attachment, onBack);
                panel.Children.Add(imageButton);
                continue;
            }

            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new FontIcon
            {
                Glyph = kind == "link" ? ((char)0xE71B).ToString() : MaterialGlyph(attachment),
                FontSize = 18,
                Foreground = ThemeBrush("SchoologyBlueBrush")
            });
            var label = new StackPanel { Spacing = 2 };
            label.Children.Add(Text(name, 13, true));
            label.Children.Add(Text(kind == "link" ? "Link" : string.IsNullOrWhiteSpace(mime) ? "File" : mime.ToUpperInvariant(), 11));
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            var button = new Button
            {
                Content = row,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 7, 4, 7)
            };
            button.Click += async (_, _) =>
            {
                await OpenAttachmentAsync(attachment, kind, onBack);
            };
            AutomationProperties.SetName(button, kind == "link" ? $"Open link {name}" : $"Open attachment {name}");
            panel.Children.Add(button);
        }
        return panel;
    }

    private static string MaterialGlyph(System.Text.Json.JsonElement data)
    {
        if (IsFolderRecord(data)) return "\uE8B7";
        var attachment = FirstFileAttachment(data);
        var type = ProfileValue(data, "type", "template_type", "resource_type")?.ToLowerInvariant() ?? "";
        var documentType = ProfileValue(data, "document_type", "documentType",
            "template_document_type", "templateDocumentType")?.ToLowerInvariant() ?? "";
        var name = ProfileValue(data, "filename", "file_name", "fileName", "title", "name")?.ToLowerInvariant() ??
            ProfileValue(attachment, "filename", "fileName", "filemime", "fileMIME")?.ToLowerInvariant() ?? "";
        var mime = ProfileValue(attachment, "filemime", "fileMIME", "mime_type")?.ToLowerInvariant() ?? "";
        var kind = type + " " + documentType + " " + name + " " + mime;
        if (type == "page") return "\uE7C3";
        if (type == "link" || documentType == "link" || kind.Contains("url")) return "\uE71B";
        if (kind.Contains("assignment")) return "\uE70F";
        if (kind.Contains("discussion")) return "\uE8BD";
        if (kind.Contains("assessment") || kind.Contains("quiz") || kind.Contains("test")) return "\uE9D5";
        if (kind.Contains("event") || kind.Contains("calendar")) return "\uE787";
        if (kind.Contains("external_tool") || kind.Contains("lti") || kind.Contains("scorm")) return "\uE71B";
        if (kind.Contains("pdf") || name.EndsWith(".pdf", StringComparison.Ordinal)) return "\uEA90";
        if (kind.Contains("image") || Regex.IsMatch(name, @"\.(png|jpe?g|gif|bmp|webp|svg)$")) return "\uEB9F";
        if (kind.Contains("video") || Regex.IsMatch(name, @"\.(mp4|mov|avi|webm|mkv)$")) return "\uE714";
        if (kind.Contains("audio") || Regex.IsMatch(name, @"\.(mp3|wav|m4a|aac|ogg|flac)$")) return "\uE8D6";
        if (kind.Contains("link") || kind.Contains("web_content") || documentType == "embed") return "\uE71B";
        if (kind.Contains("presentation") || kind.Contains("powerpoint") ||
            Regex.IsMatch(name, @"\.(ppt|pptx|pptm|pps|ppsx)$")) return "\uE8A5";
        if (kind.Contains("spreadsheet") || kind.Contains("excel") ||
            Regex.IsMatch(name, @"\.(xls|xlsx|xlsm|xlsb|csv|ods)$")) return "\uE9F9";
        if (kind.Contains("wordprocessing") || kind.Contains("document") ||
            Regex.IsMatch(name, @"\.(doc|docx|docm|odt|rtf)$")) return "\uE8A5";
        return "\uE8A5";
    }

    private static string[] ExtractEmbeddedContentUrls(string? markup)
    {
        if (string.IsNullOrWhiteSpace(markup)) return [];
        var decoded = System.Net.WebUtility.HtmlDecode(markup);
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(decoded,
            @"<(?:iframe|embed|object)\b[^>]*?\b(?:src|data)\s*=\s*(['""])(.*?)\1",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var raw = match.Groups[2].Value.Trim();
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var normalized = NormalizeSchoologyWebUrl(raw);
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
                continue;
            urls.Add(uri.AbsoluteUri);
        }
        return urls.ToArray();
    }

    private StackPanel CreateRichContentWithEmbeds(string? markup, double fontSize)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(CreateRichContent(markup, fontSize));
        foreach (var url in ExtractEmbeddedContentUrls(markup))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) continue;
            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 6, 8, 6)
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new FontIcon
            {
                Glyph = "\uE8A7",
                FontSize = 16,
                Foreground = ThemeBrush("SchoologyBlueBrush")
            });
            content.Children.Add(Text($"Open embedded content · {uri.Host}", 13, true));
            button.Content = content;
            AutomationProperties.SetName(button, $"Open embedded content from {uri.Host}");
            button.Click += async (_, _) =>
                await OpenSharedWebViewAsync(url, "Embedded content", allowLtiExternal: true);
            panel.Children.Add(button);
        }
        return panel;
    }

    private RichTextBlock CreateRichContent(string? markup, double fontSize)
    {
        var content = SchoologyRichContent.Create(markup, fontSize, async uri =>
        {
            await OpenExternalLinkAsync(uri.AbsoluteUri);
        }, darkTheme: _root.ActualTheme == ElementTheme.Dark && !_highContrastEnabled);
        content.Foreground = ThemeBrush("SchoologyTextBrush");
        return content;
    }

    private static string[] ExtractInlinePostImageUrls(string? markup)
    {
        if (string.IsNullOrWhiteSpace(markup)) return [];
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(markup,
            @"<img\b[^>]*?\bsrc\s*=\s*(['""])(.*?)\1",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var raw = System.Net.WebUtility.HtmlDecode(match.Groups[2].Value).Trim();
            if (raw.Length == 0) continue;
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            {
                try { uri = new Uri(new Uri("https://app.schoology.com/"), raw); }
                catch { continue; }
            }
            if (uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)) continue;
            urls.Add(NormalizeSchoologyImageUrl(uri.AbsoluteUri));
        }
        return urls.ToArray();
    }

    private StackPanel BuildInlinePostImages(string? markup)
    {
        var images = new StackPanel { Spacing = 8 };
        foreach (var url in ExtractInlinePostImageUrls(markup))
        {
            var image = new Image
            {
                MaxWidth = 680, MaxHeight = 440,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _ = LoadPostImageAsync(image, url);
            images.Children.Add(image);
        }
        return images;
    }

    private Border BuildUpdateCard(string kind, long entityId, SchoologyApiRecord update, JsonElement? authorProfile = null)
    {
        var updateId = JsonLong(update.Data, "id", "update_id");
        var card = new StackPanel { Spacing = 10 };
        var profile = authorProfile ?? update.Data;
        var author = ProfileValue(profile, "name_display", "display_name", "name", "user_name") ??
            ProfileValue(update.Data, "user_name", "author_name", "name_display", "created_by") ?? "Schoology";
        var createdRaw = ProfileValue(update.Data, "created", "created_at", "timestamp");
        var created = ParseSchoologyDate(createdRaw)?.ToString("g", System.Globalization.CultureInfo.CurrentCulture) ?? createdRaw;
        var picture = ProfileValue(profile, "picture_url", "pictureUrl", "picture", "photo_url", "profile_url") ??
            ProfileValue(update.Data, "picture_url", "author_picture", "user_picture", "avatar_url");
        var authorId = JsonLong(update.Data, "user_id", "uid", "author_id", "authorId", "creator_id", "creatorId", "created_by_id");
        if (authorId <= 0) authorId = JsonLong(profile, "id", "user_id", "uid");
        var postRealm = (ProfileValue(update.Data, "realm", "realm_name", "realmName") ?? "").ToLowerInvariant();
        var groupId = JsonLong(update.Data, "group_id", "groupId", "realm_id", "realmId");
        var groupName = ProfileValue(update.Data, "group_name", "groupName", "realm_title", "realmTitle");
        var isGroupPost = postRealm is "group" or "groups" && groupId > 0;
        var group = isGroupPost ? _groups.FirstOrDefault(item => item.Id == groupId) : null;
        if (isGroupPost && string.IsNullOrWhiteSpace(groupName)) groupName = group?.Name;
        var heading = new Grid { ColumnSpacing = 10 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var avatar = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20),
            Background = ThemeBrush("SchoologySubtleBrush") };
        if (!string.IsNullOrWhiteSpace(picture))
        {
            var avatarImage = new Image { Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
            avatar.Child = avatarImage;
            _ = LoadPostImageAsync(avatarImage, picture);
        }
        else avatar.Child = new FontIcon { Glyph = "\uE77B", FontSize = 19,
            Foreground = ThemeBrush("SchoologyMutedTextBrush"), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(avatar);
        var authorInfo = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var authorLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center };
        if (authorId > 0)
        {
            var authorButton = new Button
            {
                Content = Text(author, 14, true),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                MinHeight = 24,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeBrush("SchoologyBlueBrush")
            };
            AutomationProperties.SetName(authorButton, $"Open {author}'s profile");
            AutomationProperties.SetAutomationId(authorButton, $"PostAuthor_{authorId}");
            authorButton.Click += (_, _) => ShowUserProfile(authorId);
            authorLine.Children.Add(authorButton);
        }
        else authorLine.Children.Add(Text(author, 14, true));
        if (isGroupPost && !string.IsNullOrWhiteSpace(groupName))
        {
            authorLine.Children.Add(new FontIcon
            {
                Glyph = "\uE76C",
                FontSize = 12,
                Foreground = ThemeBrush("SchoologyMutedTextBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });
            var groupButton = new Button
            {
                Content = Text(groupName, 13, true),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                MinHeight = 24,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeBrush("SchoologyBlueBrush")
            };
            AutomationProperties.SetName(groupButton, $"Open {groupName}");
            AutomationProperties.SetAutomationId(groupButton, $"PostGroup_{groupId}");
            groupButton.Click += (_, _) => ShowThreePane("Groups", group ??
                new SchoologyEntity(groupId, groupName, string.Empty, null, update.Data.Clone()));
            authorLine.Children.Add(groupButton);
        }
        authorInfo.Children.Add(authorLine);
        if (!string.IsNullOrWhiteSpace(created))
        {
            var date = Text(created, 11);
            date.Foreground = ThemeBrush("SchoologyMutedTextBrush");
            authorInfo.Children.Add(date);
        }
        Grid.SetColumn(authorInfo, 1); heading.Children.Add(authorInfo);
        card.Children.Add(heading);
        var title = ProfileValue(update.Data, "title", "subject");
        if (!string.IsNullOrWhiteSpace(title)) card.Children.Add(Text(title, 16, true));
        var postBody = ProfileValue(update.Data, "richtext_body", "richtextBody", "body_richtext", "bodyRichtext",
            "body", "message", "description", "update_body", "updateBody", "text") ?? update.Subtitle;
        if (!string.IsNullOrWhiteSpace(postBody))
        {
            card.Children.Add(CreateRichContentWithEmbeds(postBody, 14));
            var inlineImages = BuildInlinePostImages(postBody);
            if (inlineImages.Children.Count > 0) card.Children.Add(inlineImages);
        }
        if (update.Data.ValueKind == JsonValueKind.Object)
        {
            var returnToPost = () =>
            {
                if (kind == "Recent") ShowHome("Recent Activity");
                else if (kind == "Profile") ShowUserProfile(entityId);
                else
                {
                    var entity = kind == "Courses" ? _courses.FirstOrDefault(item => item.Id == entityId) :
                        kind == "Groups" ? _groups.FirstOrDefault(item => item.Id == entityId) :
                        kind == "School Profile" ? GetSchoolProfileEntities().FirstOrDefault(item => item.Id == entityId) : null;
                    if (entity is not null) ShowThreePane(kind, entity);
                    else ShowHome("Recent Activity");
                }
            };
            var attachmentPanel = BuildAttachmentControls(update.Data, returnToPost);
            if (attachmentPanel.Children.Count > 0) card.Children.Add(attachmentPanel);
            var poll = FindJsonPropertyRecursive(update.Data,
                "pollUpdateModel", "poll_update_model", "poll", "poll_update", "pollUpdate");
            if (poll.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                var directPollOptions = FindJsonPropertyRecursive(update.Data,
                    "pollOptions", "poll_options", "pollOptionsList", "poll_options_list");
                if (directPollOptions.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
                    poll = update.Data;
            }
            if (poll.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
            {
                var pollPanel = new StackPanel { Spacing = 6 };
                pollPanel.Children.Add(Text("Poll", 13, true));
                var options = FindJsonPropertyRecursive(poll,
                    "options", "pollOptionsList", "poll_options", "pollOptions", "pollList", "poll_list",
                    "option", "poll_option");
                var optionItems = ReadJsonItems(options,
                    "option", "poll_option", "options", "pollOptionsList", "poll_options", "pollOptions", "pollList", "poll_list");
                if (optionItems.Length > 0)
                {
                    var pollList = new ListView
                    {
                        SelectionMode = ListViewSelectionMode.Single,
                        IsItemClickEnabled = true,
                        MaxHeight = 360,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        Background = new SolidColorBrush(Colors.Transparent)
                    };
                    AutomationProperties.SetName(pollList, "Poll options");
                    void RenderPollOptions(IReadOnlyList<JsonElement> pollOptions)
                    {
                        pollList.Items.Clear();
                        if (pollOptions.Count == 0) return;
                        var currentOptions = pollOptions;
                        var totalVotes = pollOptions.Sum(option =>
                            JsonLong(option, "count", "votes", "vote_count", "voteCount", "total_votes", "totalVotes", "num_votes", "numVotes"));
                        foreach (var option in pollOptions)
                        {
                            var optionId = JsonLong(option, "id", "poll_option_id", "pollOptionId", "pollOptionID");
                            var selected = JsonBoolean(option, "selected", "is_selected", "isSelected");
                            var optionTitle = ProfileValue(option, "title", "name", "text") ?? "Poll option";
                            var voteCount = JsonLong(option, "count", "votes", "vote_count", "voteCount", "total_votes", "totalVotes", "num_votes", "numVotes");
                            var votes = voteCount.ToString(System.Globalization.CultureInfo.CurrentCulture);
                            var percentage = totalVotes > 0 ? (int)Math.Round(voteCount * 100d / totalVotes) : 0;
                            var optionRow = new Grid { ColumnSpacing = 10, Padding = new Thickness(8, 7, 8, 7) };
                            optionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                            optionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                            optionRow.Children.Add(new RadioButton { IsChecked = selected, IsHitTestVisible = false,
                                VerticalAlignment = VerticalAlignment.Center });
                            var labels = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                            labels.Children.Add(Text(optionTitle, 13, true));
                            labels.Children.Add(Text($"{votes} votes · {percentage}%", 11));
                            Grid.SetColumn(labels, 1);
                            optionRow.Children.Add(labels);
                            var item = new ListViewItem { Content = optionRow, Tag = optionId, IsEnabled = optionId > 0,
                                HorizontalContentAlignment = HorizontalAlignment.Stretch };
                            item.Tapped += async (_, _) =>
                            {
                                if (_session is null || updateId <= 0 || optionId <= 0) return;
                                item.IsEnabled = false;
                                try
                                {
                                    using var vote = await _api.VotePollAsync(_session, updateId, optionId, !selected);
                                    var updatedModel = FindJsonPropertyRecursive(vote.RootElement,
                                        "pollUpdateModel", "poll_update_model", "poll", "poll_update", "pollUpdate");
                                    if (updatedModel.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                                        updatedModel = vote.RootElement;
                                    var updatedOptions = FindJsonPropertyRecursive(updatedModel,
                                        "options", "pollOptionsList", "poll_options", "pollOptions", "pollList", "poll_list",
                                        "option", "poll_option");
                                    var refreshedOptions = ReadJsonItems(updatedOptions,
                                        "option", "poll_option", "options", "pollOptionsList", "poll_options", "pollOptions", "pollList", "poll_list");
                                    if (refreshedOptions.Length > 0)
                                    {
                                        RenderPollOptions(refreshedOptions);
                                        return;
                                    }
                                    RenderPollOptions(currentOptions);
                                }
                                catch (Exception ex) { await ShowApiErrorAsync("Unable to vote in poll", ex.Message); }
                                finally { item.IsEnabled = true; }
                            };
                            pollList.Items.Add(item);
                        }
                    }
                    RenderPollOptions(optionItems);
                    pollPanel.Children.Add(pollList);
                }
                else pollPanel.Children.Add(Text("Poll options are not available.", 12));
                card.Children.Add(pollPanel);
            }
        }

        var commentRealm = kind;
        var commentRealmId = entityId;
        if (kind == "Recent")
        {
            var realm = (ProfileValue(update.Data, "realm", "realm_name", "realmName") ?? "").ToLowerInvariant();
            commentRealm = realm switch
            {
                "group" or "groups" => "Groups",
                "school" or "schools" => "School Profile",
                "user" or "users" => "Profile",
                _ => "Courses"
            };
            commentRealmId = JsonLong(update.Data, "realm_id", "realmId", "section_id", "sectionId",
                "group_id", "groupId", "school_id", "schoolId", "user_id", "uid");
        }

        var commentCount = JsonLong(update.Data, "num_comments", "comment_count", "comments_count", "commentCount");
        var likeCount = JsonLong(update.Data, "likes", "like_count", "likes_count", "likeCount");
        var liked = JsonBoolean(update.Data, "user_like_action", "user_liked", "userLiked", "liked", "is_liked");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        StackPanel previewPanel = new() { Spacing = 6, Visibility = Visibility.Collapsed };
        Button commentActionButton = null!;
        Button viewLikesButton = null!;
        void UpdateCommentActionLabel()
        {
            commentActionButton.Content = commentCount switch
            {
                <= 0 => "Comment",
                1 => "Comments (1)",
                2 => "Comments (2)",
                _ => $"More Comments ({commentCount})"
            };
            AutomationProperties.SetName(commentActionButton, commentCount switch
            {
                <= 0 => "Add a comment",
                1 => "View 1 comment",
                2 => "View 2 comments",
                _ => $"View more comments, {commentCount} total"
            });
        }
        var inlineComposer = new StackPanel
        {
            Spacing = 8,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 2, 0, 2)
        };
        var inlineCommentInput = new TextBox
        {
            PlaceholderText = "Write a comment…",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 72,
            MaxHeight = 160
        };
        AutomationProperties.SetAutomationId(inlineCommentInput, $"UpdateInlineComment_{updateId}");
        var inlineCommentActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        inlineCommentActions.Children.Add(Action("Cancel", (_, _) =>
        {
            inlineCommentInput.Text = string.Empty;
            inlineComposer.Visibility = Visibility.Collapsed;
        }));
        Button inlinePostButton = null!;
        inlinePostButton = Action("Post comment", async (_, _) =>
        {
            var value = inlineCommentInput.Text.Trim();
            if (value.Length == 0 || _session is null || updateId <= 0) return;
            inlinePostButton.IsEnabled = false;
            try
            {
                using var posted = await _api.AddUpdateCommentAsync(_session, commentRealm, commentRealmId, updateId, value);
                using var refreshed = await _api.GetUpdateCommentsAsync(_session, commentRealm, commentRealmId, updateId);
                var refreshedComments = SchoologyApiClient.ReadRecords(refreshed.RootElement, "comment", "comments");
                await ResolveCommentAuthorsAsync(refreshedComments, _session);
                commentCount = refreshedComments.Count;
                UpdateCommentActionLabel();
                inlineCommentInput.Text = string.Empty;
                inlineComposer.Visibility = Visibility.Collapsed;
                _ = LoadRecentPostCommentsAsync(commentRealm, commentRealmId, updateId, previewPanel,
                    count => { commentCount = count; UpdateCommentActionLabel(); });
            }
            catch (Exception ex) { await ShowApiErrorAsync("Unable to post comment", ex.Message); }
            finally { inlinePostButton.IsEnabled = true; }
        }, primary: true);
        inlineCommentActions.Children.Add(inlinePostButton);
        inlineComposer.Children.Add(inlineCommentInput);
        inlineComposer.Children.Add(inlineCommentActions);
        commentActionButton = new Button();
        AutomationProperties.SetAutomationId(commentActionButton, $"UpdateComments_{updateId}");
        UpdateCommentActionLabel();
        commentActionButton.Click += async (_, _) =>
        {
            if (updateId <= 0 || commentRealmId <= 0 || _session is null) return;
            if (commentCount <= 0)
            {
                inlineComposer.Visibility = inlineComposer.Visibility == Visibility.Visible
                    ? Visibility.Collapsed : Visibility.Visible;
                if (inlineComposer.Visibility == Visibility.Visible)
                    inlineCommentInput.Focus(FocusState.Programmatic);
                return;
            }
            await ShowUpdateCommentsDialogAsync(commentRealm, commentRealmId, updateId, () =>
            {
                commentCount++;
                UpdateCommentActionLabel();
                _ = LoadRecentPostCommentsAsync(commentRealm, commentRealmId, updateId, previewPanel,
                    count => { commentCount = count; UpdateCommentActionLabel(); });
            });
        };
        var likeButton = new Button { Content = liked ? "Unlike" : "Like" };
        AutomationProperties.SetAutomationId(likeButton, $"UpdateLike_{updateId}");
        likeButton.Click += async (_, _) =>
        {
            if (updateId <= 0 || _session is null) return;
            likeButton.IsEnabled = false;
            try
            {
                using var result = await _api.SetUpdateLikeAsync(_session, updateId, !liked);
                liked = !liked;
                likeCount = Math.Max(0, likeCount + (liked ? 1 : -1));
                likeButton.Content = liked ? "Unlike" : "Like";
                viewLikesButton.Content = likeCount > 0 ? $"View {likeCount} likes" : "No likes";
                viewLikesButton.IsEnabled = likeCount > 0;
            }
            catch (Exception ex) { await ShowApiErrorAsync("Unable to update like", ex.Message); }
            finally { likeButton.IsEnabled = true; }
        };
        viewLikesButton = new Button
        {
            Content = likeCount > 0 ? $"View {likeCount} likes" : "No likes",
            IsEnabled = likeCount > 0
        };
        AutomationProperties.SetAutomationId(viewLikesButton, $"UpdateViewLikes_{updateId}");
        viewLikesButton.Click += async (_, _) => await ShowUpdateLikesDialogAsync(updateId);
        actions.Children.Add(commentActionButton);
        actions.Children.Add(likeButton);
        actions.Children.Add(viewLikesButton);
        card.Children.Add(actions);
        card.Children.Add(inlineComposer);

        card.Children.Add(previewPanel);
        if (commentCount > 0 && commentRealmId > 0 && updateId > 0)
            _ = LoadRecentPostCommentsAsync(commentRealm, commentRealmId, updateId, previewPanel,
                count => { commentCount = count; UpdateCommentActionLabel(); });

        return Surface(card);
    }

    private async Task LoadUpdateCommentsAsync(
        string kind, long entityId, long updateId, StackPanel panel,
        TextBlock status, StackPanel list)
    {
        try
        {
            if (_session is null || updateId <= 0)
                throw new InvalidOperationException("This update does not have a valid ClassWire ID.");
            SetStatusText(status, "Loading comments…");
            list.Children.Clear();
            using var document = await _api.GetUpdateCommentsAsync(_session, kind, entityId, updateId);
            var comments = SchoologyApiClient.ReadRecords(document.RootElement, "comment", "comments");
            await ResolveCommentAuthorsAsync(comments, _session);
            list.Children.Add(BuildCommentThreadRows(comments, updateId));
            SetStatusText(status, comments.Count == 0 ? "No comments yet. Be the first to comment." : $"{comments.Count} comments");
        }
        catch (Exception ex)
        {
            SetStatusText(status, $"Unable to load comments: {ex.Message}");
            var retry = Action("Retry", async (_, _) =>
                await LoadUpdateCommentsAsync(kind, entityId, updateId, panel, status, list));
            panel.Children.Insert(Math.Max(0, panel.Children.IndexOf(status) + 1), retry);
        }
    }

    private Border ConnectedButtons(Button primary, Button count)
    {
        primary.Background = new SolidColorBrush(Colors.Transparent);
        primary.BorderThickness = new Thickness(0);
        primary.Padding = new Thickness(12, 8, 12, 8);
        count.Background = new SolidColorBrush(Colors.Transparent);
        count.BorderThickness = new Thickness(0);
        count.Padding = new Thickness(10, 8, 12, 8);

        var group = new Grid { ColumnSpacing = 0 };
        group.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        group.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        group.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        group.Children.Add(primary);
        var divider = new Border { Width = 1, Background = ThemeBrush("SchoologyDividerBrush"),
            VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 6, 0, 6),
            Visibility = count.Visibility };
        Grid.SetColumn(divider, 1);
        group.Children.Add(divider);
        count.Tag = divider;
        Grid.SetColumn(count, 2);
        group.Children.Add(count);
        return new Border
        {
            BorderBrush = ThemeBrush("SchoologyDividerBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = ThemeBrush("SchoologySubtleBrush"),
            Child = group
        };
    }

    private async Task LoadRecentPostCommentsAsync(
        string kind, long entityId, long updateId, StackPanel preview, Action<int>? onCountChanged = null)
    {
        try
        {
            if (_session is null || entityId <= 0 || updateId <= 0)
            {
                preview.Visibility = Visibility.Collapsed;
                return;
            }

            using var document = await _api.GetUpdateCommentsAsync(_session, kind, entityId, updateId);
            var comments = SchoologyApiClient.ReadRecords(document.RootElement, "comment", "comments");
            await ResolveCommentAuthorsAsync(comments, _session);
            onCountChanged?.Invoke(comments.Count);
            preview.Children.Clear();
            if (comments.Count == 0)
            {
                preview.Visibility = Visibility.Collapsed;
                return;
            }

            preview.Children.Add(BuildCommentThreadRows(comments.TakeLast(2).ToArray(), updateId, (parentId, author) =>
                _ = ShowUpdateCommentComposerAsync(kind, entityId, updateId,
                    () => _ = LoadRecentPostCommentsAsync(kind, entityId, updateId, preview, onCountChanged),
                    parentId)));
            preview.Visibility = Visibility.Visible;
        }
        catch
        {
            // Keep the post usable if preview loading fails; the full comments
            // dialog remains available through the action button.
            preview.Visibility = Visibility.Collapsed;
        }
    }

    private async Task ShowUpdateCommentComposerAsync(
        string kind, long entityId, long updateId, Action? onPosted = null, long parentCommentId = 0)
    {
        if (_session is null || entityId <= 0 || updateId <= 0) return;
        var input = new TextBox
        {
            PlaceholderText = "Write a comment…",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 100
        };
        AutomationProperties.SetAutomationId(input, $"UpdateCommentComposer_{updateId}");
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(Text("Add a comment to this post.", 13));
        content.Children.Add(input);
        var dialog = new ContentDialog
        {
            Title = parentCommentId > 0 ? "Reply to comment" : "Add comment",
            Content = CreateDialogScrollViewport(content),
            CloseButtonText = "Cancel",
            XamlRoot = _root.XamlRoot,
            RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
        };
        Button post = null!;
        post = Action("Post comment", async (_, _) =>
        {
            var value = input.Text.Trim();
            if (value.Length == 0) return;
            post.IsEnabled = false;
            try
            {
                using var result = await _api.AddUpdateCommentAsync(_session, kind, entityId, updateId, value, parentCommentId);
                dialog.Hide();
                onPosted?.Invoke();
            }
            catch (Exception ex) { await ShowApiErrorAsync("Unable to post comment", ex.Message); }
            finally { post.IsEnabled = true; }
        }, primary: true);
        content.Children.Add(post);
        await dialog.ShowAsync();
    }

    private async Task ShowUpdateCommentsDialogAsync(
        string kind, long entityId, long updateId, Action? onPosted = null)
    {
        if (_session is null || entityId <= 0 || updateId <= 0) return;
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(LoadingStatus("Loading comments…"));
        var dialog = new ContentDialog
        {
            Title = "Comments",
            Content = new ScrollViewer { Content = content, MaxHeight = 520,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = "Add comment",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _root.XamlRoot,
            RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
        };
        try
        {
            using var document = await _api.GetUpdateCommentsAsync(_session, kind, entityId, updateId);
            var comments = SchoologyApiClient.ReadRecords(document.RootElement, "comment", "comments");
            await ResolveCommentAuthorsAsync(comments, _session);
            content.Children.Clear();
            if (comments.Count == 0) content.Children.Add(Text("No comments yet.", 13));
            content.Children.Add(BuildCommentThreadRows(comments, updateId, (parentId, author) =>
                _ = ShowUpdateCommentComposerAsync(kind, entityId, updateId, onPosted, parentId)));
            dialog.Title = comments.Count == 0 ? "Comments" : $"Comments ({comments.Count})";
        }
        catch (Exception ex)
        {
            content.Children.Clear();
            content.Children.Add(Text($"Unable to load comments: {ex.Message}", 12));
        }

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            await ShowUpdateCommentComposerAsync(kind, entityId, updateId, onPosted);
    }

    private async Task ShowCommentLikesDialogAsync(long entityId, long commentId)
    {
        if (_session is null || entityId <= 0 || commentId <= 0) return;
        try
        {
            using var document = await _api.GetCommentLikesAsync(_session, entityId, commentId);
            var users = SchoologyApiClient.ReadRecords(document.RootElement, "user", "users", "like", "likes");
            var list = BuildLikesList(users, "CommentLikesList");
            long selectedUserId = 0;
            var dialog = new ContentDialog
            {
                Title = users.Count == 1 ? "1 like" : $"{users.Count} likes",
                Content = list,
                CloseButtonText = "Close",
                XamlRoot = _root.XamlRoot,
                RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
            };
            WireLikesListNavigation(list, id =>
            {
                selectedUserId = id;
                dialog.Hide();
            });
            await dialog.ShowAsync();
            if (selectedUserId > 0) ShowUserProfile(selectedUserId);
        }
        catch (Exception ex) { await ShowApiErrorAsync("Unable to load comment likes", ex.Message); }
    }

    private async Task ShowUpdateLikesDialogAsync(long updateId)
    {
        if (_session is null || updateId <= 0) return;
        try
        {
            using var document = await _api.GetUpdateLikesAsync(_session, updateId);
            var users = SchoologyApiClient.ReadRecords(document.RootElement, "user", "users", "like", "likes");
            var list = BuildLikesList(users, "UpdateLikesList");
            long selectedUserId = 0;
            var dialog = new ContentDialog
            {
                Title = users.Count == 1 ? "1 like" : $"{users.Count} likes",
                Content = list,
                CloseButtonText = "Close",
                XamlRoot = _root.XamlRoot,
                RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
            };
            WireLikesListNavigation(list, id =>
            {
                selectedUserId = id;
                dialog.Hide();
            });
            await dialog.ShowAsync();
            if (selectedUserId > 0) ShowUserProfile(selectedUserId);
        }
        catch (Exception ex) { await ShowApiErrorAsync("Unable to load likes", ex.Message); }
    }

    private ListView BuildLikesList(IReadOnlyList<SchoologyApiRecord> users, string automationId)
    {
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = false,
            MaxHeight = 480,
            MinWidth = 340,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent)
        };
        AutomationProperties.SetAutomationId(list, automationId);
        AutomationProperties.SetName(list, "People who liked this item");
        if (users.Count == 0)
        {
            list.Items.Add(new ListViewItem
            {
                Content = EmptyState("No likes were returned."),
                IsEnabled = false,
                IsTabStop = false
            });
            return list;
        }
        foreach (var person in users)
        {
            var data = person.Data;
            var user = data.ValueKind == System.Text.Json.JsonValueKind.Object &&
                data.TryGetProperty("user", out var nested) ? nested : data;
            var userId = JsonLong(data, "uid", "user_id", "userId", "id");
            if (userId <= 0) userId = JsonLong(user, "id", "uid", "user_id");
            var name = ProfileValue(user, "name_display", "display_name", "name", "user_name") ??
                ProfileValue(data, "name_display", "display_name", "name", "user_name") ??
                string.Join(" ", new[] { ProfileValue(user, "name_first", "first_name"), ProfileValue(user, "name_last", "last_name") }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(8, 6, 8, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(BuildProfileAvatar(data, userId));
            var label = Text(string.IsNullOrWhiteSpace(name) ? person.Title : name, 13, true);
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            var item = new ListViewItem
            {
                Content = row,
                Tag = userId,
                IsEnabled = userId > 0,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            AutomationProperties.SetName(item, string.IsNullOrWhiteSpace(name) ? person.Title : name);
            AutomationProperties.SetAutomationId(item, $"LikeUser_{userId}");
            list.Items.Add(item);
        }
        return list;
    }

    private static void WireLikesListNavigation(ListView list, Action<long> onUserSelected)
    {
        // Other navigable lists in this app (notably Members) attach actions
        // directly to each ListViewItem. Keep the same interaction model here.
        foreach (var item in list.Items.OfType<ListViewItem>())
        {
            if (!item.IsEnabled || item.Tag is not long userId || userId <= 0) continue;

            item.Tapped += (_, _) => onUserSelected(userId);
            item.KeyDown += (_, args) =>
            {
                if (args.Key is VirtualKey.Enter or VirtualKey.Space)
                {
                    args.Handled = true;
                    onUserSelected(userId);
                }
            };
        }
    }

    // Dialog content can exceed the available window height on compact displays or
    // when attachments/comments expand. Size the viewport from the live root height
    // and let it re-evaluate its available space whenever the window is resized.
    private ScrollViewer CreateDialogScrollViewport(UIElement content, double preferredMaxHeight = 640)
    {
        var availableHeight = _root.ActualHeight > 0
            ? Math.Max(240, _root.ActualHeight - 176)
            : preferredMaxHeight;
        return new ScrollViewer
        {
            Content = content,
            MinHeight = 0,
            MaxHeight = Math.Min(preferredMaxHeight, availableHeight),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled,
            ZoomMode = ZoomMode.Disabled,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private async Task ShowApiErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = _root.XamlRoot,
            RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
        };
        await dialog.ShowAsync();
    }

    private static long JsonLong(System.Text.Json.JsonElement data, params string[] keys)
    {
        if (data.ValueKind != System.Text.Json.JsonValueKind.Object) return 0;
        foreach (var key in keys)
            if (data.TryGetProperty(key, out var value) && long.TryParse(value.ToString(), out var result))
                return result;
        return 0;
    }

    private static bool JsonBoolean(System.Text.Json.JsonElement data, params string[] keys)
    {
        if (data.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
        foreach (var key in keys)
        {
            if (!data.TryGetProperty(key, out var value)) continue;
            if (value.ValueKind == System.Text.Json.JsonValueKind.True) return true;
            if (value.ValueKind == System.Text.Json.JsonValueKind.False) return false;
            if (bool.TryParse(value.ToString(), out var parsed)) return parsed;
            if (value.ToString() == "1") return true;
        }
        return false;
    }

    private static bool? JsonBooleanValue(System.Text.Json.JsonElement data, params string[] keys)
    {
        if (data.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
        foreach (var key in keys)
        {
            if (!data.TryGetProperty(key, out var value) ||
                value.ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined) continue;
            if (value.ValueKind == System.Text.Json.JsonValueKind.True) return true;
            if (value.ValueKind == System.Text.Json.JsonValueKind.False) return false;
            if (bool.TryParse(value.ToString(), out var parsed)) return parsed;
            if (int.TryParse(value.ToString(), out var number)) return number != 0;
        }
        return null;
    }

    private Grid BuildMessagesPage()
    {
        var root = new Grid { Margin = new Thickness(18), ColumnSpacing = 12,
            Background = new SolidColorBrush(Colors.Transparent) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // One master list: the three mailbox modes share the same selection and
        // detail surface rather than navigating between separate inbox/sent pages.
        var left = new Grid { RowSpacing = 10, Background = InAppAcrylicBrush(0.24, 0.32),
            Margin = new Thickness(8) };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var messagesHeader = new Grid { ColumnSpacing = 8 };
        messagesHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        messagesHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var messagesTitle = Text("Messages", 23, true);
        messagesTitle.VerticalAlignment = VerticalAlignment.Center;
        messagesHeader.Children.Add(messagesTitle);
        var createMessageButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { new SymbolIcon(Symbol.Add), Text("Create", 13, true) }
            },
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetAutomationId(createMessageButton, "MessagesCreateButton");
        AutomationProperties.SetName(createMessageButton, "Create a new message");
        Grid.SetColumn(createMessageButton, 1);
        messagesHeader.Children.Add(createMessageButton);
        left.Children.Add(messagesHeader);

        var modes = new SelectorBar { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var label in new[] { "Inbox", "Unread", "Sent" })
        {
            var modeItem = new SelectorBarItem { Text = label, Tag = label };
            AutomationProperties.SetName(modeItem, label + " messages");
            modes.Items.Add(modeItem);
        }
        AutomationProperties.SetAutomationId(modes, "MessagesMailboxSelector");
        var initialMailbox = _pendingProtocolMailbox == "Sent" ? "Sent" : "Inbox";
        _pendingProtocolMailbox = null;
        currentMode = initialMailbox;
        modes.SelectedItem = modes.Items.OfType<SelectorBarItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, initialMailbox, StringComparison.Ordinal))
            ?? modes.Items[0];
        Grid.SetRow(modes, 1); left.Children.Add(modes);

        var listHost = new Grid();
        Grid.SetRow(listHost, 2);
        left.Children.Add(listHost);
        var statusHost = LoadingStatus("Loading messages…", out var status, 16);
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = false, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent) };
        AutomationProperties.SetAutomationId(list, "SchoologyMessagesList");
        AutomationProperties.SetName(list, "Messages");
        listHost.Children.Add(list);
        var empty = Text("No messages in this folder.", 13);
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        empty.VerticalAlignment = VerticalAlignment.Center;
        empty.Visibility = Visibility.Collapsed;
        listHost.Children.Add(empty);
        var statusOverlay = new Border { Background = InAppAcrylicBrush(0.24, 0.32),
            Padding = new Thickness(8), VerticalAlignment = VerticalAlignment.Top, Child = statusHost };
        listHost.Children.Add(statusOverlay);

        var detail = new Grid { Background = InAppAcrylicBrush(0.24, 0.32) };
        var detailFrame = new Frame { Background = new SolidColorBrush(Colors.Transparent) };
        detail.Children.Add(detailFrame);
        // The detail Frame exclusively owns its content; never parent a placeholder in both places.
        Grid.SetColumn(left, 0); Grid.SetColumn(detail, 1);
        root.Children.Add(left); root.Children.Add(detail);

        IReadOnlyList<SchoologyApiRecord> inboxRecords = [];
        IReadOnlyList<SchoologyApiRecord> sentRecords = [];
        var currentMode = "Inbox";
        bool loading = false;
        bool messageSelected = false;
        bool mailboxLoadFailed = false;
        UIElement mailboxStatusContent = statusHost;
        bool inboxLoaded = false;
        bool sentLoaded = false;
        int mailboxLoadGeneration = 0;
        int messageViewGeneration = 0;
        var messageDetailViews = new Dictionary<string, UIElement>(StringComparer.Ordinal);
        var messageDetailData = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        CancellationTokenSource? mailboxLoadCancellation = null;
        CancellationTokenSource? messageLoadCancellation = null;

        bool IsUnread(SchoologyApiRecord record)
        {
            var data = record.Data;
            var unread = JsonBoolean(data, "unread", "is_unread", "isUnread");
            if (unread) return true;
            var read = JsonBoolean(data, "read", "is_read", "isRead");
            if (data.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var key in new[] { "message_status", "status", "read_status" })
                    if (data.TryGetProperty(key, out var value))
                    {
                        var state = value.ToString();
                        if (state.Equals("unread", StringComparison.OrdinalIgnoreCase) ||
                            state.Equals("new", StringComparison.OrdinalIgnoreCase)) return true;
                        if (state.Equals("read", StringComparison.OrdinalIgnoreCase)) return false;
                    }
            }
            return !read && JsonBoolean(data, "is_new", "new_message");
        }

        long MessagePersonId(SchoologyApiRecord record, string mode)
        {
            if (mode == "Sent")
            {
                var recipientIds = ProfileValue(record.Data, "recipient_ids", "recipientIds", "recipient_id", "recipientId");
                if (!string.IsNullOrWhiteSpace(recipientIds) &&
                    long.TryParse(recipientIds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out var recipientId))
                    return recipientId;
            }
            return JsonLong(record.Data, "author_id", "authorId", "uid", "user_id", "sender_id", "senderId");
        }

        async Task ResolveMessagePeopleAsync(IReadOnlyList<SchoologyApiRecord> records, string mode)
        {
            if (_session is null) return;
            var ids = records.Select(record => MessagePersonId(record, mode))
                .Where(id => id > 0 && !_userProfileCache.ContainsKey(id)).Distinct().Take(20).ToArray();
            await Task.WhenAll(ids.Select(async id =>
            {
                try
                {
                    using var profileDocument = await _api.GetUserAsync(_session, id);
                    var profile = profileDocument.RootElement;
                    if (profile.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        profile.TryGetProperty("user", out var nestedUser)) profile = nestedUser;
                    _userProfileCache[id] = profile.Clone();
                }
                catch (Exception ex) { App.WriteStartupLog($"Could not resolve message participant {id}.", ex); }
            }));
        }

        void ShowDetailPlaceholder(string title, string description)
        {
            messageSelected = false;
            var placeholder = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420 };
            placeholder.Children.Add(new FontIcon { Glyph = "\uE715", FontSize = 36,
                Foreground = ThemeBrush("SchoologyMutedTextBrush"), HorizontalAlignment = HorizontalAlignment.Center });
            placeholder.Children.Add(Text(title, 19, true));
            var message = Text(description, 13);
            message.Foreground = ThemeBrush("SchoologyMutedTextBrush");
            placeholder.Children.Add(message);
            NavigateShellFrame(detailFrame, placeholder, SlideNavigationTransitionEffect.FromLeft);
        }

        void RenderMessages()
        {
            list.Items.Clear();
            var records = currentMode switch
            {
                "Sent" => sentRecords,
                "Unread" => inboxRecords.Where(IsUnread).ToArray(),
                _ => inboxRecords
            };
            foreach (var record in records)
            {
                var data = record.Data;
                var id = JsonLong(data, "id", "message_id", "messageId");
                var isUnread = currentMode != "Sent" && IsUnread(record);
                var personId = MessagePersonId(record, currentMode);
                var personProfile = personId > 0 && _userProfileCache.TryGetValue(personId, out var cachedPerson)
                    ? cachedPerson : default;
                var personName = (personProfile.ValueKind == System.Text.Json.JsonValueKind.Object
                        ? ProfileValue(personProfile, "name_display", "display_name", "name", "username") : null) ??
                    (currentMode == "Sent"
                        ? ProfileValue(data, "recipient_name", "recipient_display_name", "to_name")
                        : ProfileValue(data, "author_name", "user_name", "name_display", "sender_name")) ??
                    (personId > 0 ? $"User {personId}" : currentMode == "Sent" ? "To recipient" : "ClassWire user");
                var picture = personProfile.ValueKind == System.Text.Json.JsonValueKind.Object
                    ? ProfileValue(personProfile, "picture_url", "pictureUrl", "picture", "photo_url", "avatar_url") : null;
                var subject = ProfileValue(data, "subject", "message_subject", "title") ?? record.Title;
                var snippet = ProfileValue(data, "body", "message", "text", "message_body") ?? record.Subtitle;
                var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(8, 10, 8, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var marker = new Border { Width = 3, CornerRadius = new CornerRadius(2),
                    Background = isUnread ? ThemeBrush("SchoologyBlueBrush") : new SolidColorBrush(Colors.Transparent) };
                row.Children.Add(marker);
                var avatar = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18),
                    Background = ThemeBrush("SchoologySubtleBrush"), VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center };
                if (!string.IsNullOrWhiteSpace(picture))
                {
                    var avatarImage = new Image { Width = 36, Height = 36, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
                    avatar.Child = avatarImage;
                    _ = LoadPostImageAsync(avatarImage, picture);
                }
                else avatar.Child = new TextBlock { Text = string.IsNullOrWhiteSpace(personName) ? "?" : personName[..1].ToUpperInvariant(),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
                Grid.SetColumn(avatar, 1); row.Children.Add(avatar);
                var textStack = new StackPanel { Spacing = 3 };
                var sender = Text(personName, 13, isUnread);
                var subjectText = Text(subject, 13, isUnread);
                subjectText.MaxLines = 1;
                var preview = Text(StripMarkup(snippet), 11);
                preview.MaxLines = 2;
                preview.Foreground = ThemeBrush("SchoologyMutedTextBrush");
                textStack.Children.Add(sender); textStack.Children.Add(subjectText); textStack.Children.Add(preview);
                Grid.SetColumn(textStack, 2); row.Children.Add(textStack);
                var item = new ListViewItem { Content = row, Tag = record,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch };
                AutomationProperties.SetName(item, $"{personName}: {subject}");
                AttachMessageActivation(item, record);
                list.Items.Add(item);
            }
            empty.Visibility = records.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            statusOverlay.Visibility = loading || mailboxLoadFailed ? Visibility.Visible : Visibility.Collapsed;
            statusOverlay.Child = mailboxStatusContent;
        }

        void ShowMailboxLoadError(string message)
        {
            mailboxLoadFailed = true;
            var errorPanel = new StackPanel { Spacing = 8, Padding = new Thickness(10) };
            errorPanel.Children.Add(Text(message, 12));
            errorPanel.Children.Add(Action("Retry", async (_, _) => await LoadMailboxAsync(currentMode)));
            mailboxStatusContent = errorPanel;
            RenderMessages();
        }

        async Task LoadMailboxAsync(string mode)
        {
            var generation = ++mailboxLoadGeneration;
            mailboxLoadCancellation?.Cancel();
            var requestCancellation = new CancellationTokenSource();
            requestCancellation.CancelAfter(TimeSpan.FromSeconds(35));
            mailboxLoadCancellation = requestCancellation;

            if (_session is null)
            {
                loading = false;
                ShowMailboxLoadError("Sign in to view your ClassWire messages.");
                ShowDetailPlaceholder("Messages unavailable", "Sign in to load your ClassWire messages.");
                if (ReferenceEquals(mailboxLoadCancellation, requestCancellation))
                    mailboxLoadCancellation = null;
                requestCancellation.Dispose();
                return;
            }

            currentMode = mode;
            loading = true;
            mailboxLoadFailed = false;
            mailboxStatusContent = statusHost;
            statusOverlay.Child = statusHost;
            SetStatusText(status, $"Loading {mode.ToLowerInvariant()}…");
            RenderMessages();
            try
            {
                if (mode == "Sent")
                {
                    if (!sentLoaded)
                    {
                        using var sent = await _api.GetMessagesAsync(_session, true, requestCancellation.Token);
                        if (generation != mailboxLoadGeneration) return;
                        sentRecords = SchoologyApiClient.ReadRecords(sent.RootElement, "message", "messages");
                        sentLoaded = true;
                    }
                }
                else if (!inboxLoaded)
                {
                    using var inbox = await _api.GetMessagesAsync(_session, false, requestCancellation.Token);
                    if (generation != mailboxLoadGeneration) return;
                    inboxRecords = SchoologyApiClient.ReadRecords(inbox.RootElement, "message", "messages");
                    inboxLoaded = true;
                }

                if (generation != mailboxLoadGeneration) return;
                var recordsToResolve = mode == "Sent" ? sentRecords : inboxRecords;
                loading = false;
                mailboxLoadFailed = false;
                mailboxStatusContent = statusHost;
                RenderMessages();
                SetStatusText(status, recordsCountText(mode));
                if (_pendingProtocolMessageId > 0 &&
                    _pendingProtocolMessageSent == (mode == "Sent"))
                {
                    var targetId = _pendingProtocolMessageId;
                    _pendingProtocolMessageId = 0;
                    var targetRecord = recordsToResolve.FirstOrDefault(item =>
                        JsonLong(item.Data, "id", "message_id", "messageId") == targetId)
                        ?? DeepLinkRecord("Message", targetId);
                    await OpenMessageAsync(targetRecord);
                }
                _ = ResolveAndRefreshMessagePeopleAsync(recordsToResolve, mode);
                if (mode == "Unread" && !inboxRecords.Any(IsUnread))
                    ShowDetailPlaceholder("You're all caught up", "There are no unread messages.");
                else if (mode == "Sent" && sentRecords.Count == 0)
                    ShowDetailPlaceholder("No sent messages", "Messages you send will appear here.");
                else if (mode == "Inbox" && inboxRecords.Count == 0)
                    ShowDetailPlaceholder("Your inbox is empty", "New messages will appear here.");
            }
            catch (OperationCanceledException) when (generation != mailboxLoadGeneration)
            {
                // A newer mailbox selection owns the list now.
            }
            catch (OperationCanceledException)
            {
                if (generation == mailboxLoadGeneration)
                {
                    loading = false;
                    ShowMailboxLoadError($"Loading {mode.ToLowerInvariant()} timed out. Select Retry to try again.");
                }
            }
            catch (Exception ex)
            {
                if (generation != mailboxLoadGeneration) return;
                loading = false;
                ShowMailboxLoadError($"Unable to load {mode.ToLowerInvariant()}: {ex.Message}");
            }
            finally
            {
                if (generation == mailboxLoadGeneration)
                {
                    loading = false;
                    statusOverlay.Visibility = mailboxLoadFailed ? Visibility.Visible : Visibility.Collapsed;
                }
                if (ReferenceEquals(mailboxLoadCancellation, requestCancellation))
                    mailboxLoadCancellation = null;
                requestCancellation.Dispose();
            }
        }

        async Task RefreshSentAfterComposeAsync()
        {
            messageViewGeneration++;
            messageLoadCancellation?.Cancel();
            messageSelected = false;
            left.Visibility = Visibility.Visible;
            detail.Visibility = root.ActualWidth < 760 ? Visibility.Collapsed : Visibility.Visible;
            ShowDetailPlaceholder("Message sent", "Your message appears in Sent.");
            sentLoaded = false;
            sentRecords = [];
            foreach (var key in messageDetailViews.Keys.Where(key => key.StartsWith("Sent:", StringComparison.Ordinal)).ToArray())
                messageDetailViews.Remove(key);
            foreach (var key in messageDetailData.Keys.Where(key => key.StartsWith("Sent:", StringComparison.Ordinal)).ToArray())
                messageDetailData.Remove(key);
            var sentMode = modes.Items.OfType<SelectorBarItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, "Sent", StringComparison.Ordinal));
            if (sentMode is not null && !ReferenceEquals(modes.SelectedItem, sentMode))
                modes.SelectedItem = sentMode;
            else
                await LoadMailboxAsync("Sent");
        }

        async Task ResolveAndRefreshMessagePeopleAsync(IReadOnlyList<SchoologyApiRecord> records, string mode)
        {
            await ResolveMessagePeopleAsync(records, mode);
            _root.DispatcherQueue.TryEnqueue(() =>
            {
                if (currentMode == mode && !loading) RenderMessages();
            });
        }

        string recordsCountText(string mode)
        {
            var count = mode == "Sent" ? sentRecords.Count :
                mode == "Unread" ? inboxRecords.Count(IsUnread) : inboxRecords.Count;
            return count == 1 ? "1 message" : $"{count} messages";
        }

        async Task OpenMessageAsync(SchoologyApiRecord record)
        {
            if (_session is null) return;
            var id = JsonLong(record.Data, "id", "message_id", "messageId");
            if (id <= 0)
            {
                await ShowApiErrorAsync("Unable to open message", "ClassWire did not return a valid message ID.");
                return;
            }
            var viewGeneration = ++messageViewGeneration;
            var activeMode = currentMode;
            var cacheKey = $"{activeMode}:{id}";
            messageLoadCancellation?.Cancel();
            if (messageDetailViews.TryGetValue(cacheKey, out var cachedDetail))
            {
                messageSelected = true;
                NavigateShellFrame(detailFrame, cachedDetail, SlideNavigationTransitionEffect.FromRight);
                return;
            }
            var messageCancellation = new CancellationTokenSource();
            messageCancellation.CancelAfter(TimeSpan.FromSeconds(35));
            messageLoadCancellation = messageCancellation;
            messageSelected = true;
            var loadingPanel = new StackPanel { Spacing = 10, Margin = new Thickness(24) };
            loadingPanel.Children.Add(new ProgressRing { IsActive = true, Width = 28, Height = 28 });
            loadingPanel.Children.Add(Text("Loading message…", 13));
            NavigateShellFrame(detailFrame, loadingPanel, SlideNavigationTransitionEffect.FromRight);
            try
            {
                JsonElement rootMessage;
                if (!messageDetailData.TryGetValue(cacheKey, out rootMessage))
                {
                    using var document = await _api.GetMessageAsync(_session, id, activeMode == "Sent", messageCancellation.Token);
                    rootMessage = document.RootElement.Clone();
                    messageDetailData[cacheKey] = rootMessage;
                }
                if (viewGeneration != messageViewGeneration || activeMode != currentMode) return;
                var threadRecords = SchoologyApiClient.ReadRecords(rootMessage, "message", "messages");
                var threadMessages = threadRecords.Select(item => item.Data).ToList();
                if (threadMessages.Count == 0 && rootMessage.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    rootMessage.TryGetProperty("message", out var singleMessage) && singleMessage.ValueKind == System.Text.Json.JsonValueKind.Object)
                    threadMessages.Add(singleMessage.Clone());
                if (threadMessages.Count == 0 && rootMessage.ValueKind == System.Text.Json.JsonValueKind.Object)
                    threadMessages.Add(rootMessage.Clone());
                var subject = ProfileValue(rootMessage, "subject", "message_subject", "title") ??
                    threadMessages.Select(message => ProfileValue(message, "subject", "message_subject", "title")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? record.Title;
                var sender = ProfileValue(rootMessage, "author_name", "user_name", "sender_name", "name_display") ??
                    ProfileValue(record.Data, "author_name", "user_name", "sender_name") ?? "ClassWire user";
                var authorIds = threadMessages.Select(message => JsonLong(message, "author_id", "authorId", "uid", "user_id", "sender_id", "senderId"))
                    .Where(id => id > 0).Distinct().ToArray();
                await Task.WhenAll(authorIds.Where(id => !_userProfileCache.ContainsKey(id)).Select(async id =>
                {
                    try
                    {
                        using var profileDocument = await _api.GetUserAsync(_session, id);
                        var profileRoot = profileDocument.RootElement;
                        if (profileRoot.ValueKind == System.Text.Json.JsonValueKind.Object && profileRoot.TryGetProperty("user", out var nestedUser)) profileRoot = nestedUser;
                        _userProfileCache[id] = profileRoot.Clone();
                    }
                    catch (Exception ex) { App.WriteStartupLog($"Could not resolve message author {id}.", ex); }
                }));
                if (viewGeneration != messageViewGeneration || activeMode != currentMode) return;
                var detailContent = new Grid { RowSpacing = 0 };
                detailContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                detailContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                var header = new StackPanel { Spacing = 6, Margin = new Thickness(22, 18, 22, 14) };
                var backButton = Action("‹ Messages", (_, _) => { left.Visibility = Visibility.Visible; if (root.ActualWidth < 760) detail.Visibility = Visibility.Collapsed; ShowDetailPlaceholder("Select a message", "Choose a message from the list to read it."); });
                AutomationProperties.SetAutomationId(backButton, "MessageDetailBack");
                backButton.Visibility = root.ActualWidth < 760 ? Visibility.Visible : Visibility.Collapsed;
                header.Children.Add(backButton);
                header.Children.Add(Text(subject, 22, true));
                header.Children.Add(Text(sender, 12));
                Grid.SetRow(header, 0); detailContent.Children.Add(header);
                var body = new StackPanel { Spacing = 12, Margin = new Thickness(22, 14, 22, 22) };
                if (threadMessages.Count == 0) body.Children.Add(EmptyState("This message thread did not contain any messages."));
                foreach (var message in threadMessages)
                {
                    var authorId = JsonLong(message, "author_id", "authorId", "uid", "user_id", "sender_id", "senderId");
                    var profile = authorId == _session.UserId ? _session.User :
                        authorId > 0 && _userProfileCache.TryGetValue(authorId, out var cachedProfile) ? cachedProfile : default;
                    if (profile.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        profile.TryGetProperty("user", out var nestedAuthor)) profile = nestedAuthor;
                    var authorName = authorId == _session.UserId ? "You" :
                        (profile.ValueKind == System.Text.Json.JsonValueKind.Object ? ProfileValue(profile, "name_display", "display_name", "name", "username") : null) ??
                        ProfileValue(message, "author_name", "sender_name", "user_name", "name_display") ??
                        (authorId > 0 ? $"User {authorId}" : sender);
                    var authorPicture = profile.ValueKind == System.Text.Json.JsonValueKind.Object
                        ? ProfileValue(profile, "picture_url", "pictureUrl", "picture", "photo_url", "avatar_url") : null;
                    var timestamp = ProfileValue(message, "last_updated", "lastUpdated", "created", "created_at", "timestamp");
                    var messageCard = new StackPanel { Spacing = 8, Padding = new Thickness(14) };
                    var messageHeader = new Grid { ColumnSpacing = 8 };
                    messageHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                    messageHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    messageHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var authorAvatar = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16),
                        Background = ThemeBrush("SchoologySubtleBrush"), VerticalAlignment = VerticalAlignment.Center };
                    if (!string.IsNullOrWhiteSpace(authorPicture))
                    {
                        var authorImage = new Image { Width = 32, Height = 32, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
                        authorAvatar.Child = authorImage;
                        _ = LoadPostImageAsync(authorImage, authorPicture);
                    }
                    else authorAvatar.Child = new TextBlock { Text = string.IsNullOrWhiteSpace(authorName) ? "?" : authorName[..1].ToUpperInvariant(),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
                    messageHeader.Children.Add(authorAvatar);
                    var authorText = Text(authorName, 13, true);
                    Grid.SetColumn(authorText, 1); messageHeader.Children.Add(authorText);
                    if (!string.IsNullOrWhiteSpace(timestamp))
                    {
                        var timeText = Text(timestamp, 11);
                        timeText.Foreground = ThemeBrush("SchoologyMutedTextBrush");
                        Grid.SetColumn(timeText, 2); messageHeader.Children.Add(timeText);
                    }
                    messageCard.Children.Add(messageHeader);
                    var messageBodyText = ProfileValue(message, "message_richtext", "messageRichtext", "richtext_message", "richtextMessage",
                        "message", "body", "text", "message_body", "content");
                    if (!string.IsNullOrWhiteSpace(messageBodyText))
                        messageCard.Children.Add(CreateRichContentWithEmbeds(messageBodyText, 14));
                    else messageCard.Children.Add(Text("No message text was returned.", 12));
                    var attachmentRoot = FindJsonPropertyRecursive(message, "attachments", "attachment", "files", "file-attachment");
                    if (attachmentRoot.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        var attachmentControls = BuildAttachmentControls(attachmentRoot,
                            () => NavigateShellFrame(detailFrame, detailContent, SlideNavigationTransitionEffect.FromRight));
                        if (attachmentControls.Children.Count > 0) messageCard.Children.Add(attachmentControls);
                    }
                    body.Children.Add(Surface(messageCard));
                }
                var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                Grid.SetRow(scroll, 1); detailContent.Children.Add(scroll);
                messageDetailViews[cacheKey] = detailContent;
                NavigateShellFrame(detailFrame, detailContent, SlideNavigationTransitionEffect.FromRight);
            }
            catch (OperationCanceledException) when (viewGeneration != messageViewGeneration)
            {
                // The user switched mailboxes or selected another message.
            }
            catch (Exception ex)
            {
                if (viewGeneration != messageViewGeneration) return;
                var error = new StackPanel { Spacing = 10, Padding = new Thickness(24) };
                error.Children.Add(Text($"Unable to load message: {ex.Message}", 13));
                error.Children.Add(Action("Retry", async (_, _) => await OpenMessageAsync(record)));
                NavigateShellFrame(detailFrame, error, SlideNavigationTransitionEffect.FromRight);
            }
            finally
            {
                if (ReferenceEquals(messageLoadCancellation, messageCancellation))
                    messageLoadCancellation = null;
                messageCancellation.Dispose();
            }
        }

        // Activate each concrete row directly; this is reliable for manually
        // supplied ListViewItem containers and preserves the two-screen portrait flow.
        void AttachMessageActivation(ListViewItem item, SchoologyApiRecord record)
        {
            item.Tapped += async (_, _) =>
            {
                await OpenMessageAsync(record);
                if (messageSelected && root.ActualWidth < 760)
                {
                    left.Visibility = Visibility.Collapsed;
                    detail.Visibility = Visibility.Visible;
                }
            };
        }
        modes.SelectionChanged += async (_, _) =>
        {
            try
            {
                if (modes.SelectedItem is not SelectorBarItem item) return;
                var mode = item.Tag as string ?? item.Text;
                if (mode == currentMode) return;
                messageViewGeneration++;
                messageLoadCancellation?.Cancel();
                ShowDetailPlaceholder("Select a message", "Choose a message from the list to read it.");
                await LoadMailboxAsync(mode);
            }
            catch (Exception ex)
            {
                App.WriteStartupLog("Mailbox selector navigation failed.", ex);
                loading = false;
                statusOverlay.Visibility = Visibility.Collapsed;
                SetStatusText(status, $"Unable to switch mailbox: {ex.Message}");
            }
        };

        // Responsive master/detail: on narrow windows, opening a message replaces
        // the list surface and Back returns to the same selected mailbox.
        root.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 760;
            left.Visibility = narrow && messageSelected ? Visibility.Collapsed : Visibility.Visible;
            detail.Visibility = narrow && !messageSelected ? Visibility.Collapsed : Visibility.Visible;
        };

        // Attach the Create action only after every mailbox local captured by the
        // Sent-refresh callback has been initialized.
        createMessageButton.Click += async (_, _) => await ShowComposeMessageDialogAsync(RefreshSentAfterComposeAsync);
        ShowDetailPlaceholder("Select a message", "Choose a message from the list to read it.");
        _ = LoadMailboxAsync(initialMailbox);
        return root;
    }


    private async Task ShowComposeMessageDialogAsync(Func<Task> refreshSent)
    {
        var session = _session;
        if (session is null)
        {
            await ShowApiErrorAsync("Messages unavailable", "Sign in to create a ClassWire message.");
            return;
        }

        var recipients = new List<(long Id, string Name)>();
        var fileAttachments = new List<(string Id, string Name)>();
        var resourceAttachments = new List<(long Id, string Name)>();
        var content = new StackPanel { Spacing = 12, MinWidth = 440 };

        var toLabel = Text("To", 12, true);
        var recipientSearch = new TextBox { PlaceholderText = "Search for a recipient" };
        AutomationProperties.SetAutomationId(recipientSearch, "MessageRecipientSearch");
        var selectedRecipientsScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 48
        };
        var selectedRecipients = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        selectedRecipientsScroll.Content = selectedRecipients;
        var suggestions = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = false,
            MaxHeight = 150,
            Visibility = Visibility.Collapsed,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetAutomationId(suggestions, "MessageRecipientSuggestions");
        var subject = new TextBox { Header = "Subject", PlaceholderText = "Subject" };
        AutomationProperties.SetAutomationId(subject, "MessageSubject");
        var bodyComposer = CreateRichTextComposer("Message", "Write a message…",
            "MessageBody", 140, 260);
        var body = bodyComposer.Editor;

        content.Children.Add(toLabel);
        content.Children.Add(recipientSearch);
        content.Children.Add(selectedRecipientsScroll);
        content.Children.Add(suggestions);
        content.Children.Add(subject);
        content.Children.Add(bodyComposer.Container);

        var attachmentActions = new Grid { ColumnSpacing = 10 };
        attachmentActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        attachmentActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var attachFile = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new SymbolIcon(Symbol.Page), Text("File", 13) }
            },
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var attachResource = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new SymbolIcon(Symbol.Folder), Text("Resource", 13) }
            },
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetAutomationId(attachFile, "MessageAttachFileButton");
        AutomationProperties.SetAutomationId(attachResource, "MessageAttachResourceButton");
        Grid.SetColumn(attachResource, 1);
        attachmentActions.Children.Add(attachFile);
        attachmentActions.Children.Add(attachResource);
        content.Children.Add(attachmentActions);

        var attachmentList = new StackPanel { Spacing = 5 };
        content.Children.Add(attachmentList);
        var status = Text(string.Empty, 12);
        status.Foreground = ThemeBrush("SchoologyMutedTextBrush");
        content.Children.Add(status);

        void RenderRecipients()
        {
            selectedRecipients.Children.Clear();
            selectedRecipientsScroll.Visibility = recipients.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            foreach (var recipient in recipients.ToArray())
            {
                var chip = new Button { Content = $"{recipient.Name}  ×", Padding = new Thickness(10, 4, 10, 4) };
                AutomationProperties.SetName(chip, $"Remove {recipient.Name}");
                chip.Click += (_, _) =>
                {
                    recipients.RemoveAll(item => item.Id == recipient.Id);
                    RenderRecipients();
                };
                selectedRecipients.Children.Add(chip);
            }
        }

        void RenderAttachments()
        {
            attachmentList.Children.Clear();
            foreach (var attachment in fileAttachments.ToArray())
            {
                var remove = new Button { Content = $"{attachment.Name}  ×", Padding = new Thickness(8, 4, 8, 4) };
                remove.Click += (_, _) =>
                {
                    fileAttachments.RemoveAll(item => item.Id == attachment.Id);
                    RenderAttachments();
                };
                attachmentList.Children.Add(remove);
            }
            foreach (var attachment in resourceAttachments.ToArray())
            {
                var remove = new Button { Content = $"{attachment.Name}  ×", Padding = new Thickness(8, 4, 8, 4) };
                remove.Click += (_, _) =>
                {
                    resourceAttachments.RemoveAll(item => item.Id == attachment.Id);
                    RenderAttachments();
                };
                attachmentList.Children.Add(remove);
            }
        }

        void AddRecipient(long id, string name)
        {
            if (id <= 0 || recipients.Any(item => item.Id == id)) return;
            recipients.Add((id, name));
            recipientSearch.Text = string.Empty;
            suggestions.Items.Clear();
            suggestions.Visibility = Visibility.Collapsed;
            RenderRecipients();
            recipientSearch.Focus(FocusState.Programmatic);
        }

        var dialog = new ContentDialog
        {
            Title = "New Message",
            Content = CreateDialogScrollViewport(content),
            PrimaryButtonText = "Send",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _root.XamlRoot,
            RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme
        };

        var searchGeneration = 0;
        CancellationTokenSource? searchCancellation = null;
        recipientSearch.TextChanged += async (_, _) =>
        {
            var query = recipientSearch.Text.Trim();
            var generation = ++searchGeneration;
            searchCancellation?.Cancel();
            searchCancellation?.Dispose();
            searchCancellation = null;
            suggestions.Items.Clear();
            if (query.Length < 2)
            {
                suggestions.Visibility = Visibility.Collapsed;
                return;
            }

            var cancellation = new CancellationTokenSource();
            searchCancellation = cancellation;
            suggestions.Items.Add(new ListViewItem { Content = Text("Searching…", 12), IsEnabled = false });
            suggestions.Visibility = Visibility.Visible;
            try
            {
                await Task.Delay(250, cancellation.Token);
                using var result = await _api.GetMessageRecipientsAsync(session, query, cancellation.Token);
                if (generation != searchGeneration || cancellation.IsCancellationRequested) return;
                suggestions.Items.Clear();
                foreach (var record in SchoologyApiClient.ReadRecords(result.RootElement,
                    "recipient", "recipients", "user", "users"))
                {
                    var id = JsonLong(record.Data, "id", "uid", "user_id", "userId", "recipient_id", "recipientId");
                    var name = ProfileValue(record.Data, "name_display", "nameDisplay", "display_name",
                        "displayName", "name", "full_name", "fullName") ?? record.Title;
                    if (id <= 0 || recipients.Any(item => item.Id == id)) continue;
                    var item = new ListViewItem
                    {
                        Content = Text(name, 13),
                        HorizontalContentAlignment = HorizontalAlignment.Stretch
                    };
                    void SelectRecipient() => AddRecipient(id, name);
                    item.Tapped += (_, _) => SelectRecipient();
                    item.KeyDown += (_, args) =>
                    {
                        if (args.Key is VirtualKey.Enter or VirtualKey.Space)
                        {
                            args.Handled = true;
                            SelectRecipient();
                        }
                    };
                    suggestions.Items.Add(item);
                }
                if (suggestions.Items.Count == 0)
                    suggestions.Items.Add(new ListViewItem { Content = Text("No recipients found.", 12), IsEnabled = false });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (generation != searchGeneration) return;
                suggestions.Items.Clear();
                suggestions.Items.Add(new ListViewItem { Content = Text($"Unable to search recipients: {ex.Message}", 12), IsEnabled = false });
            }
        };

        attachFile.Click += async (_, _) =>
        {
            try
            {
                var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
                picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(AppWindow.Id));
                var file = await picker.PickSingleFileAsync();
                if (file is null) return;
                var info = new FileInfo(file.Path);
                if (!info.Exists || info.Length <= 0)
                    throw new InvalidOperationException("The selected file is empty or no longer available.");
                if (info.Length > 60L * 1024 * 1024)
                    throw new InvalidOperationException("Message attachments are limited to 60 MB.");
                status.Text = $"Uploading {file.Name}…";
                attachFile.IsEnabled = false;
                var bytes = await File.ReadAllBytesAsync(file.Path);
                var id = await _api.UploadMessageFileAsync(session, file.Name, GetSubmissionMimeType(file.Name), bytes);
                fileAttachments.Add((id, file.Name));
                RenderAttachments();
                status.Text = "File attached.";
            }
            catch (Exception ex) { status.Text = $"Unable to attach file: {ex.Message}"; }
            finally { attachFile.IsEnabled = true; }
        };

        var resourceFlyout = new Flyout();
        var resourceList = new ListView
        {
            Width = 380,
            MaxHeight = 340,
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetAutomationId(resourceList, "MessageResourcePickerList");
        resourceFlyout.Content = resourceList;
        attachResource.Click += async (_, _) =>
        {
            resourceList.Items.Clear();
            resourceList.Items.Add(new ListViewItem { Content = Text("Loading resources…", 12), IsEnabled = false });
            resourceFlyout.ShowAt(attachResource);
            try
            {
                var choices = await LoadMessageResourceChoicesAsync(session);
                resourceList.Items.Clear();
                foreach (var choice in choices)
                {
                    var item = new ListViewItem
                    {
                        Content = Text(choice.Title, 13),
                        HorizontalContentAlignment = HorizontalAlignment.Stretch
                    };
                    item.Tapped += (_, _) =>
                    {
                        if (resourceAttachments.All(existing => existing.Id != choice.Id))
                        {
                            resourceAttachments.Add((choice.Id, choice.Title));
                            RenderAttachments();
                        }
                        resourceFlyout.Hide();
                    };
                    resourceList.Items.Add(item);
                }
                if (choices.Count == 0)
                    resourceList.Items.Add(new ListViewItem { Content = Text("No resources found.", 12), IsEnabled = false });
            }
            catch (Exception ex)
            {
                resourceList.Items.Clear();
                resourceList.Items.Add(new ListViewItem { Content = Text($"Unable to load resources: {ex.Message}", 12), IsEnabled = false });
            }
        };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            args.Cancel = true;
            try
            {
                if (recipients.Count == 0)
                {
                    status.Text = "Choose at least one recipient from the suggestions.";
                    recipientSearch.Focus(FocusState.Programmatic);
                    return;
                }
                if (string.IsNullOrWhiteSpace(subject.Text))
                {
                    status.Text = "Enter a subject.";
                    subject.Focus(FocusState.Programmatic);
                    return;
                }
                var messageHtml = SerializeTextSubmission(body, out var messagePlainText);
                if (string.IsNullOrWhiteSpace(messagePlainText))
                {
                    status.Text = "Enter a message.";
                    body.Focus(FocusState.Programmatic);
                    return;
                }

                status.Text = "Sending…";
                dialog.IsPrimaryButtonEnabled = false;
                using var response = await _api.CreateMessageAsync(session,
                    recipients.Select(item => item.Id).ToArray(),
                    subject.Text.Trim(), messageHtml,
                    fileAttachments.Select(item => item.Id).ToArray(),
                    resourceAttachments.Select(item => item.Id).ToArray());
                status.Text = "Message sent.";
                await Task.Delay(250);
                args.Cancel = false;
            }
            catch (Exception ex)
            {
                status.Text = $"Unable to send message: {ex.Message}";
                dialog.IsPrimaryButtonEnabled = true;
            }
            finally { deferral.Complete(); }
        };

        dialog.Closed += (_, _) =>
        {
            searchGeneration++;
            searchCancellation?.Cancel();
            searchCancellation?.Dispose();
            resourceFlyout.Hide();
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await refreshSent();
    }

    private async Task<List<(long Id, string Title)>> LoadMessageResourceChoicesAsync(
        SchoologyAuthService.Session session)
    {
        var choices = new List<(long Id, string Title)>();
        var seenResources = new HashSet<long>();
        var visitedFolders = new HashSet<(long CollectionId, long FolderId)>();
        using var collections = await _api.GetResourceCollectionsAsync(session);
        var collectionRecords = SchoologyApiClient.ReadRecords(collections.RootElement,
            "collection", "collections", "items");

        async Task LoadFolderAsync(long collectionId, long folderId, int depth)
        {
            if (depth > 4 || choices.Count >= 300 ||
                !visitedFolders.Add((collectionId, folderId))) return;
            using var resources = await _api.GetCollectionResourcesAsync(session, collectionId, folderId);
            foreach (var resource in SchoologyApiClient.ReadRecords(resources.RootElement,
                "resource", "resources", "items"))
            {
                if (IsFolderRecord(resource.Data))
                {
                    var nestedFolderId = JsonLong(resource.Data, "id", "folder_id", "folderId");
                    if (nestedFolderId > 0) await LoadFolderAsync(collectionId, nestedFolderId, depth + 1);
                    continue;
                }
                var id = JsonLong(resource.Data, "template_id", "templateID", "resource_id",
                    "resourceId", "id");
                if (id <= 0 || !seenResources.Add(id)) continue;
                var title = ProfileValue(resource.Data, "title", "name") ?? resource.Title;
                choices.Add((id, title));
                if (choices.Count >= 300) break;
            }
        }

        foreach (var collection in collectionRecords)
        {
            var collectionId = JsonLong(collection.Data, "id", "collection_id", "collectionId");
            if (collectionId > 0) await LoadFolderAsync(collectionId, 0, 0);
            if (choices.Count >= 300) break;
        }
        return choices;
    }

    private static string StripMarkup(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = System.Net.WebUtility.HtmlDecode(
            System.Text.RegularExpressions.Regex.Replace(value, "<[^>]+>", " "));
        return System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
    }

    private static string FormatGradeDisplayValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "—";
        var trimmed = value.Trim();
        var suffix = trimmed.EndsWith('%') ? "%" : "";
        var numericText = suffix.Length > 0 ? trimmed[..^1].Trim() : trimmed;
        if (decimal.TryParse(numericText, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            var rounded = decimal.Round(number, 2, MidpointRounding.AwayFromZero);
            return rounded.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + suffix;
        }
        return trimmed;
    }

    private static string FormatGradeScore(string? score, string? maximum)
    {
        var formatted = FormatGradeDisplayValue(score);
        return string.IsNullOrWhiteSpace(maximum) ? formatted :
            formatted + " / " + FormatGradeDisplayValue(maximum);
    }

    private void AddGradeHierarchyRow(StackPanel destination, string title, int depth,
        Action<StackPanel> populateChildren, bool initiallyExpanded = false)
    {
        var wrapper = new StackPanel { Spacing = 0 };
        var row = new Grid
        {
            ColumnSpacing = 10,
            Padding = new Thickness(10 + depth * 22, 9, 12, 9),
            MinHeight = 46
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var arrowTransform = new RotateTransform { Angle = initiallyExpanded ? 180 : 0 };
        var arrow = new FontIcon
        {
            Glyph = initiallyExpanded ? "\uE70E" : "\uE70D",
            FontSize = 13,
            Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            RenderTransform = arrowTransform
        };
        arrow.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        row.Children.Add(arrow);
        var label = Text(title, 14, true);
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        var item = new ListViewItem
        {
            Content = row, Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(item, title);
        wrapper.Children.Add(item);

        var childOffset = new TranslateTransform();
        var children = new StackPanel
        {
            Spacing = 1,
            Visibility = initiallyExpanded ? Visibility.Visible : Visibility.Collapsed,
            Opacity = 1,
            RenderTransform = childOffset,
            Margin = new Thickness(0, 0, 0, 4)
        };
        wrapper.Children.Add(children);
        var expanded = initiallyExpanded;
        if (expanded) populateChildren(children);

        void AnimateArrow(bool open)
        {
            var target = open ? 180d : 0d;
            if (!AnimationsEnabled) { arrowTransform.Angle = target; return; }
            var animation = new DoubleAnimation
            {
                To = target, Duration = TimeSpan.FromMilliseconds(180),
                EnableDependentAnimation = true
            };
            var storyboard = new Storyboard();
            Storyboard.SetTarget(animation, arrowTransform);
            Storyboard.SetTargetProperty(animation, "Angle");
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }

        void AnimateChildren(bool open)
        {
            if (!AnimationsEnabled)
            {
                children.Opacity = 1;
                childOffset.Y = 0;
                children.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
            if (open) children.Visibility = Visibility.Visible;
            var opacity = new DoubleAnimation
            {
                From = open ? 0 : 1, To = open ? 1 : 0,
                Duration = TimeSpan.FromMilliseconds(160), EnableDependentAnimation = true
            };
            var slide = new DoubleAnimation
            {
                From = open ? -6 : 0, To = open ? 0 : -6,
                Duration = TimeSpan.FromMilliseconds(180), EnableDependentAnimation = true
            };
            var storyboard = new Storyboard();
            Storyboard.SetTarget(opacity, children);
            Storyboard.SetTargetProperty(opacity, "Opacity");
            Storyboard.SetTarget(slide, childOffset);
            Storyboard.SetTargetProperty(slide, "Y");
            storyboard.Children.Add(opacity);
            storyboard.Children.Add(slide);
            storyboard.Completed += (_, _) =>
            {
                if (!open && !expanded) children.Visibility = Visibility.Collapsed;
                if (open) { children.Opacity = 1; childOffset.Y = 0; }
            };
            storyboard.Begin();
        }

        item.Tapped += (_, args) =>
        {
            args.Handled = true;
            expanded = !expanded;
            if (expanded && children.Children.Count == 0) populateChildren(children);
            AnimateArrow(expanded);
            AnimateChildren(expanded);
        };
        destination.Children.Add(wrapper);
    }

    private void AddGradeAssignmentRow(StackPanel destination, long sectionId,
        JsonElement assignment, JsonElement gradeData, int depth)
    {
        var title = ProfileValue(assignment, "title", "assignment_title", "name") ?? "Assignment";
        var rawScore = ProfileValue(gradeData, "grade", "calculated_grade", "score", "grade_value");
        var maximum = ProfileValue(assignment, "max_points", "maxPoints") ??
            ProfileValue(gradeData, "max_points", "maxPoints");
        var score = string.IsNullOrWhiteSpace(rawScore) ? "—" : FormatGradeScore(rawScore, maximum);
        var row = new Grid
        {
            ColumnSpacing = 12,
            Padding = new Thickness(10 + depth * 22, 9, 14, 9),
            MinHeight = 48,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        var name = Text(title, 14);
        name.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(name);
        var scoreText = Text(score, 13, true);
        scoreText.VerticalAlignment = VerticalAlignment.Center;
        scoreText.HorizontalAlignment = HorizontalAlignment.Stretch;
        scoreText.TextAlignment = TextAlignment.Right;
        Grid.SetColumn(scoreText, 1);
        row.Children.Add(scoreText);
        var button = new Button
        {
            Content = row, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0)
        };
        AutomationProperties.SetName(button, $"{title}, score {score}");
        button.Click += async (_, _) =>
        {
            var id = JsonLong(assignment, "id", "assignment_id", "assignmentId");
            if (id <= 0) return;
            var type = (ProfileValue(assignment, "type", "template_type", "type_name") ?? "assignment").ToLowerInvariant();
            var record = new SchoologyApiRecord(title, "", assignment.Clone());
            if (type is "assessment" or "assessment_v2" or "managed_assessment" or "quiz" or "test")
                await OpenSharedWebViewAsync($"https://app.schoology.com/assignment/{id}", title,
                    allowLtiExternal: true, onBack: () => ShowHome("Grades"));
            else
                await ShowAssignmentDetailsAsync(sectionId, record);
        };
        var wrapper = new StackPanel { Spacing = 0 };
        wrapper.Children.Add(button);
        destination.Children.Add(wrapper);
    }

    private UIElement BuildGradeDetails(JsonElement sectionData, bool expandPeriods)
    {
        var sectionId = JsonLong(sectionData, "section_id", "sectionId", "id");
        if (sectionId <= 0 || !_courses.Any(course => course.Id == sectionId))
            return EmptyState("No grades are available for this course.");
        var content = new StackPanel { Spacing = 2 };
        _ = LoadCourseGradeDetailsAsync(content, sectionId, sectionData.Clone(), expandPeriods);
        return content;
    }

    private async Task LoadCourseGradeDetailsAsync(StackPanel rows, long sectionId,
        JsonElement sectionData, bool expandPeriods)
    {
        rows.Children.Clear();
        rows.Children.Add(LoadingStatus("Loading grades…"));
        async Task<JsonDocument?> TryLoadAsync(Func<Task<JsonDocument>> request)
        {
            try { return await request(); }
            catch { return null; }
        }

        try
        {
            var session = _session ?? throw new InvalidOperationException("Sign in to view grades.");
            var responses = await Task.WhenAll(
                TryLoadAsync(() => _api.GetGradingPeriodsAsync(session, sectionId)),
                TryLoadAsync(() => _api.GetGradingCategoriesAsync(session, sectionId)),
                TryLoadAsync(() => _api.GetGradeItemsAsync(session, sectionId)),
                TryLoadAsync(() => _api.GetSectionGradesAsync(session, sectionId)),
                TryLoadAsync(() => _api.GetGradingScalesAsync(session, sectionId)),
                TryLoadAsync(() => _api.GetSectionEnrollmentsAsync(session, sectionId)),
                TryLoadAsync(() => _api.GetSectionDetailsAsync(session, sectionId)));

            using var periodDocument = responses[0];
            using var categoryDocument = responses[1];
            using var itemDocument = responses[2];
            using var finalGradesDocument = responses[3];
            using var scalesDocument = responses[4];
            using var enrollmentsDocument = responses[5];
            using var sectionDocument = responses[6];
            if (itemDocument is null)
                throw new InvalidOperationException("ClassWire did not return this course's grade items.");

            rows.Children.Clear();
            var finalGradeRecords = SchoologyApiClient.ReadRecords(finalGradesDocument?.RootElement ?? default,
                "final_grade", "finalGrade", "final_grades", "finalGrades");
            var enrollmentRecords = SchoologyApiClient.ReadRecords(enrollmentsDocument?.RootElement ?? default, "enrollment", "enrollments");
            var ownEnrollment = enrollmentRecords.FirstOrDefault(record =>
                string.Equals(ProfileValue(record.Data, "uid", "user_id", "userId"),
                    session.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
            var enrollmentData = ownEnrollment is null ? default : ownEnrollment.Data;
            var enrollmentId = JsonLong(enrollmentData, "id", "enrollment_id", "enrollmentId");
            var finalRecord = finalGradeRecords.FirstOrDefault(record =>
                enrollmentId > 0 && JsonLong(record.Data, "enrollment_id", "enrollmentId") == enrollmentId);
            if (finalRecord is null && finalGradeRecords.Count == 1) finalRecord = finalGradeRecords[0];
            if (finalRecord is null)
            {
                var embeddedFinalGrades = SchoologyApiClient.ReadRecords(sectionData, "final_grade", "finalGrade", "final_grades", "finalGrades");
                finalRecord = embeddedFinalGrades.FirstOrDefault(record =>
                    enrollmentId > 0 && JsonLong(record.Data, "enrollment_id", "enrollmentId") == enrollmentId);
                if (finalRecord is null && embeddedFinalGrades.Count == 1) finalRecord = embeddedFinalGrades[0];
            }
            var finalData = finalRecord is null ? default : finalRecord.Data;
            var finalPeriods = SchoologyApiClient.ReadRecords(
                finalData, "period", "periods", "final_period", "finalPeriod", "final_grade_period", "finalGradePeriod");
            var finalPeriodRecord = finalPeriods.FirstOrDefault(record =>
                string.Equals(ProfileValue(record.Data, "period_id", "periodId"), "final",
                    StringComparison.OrdinalIgnoreCase));
            var finalPeriodData = finalPeriodRecord is null ? default : finalPeriodRecord.Data;

            var scaleRecords = SchoologyApiClient.ReadRecords(
                scalesDocument?.RootElement ?? default,
                "grading_scale", "gradingScale", "scales", "scale");
            var scaleId = JsonLong(finalData, "scale_id", "scaleId");
            if (scaleId <= 0) scaleId = JsonLong(finalPeriodData, "scale_id", "scaleId");
            var sectionDetails = sectionDocument?.RootElement ?? default;
            if (sectionDetails.ValueKind == JsonValueKind.Object &&
                sectionDetails.TryGetProperty("section", out var nestedSection))
                sectionDetails = nestedSection;
            if (scaleId <= 0 && sectionDetails.ValueKind == JsonValueKind.Object &&
                sectionDetails.TryGetProperty("options", out var options))
                scaleId = JsonLong(options, "default_grading_scale_id", "defaultGradingScaleId");
            var scaleRecord = scaleRecords.FirstOrDefault(record =>
                scaleId > 0 && JsonLong(record.Data, "id", "scale_id", "scaleId") == scaleId);
            if (scaleRecord is null && scaleRecords.Count == 1) scaleRecord = scaleRecords[0];
            var scaleData = scaleRecord is null ? default : scaleRecord.Data;

            var summary = new Grid { Padding = new Thickness(14, 10, 14, 10), ColumnSpacing = 10 };
            summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summary.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            summary.Children.Add(Text("Overall Grade", 14, true));
            var overall = Text(FormatCourseOverallGrade(sectionData, finalData, finalPeriodData, scaleData), 14, true);
            overall.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(overall, 1);
            summary.Children.Add(overall);
            rows.Children.Add(summary);

            var teacherComment = ProfileValue(finalPeriodData, "comment") ?? ProfileValue(finalData, "comment");
            if (!string.IsNullOrWhiteSpace(teacherComment))
            {
                var comment = new StackPanel { Spacing = 3, Margin = new Thickness(14, 0, 14, 10) };
                comment.Children.Add(Text("Teacher Comment", 12, true));
                comment.Children.Add(CreateRichContent(teacherComment, 13));
                rows.Children.Add(comment);
            }

            var gradesByAssignment = new Dictionary<long, JsonElement>();
            foreach (var period in SchoologyApiClient.ReadRecords(sectionData, "period", "periods"))
            foreach (var assignmentGrade in SchoologyApiClient.ReadRecords(period.Data, "assignment", "assignments"))
            {
                var assignmentId = JsonLong(assignmentGrade.Data, "assignment_id", "assignmentId", "id");
                if (assignmentId > 0) gradesByAssignment[assignmentId] = assignmentGrade.Data.Clone();
            }

            var periodRecords = SchoologyApiClient.ReadRecords(
                periodDocument?.RootElement ?? default, "grading_period", "gradePeriod", "period", "periods");
            var categoryRecords = SchoologyApiClient.ReadRecords(
                categoryDocument?.RootElement ?? default, "grading_category", "category", "categories");
            var periodNames = periodRecords
                .GroupBy(record => JsonLong(record.Data, "id", "grading_period_id", "period_id").ToString(
                    System.Globalization.CultureInfo.InvariantCulture))
                .ToDictionary(group => group.Key,
                    group => ProfileValue(group.First().Data, "title", "name") ?? "Grading Period");
            var categoryNames = categoryRecords
                .GroupBy(record => JsonLong(record.Data, "id", "grading_category_id", "category_id").ToString(
                    System.Globalization.CultureInfo.InvariantCulture))
                .ToDictionary(group => group.Key,
                    group => ProfileValue(group.First().Data, "title", "name") ?? "Category");
            SchoologyApiRecord[] assignments =
                SchoologyApiClient.ReadRecords(itemDocument.RootElement,
                    "assignment", "assignments", "grade_item", "grade_items")
                .Select(record => new SchoologyApiRecord(record.Title, record.Subtitle, record.Data.Clone()))
                .ToArray();

            foreach (var periodGroup in assignments.GroupBy(record =>
                JsonLong(record.Data, "grading_period_id", "grading_period", "period_id").ToString(
                    System.Globalization.CultureInfo.InvariantCulture))
                .OrderBy(group => periodNames.GetValueOrDefault(group.Key, group.Key)))
            {
                var periodName = periodNames.GetValueOrDefault(periodGroup.Key,
                    periodGroup.Key == "0" ? "Ungraded" : "Grading Period");
                var periodAssignments = periodGroup.ToArray();
                AddGradeHierarchyRow(rows, periodName, 0, periodChildren =>
                {
                    foreach (var categoryGroup in periodAssignments.GroupBy(record =>
                        JsonLong(record.Data, "grading_category_id", "grading_category", "category_id").ToString(
                            System.Globalization.CultureInfo.InvariantCulture))
                        .OrderBy(group => categoryNames.GetValueOrDefault(group.Key, group.Key)))
                    {
                        var categoryName = categoryNames.GetValueOrDefault(categoryGroup.Key,
                            categoryGroup.Key == "0" ? "Ungraded" : "Category");
                        var categoryAssignments = categoryGroup.ToArray();
                        AddGradeHierarchyRow(periodChildren, categoryName, 1, categoryChildren =>
                        {
                            foreach (var assignment in categoryAssignments.OrderBy(record => record.Title,
                                StringComparer.CurrentCultureIgnoreCase))
                            {
                                var id = JsonLong(assignment.Data, "id", "assignment_id", "assignmentId");
                                var grade = id > 0 && gradesByAssignment.TryGetValue(id, out var found)
                                    ? found : default;
                                AddGradeAssignmentRow(categoryChildren, sectionId, assignment.Data, grade, 2);
                            }
                        });
                    }
                }, initiallyExpanded: expandPeriods);
            }
            if (assignments.Length == 0)
                rows.Children.Add(EmptyState("No graded items are available for this course."));
        }
        catch (Exception ex)
        {
            rows.Children.Clear();
            rows.Children.Add(EmptyState("Unable to load grades.", ex.Message));
        }
    }

    private static string FormatCourseOverallGrade(
        JsonElement sectionData, JsonElement finalGrade, JsonElement finalPeriod, JsonElement gradingScale)
    {
        var overrideString = ProfileValue(finalPeriod, "override_str", "overrideStr", "override_string", "overrideString", "grade_override", "gradeOverride");
        if (string.IsNullOrWhiteSpace(overrideString))
            overrideString = ProfileValue(finalGrade, "override_str", "overrideStr", "override_string", "overrideString", "grade_override", "gradeOverride");
        if (!string.IsNullOrWhiteSpace(overrideString)) return overrideString.Trim();
        var numericOverride = ProfileValue(finalPeriod, "override_numeric", "overrideNumeric");
        if (string.IsNullOrWhiteSpace(numericOverride)) numericOverride = ProfileValue(finalGrade, "override_numeric", "overrideNumeric");
        var rawGrade = !string.IsNullOrWhiteSpace(numericOverride) ? numericOverride :
            ProfileValue(finalPeriod, "grade") ?? ProfileValue(finalGrade, "grade", "calculated_grade", "calculatedGrade") ??
            ProfileValue(sectionData, "grade", "calculated_grade", "calculatedGrade");
        if (string.IsNullOrWhiteSpace(rawGrade)) return "—";
        if (!decimal.TryParse(rawGrade.Trim().TrimEnd('%').Trim(), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var numeric)) return rawGrade.Trim();
        var percentage = decimal.Round(numeric, 2, MidpointRounding.AwayFromZero)
            .ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";
        if (gradingScale.ValueKind != JsonValueKind.Object || JsonLong(gradingScale, "type", "scale_type", "scaleType") != 1)
            return percentage;
        var definition = gradingScale;
        if (gradingScale.TryGetProperty("scale", out var nestedScale)) definition = nestedScale;
        JsonElement levels = default;
        if (definition.ValueKind == JsonValueKind.Object &&
            !definition.TryGetProperty("level", out levels) && !definition.TryGetProperty("levels", out levels)) levels = default;
        if (levels.ValueKind == JsonValueKind.Object && levels.TryGetProperty("level", out var nestedLevels)) levels = nestedLevels;
        var levelItems = new List<JsonElement>();
        if (levels.ValueKind == JsonValueKind.Array) levelItems.AddRange(levels.EnumerateArray());
        else if (levels.ValueKind == JsonValueKind.Object) levelItems.Add(levels);
        var parsed = new List<(string Grade, decimal Cutoff)>();
        foreach (var level in levelItems)
        {
            var grade = ProfileValue(level, "grade", "title", "name");
            var cutoff = ProfileValue(level, "cutoff", "average", "percent", "percentage");
            if (!string.IsNullOrWhiteSpace(grade) && decimal.TryParse(cutoff, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var threshold)) parsed.Add((grade, threshold));
        }
        var letter = parsed.OrderByDescending(level => level.Cutoff).FirstOrDefault(level => numeric >= level.Cutoff).Grade;
        if (string.IsNullOrWhiteSpace(letter)) return percentage;
        return JsonBoolean(gradingScale, "hide_numeric", "hideNumeric") ? letter : $"{letter} ({percentage})";
    }

    private async Task LoadStandaloneOverallGradeAsync(long sectionId, JsonElement sectionFallback, TextBlock target)
    {
        try
        {
            var session = _session;
            if (session is null || sectionId <= 0) return;

            // Match the Android FinalGradesApiHelper and the Tauri hydrateUserCourseGrades
            // flow: grades + active enrollments identify this student's final-grade record;
            // grading scales and section options format it. Do not substitute a separate
            // users/{uid}/grades?section_id response, which is not part of either flow.
            async Task<JsonDocument?> TryLoadAsync(string endpoint, Func<Task<JsonDocument>> request)
            {
                try { return await request(); }
                catch (Exception ex)
                {
                    App.WriteStartupLog($"Could not load {endpoint} for overall grade in section {sectionId}.", ex);
                    return null;
                }
            }

            var responses = await Task.WhenAll(
                TryLoadAsync("section grades", () => _api.GetSectionGradesAsync(session, sectionId)),
                TryLoadAsync("grading scales", () => _api.GetGradingScalesAsync(session, sectionId)),
                TryLoadAsync("section enrollments", () => _api.GetSectionEnrollmentsAsync(session, sectionId)),
                TryLoadAsync("section details", () => _api.GetSectionDetailsAsync(session, sectionId)));
            using var gradesDocument = responses[0];
            using var scalesDocument = responses[1];
            using var enrollmentsDocument = responses[2];
            using var sectionDocument = responses[3];

            var finalRecords = SchoologyApiClient.ReadRecords(gradesDocument?.RootElement ?? default,
                "final_grade", "finalGrade", "final_grades", "finalGrades");
            var enrollmentRecords = SchoologyApiClient.ReadRecords(enrollmentsDocument?.RootElement ?? default,
                "enrollment", "enrollments");
            var ownEnrollment = enrollmentRecords.FirstOrDefault(record =>
                string.Equals(ProfileValue(record.Data, "uid", "user_id", "userId"),
                    session.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
            var enrollmentId = JsonLong(ownEnrollment?.Data ?? default, "id", "enrollment_id", "enrollmentId");
            if (enrollmentId <= 0)
                enrollmentId = JsonLong(sectionFallback, "enrollment_id", "enrollmentId");
            if (enrollmentId <= 0 && sectionFallback.ValueKind == JsonValueKind.Object &&
                sectionFallback.TryGetProperty("enrollment", out var embeddedEnrollment))
                enrollmentId = JsonLong(embeddedEnrollment, "id", "enrollment_id", "enrollmentId");

            // Android joins final_grade.enrollment_id to an enrollment; Tauri performs the
            // same match and only accepts a sole grade record when no matching ID exists.
            var finalRecord = finalRecords.FirstOrDefault(record =>
                enrollmentId > 0 && JsonLong(record.Data, "enrollment_id", "enrollmentId") == enrollmentId);
            if (finalRecord is null && finalRecords.Count == 1) finalRecord = finalRecords[0];

            // Some /users/{uid}/grades section records already include final_grade. Keep
            // that embedded representation as a fallback, but never choose an arbitrary
            // student's entry from a multi-record collection.
            if (finalRecord is null)
            {
                var embeddedFinals = SchoologyApiClient.ReadRecords(sectionFallback,
                    "final_grade", "finalGrade", "final_grades", "finalGrades");
                finalRecord = embeddedFinals.FirstOrDefault(record =>
                    enrollmentId > 0 && JsonLong(record.Data, "enrollment_id", "enrollmentId") == enrollmentId);
                if (finalRecord is null && embeddedFinals.Count == 1) finalRecord = embeddedFinals[0];
            }

            var finalData = finalRecord?.Data ?? default;
            var periods = SchoologyApiClient.ReadRecords(finalData,
                "period", "periods", "final_period", "finalPeriod", "final_grade_period", "finalGradePeriod");
            var finalPeriod = periods.FirstOrDefault(record =>
                string.Equals(ProfileValue(record.Data, "period_id", "periodId"), "final",
                    StringComparison.OrdinalIgnoreCase));
            var finalPeriodData = finalPeriod?.Data ?? default;

            var sectionDetails = sectionDocument?.RootElement ?? sectionFallback;
            if (sectionDetails.ValueKind == JsonValueKind.Object &&
                sectionDetails.TryGetProperty("section", out var nestedSection)) sectionDetails = nestedSection;
            if (sectionDetails.ValueKind == JsonValueKind.Object &&
                sectionDetails.TryGetProperty("data", out var nestedData) &&
                nestedData.ValueKind == JsonValueKind.Object) sectionDetails = nestedData;

            var scales = SchoologyApiClient.ReadRecords(scalesDocument?.RootElement ?? default,
                "grading_scale", "gradingScale", "scales", "scale");
            var scaleId = JsonLong(finalPeriodData, "scale_id", "scaleId");
            if (scaleId <= 0) scaleId = JsonLong(finalData, "scale_id", "scaleId");
            if (scaleId <= 0 && sectionDetails.ValueKind == JsonValueKind.Object &&
                sectionDetails.TryGetProperty("options", out var options))
                scaleId = JsonLong(options, "default_grading_scale_id", "defaultGradingScaleId");
            var scale = scales.FirstOrDefault(record =>
                scaleId > 0 && JsonLong(record.Data, "id", "scale_id", "scaleId") == scaleId);
            if (scale is null && scales.Count == 1) scale = scales[0];

            var display = FormatCourseOverallGrade(sectionFallback, finalData, finalPeriodData, scale?.Data ?? default);
            target.Text = display;
            App.WriteStartupLog(
                $"Resolved overall grade for section {sectionId}: grade-records={finalRecords.Count}, " +
                $"enrollments={enrollmentRecords.Count}, matched-enrollment={(enrollmentId > 0)}, " +
                $"final-period={(finalPeriod is not null)}, has-grade={!string.Equals(display, "—", StringComparison.Ordinal)}.");
        }
        catch (Exception ex)
        {
            App.WriteStartupLog($"Could not resolve standalone overall grade for section {sectionId}.", ex);
        }
    }

    private Grid BuildGradesPage(long? onlyCourseId = null)
    {
        var root = new Grid
        {
            Margin = new Thickness(24),
            RowSpacing = 10,
            MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 0 });
        root.Children.Add(Text("Grades", 24, true));

        // Keep the scroll viewport in a star-sized row so it receives the finite
        // height offered by the shell Frame. The expanded grade hierarchy is the
        // ScrollViewer content (not a ListView inside another scrolling surface),
        // allowing the viewport to grow with the window while scrolling its full
        // expanded course content when it exceeds the available height.
        var courseList = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Stretch };
        var viewport = new ScrollViewer
        {
            Content = courseList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Enabled,
            ZoomMode = ZoomMode.Disabled,
            IsTabStop = true,
            MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent)
        };
        AutomationProperties.SetAutomationId(viewport, "SchoologyGradesList");
        Grid.SetRow(viewport, 1);
        root.Children.Add(viewport);

        foreach (var course in _courses.Where(course => onlyCourseId is null || course.Id == onlyCourseId.Value))
        {
            var courseWrapper = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            var header = new Grid { ColumnSpacing = 12, Padding = new Thickness(12), MinHeight = 56, HorizontalAlignment = HorizontalAlignment.Stretch };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = Text(course.Name, 15, true);
            name.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(name);
            var overallText = Text("—", 13, true);
            overallText.VerticalAlignment = VerticalAlignment.Center;
            overallText.HorizontalAlignment = HorizontalAlignment.Right;
            overallText.MinWidth = 76;
            AutomationProperties.SetName(overallText, $"Overall grade for {course.Name}");
            Grid.SetColumn(overallText, 1);
            header.Children.Add(overallText);
            _ = LoadStandaloneOverallGradeAsync(course.Id, course.Data, overallText);
            var arrow = new FontIcon { Glyph = "\uE70D", FontSize = 14,
                Foreground = ThemeBrush("TextFillColorSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(arrow, 2);
            header.Children.Add(arrow);
            var toggle = new Button
            {
                Content = header, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(0),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0)
            };
            AutomationProperties.SetName(toggle, $"Grades for {course.Name}");
            courseWrapper.Children.Add(toggle);
            var gradeRows = new StackPanel { Spacing = 2, Visibility = Visibility.Collapsed };
            courseWrapper.Children.Add(gradeRows);
            var loaded = false;
            var loading = false;
            var expanded = false;
            toggle.Click += async (_, _) =>
            {
                expanded = !expanded;
                gradeRows.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                arrow.Glyph = expanded ? "\uE70E" : "\uE70D";
                if (!expanded || loaded || loading) return;
                loading = true;
                gradeRows.Children.Clear();
                gradeRows.Children.Add(LoadingStatus("Loading grades…"));
                try
                {
                    var session = _session ?? throw new InvalidOperationException("Sign in to view grades.");
                    async Task<JsonDocument?> TryLoadAsync(Func<Task<JsonDocument>> request)
                    {
                        try { return await request(); }
                        catch (Exception ex) { App.WriteStartupLog($"A grade endpoint was unavailable for section {course.Id}.", ex); return null; }
                    }
                    var responses = await Task.WhenAll(
                        TryLoadAsync(() => _api.GetUserGradesForSectionAsync(session, course.Id)),
                        TryLoadAsync(() => _api.GetGradingPeriodsAsync(session, course.Id)),
                        TryLoadAsync(() => _api.GetGradingCategoriesAsync(session, course.Id)),
                        TryLoadAsync(() => _api.GetGradeItemsAsync(session, course.Id)));
                    using var userGrades = responses[0];
                    using var periodDocument = responses[1];
                    using var categoryDocument = responses[2];
                    using var itemDocument = responses[3];
                    var sections = SchoologyApiClient.ReadRecords(userGrades?.RootElement ?? default, "section", "sections");
                    var section = sections.FirstOrDefault(record =>
                        JsonLong(record.Data, "section_id", "sectionId", "id") == course.Id);
                    var sectionData = section?.Data ?? course.Data;
                    if (sectionData.ValueKind != JsonValueKind.Object)
                    {
                        gradeRows.Children.Clear();
                        gradeRows.Children.Add(EmptyState("No grades are available for this course."));
                        loaded = true;
                        return;
                    }

                    gradeRows.Children.Clear();
                    var embeddedFinalGrades = SchoologyApiClient.ReadRecords(sectionData,
                        "final_grade", "finalGrade", "final_grades", "finalGrades");
                    var embeddedFinal = embeddedFinalGrades.Count == 1 ? embeddedFinalGrades[0].Data : default;
                    var embeddedPeriods = SchoologyApiClient.ReadRecords(embeddedFinal,
                        "period", "periods", "final_period", "finalPeriod", "final_grade_period", "finalGradePeriod");
                    var embeddedFinalPeriod = embeddedPeriods.FirstOrDefault(record =>
                        string.Equals(ProfileValue(record.Data, "period_id", "periodId"), "final", StringComparison.OrdinalIgnoreCase));
                    var summary = new Grid { Padding = new Thickness(14, 10, 14, 10), ColumnSpacing = 10 };
                    summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    summary.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    summary.Children.Add(Text("Overall Grade", 14, true));
                    var value = Text(FormatCourseOverallGrade(sectionData, embeddedFinal,
                        embeddedFinalPeriod?.Data ?? default, default), 14, true);
                    Grid.SetColumn(value, 1);
                    summary.Children.Add(value);
                    gradeRows.Children.Add(summary);

                    var gradesByAssignment = new Dictionary<long, JsonElement>();
                    foreach (var period in SchoologyApiClient.ReadRecords(sectionData, "period", "periods"))
                    foreach (var assignmentGrade in SchoologyApiClient.ReadRecords(period.Data, "assignment", "assignments"))
                    {
                        var assignmentId = JsonLong(assignmentGrade.Data, "assignment_id", "assignmentId", "id");
                        if (assignmentId > 0) gradesByAssignment[assignmentId] = assignmentGrade.Data;
                    }

                    var periodNames = SchoologyApiClient.ReadRecords(periodDocument?.RootElement ?? default,
                        "grading_period", "gradePeriod", "period", "periods")
                        .GroupBy(record => JsonLong(record.Data, "id", "grading_period_id", "period_id").ToString(
                            System.Globalization.CultureInfo.InvariantCulture))
                        .ToDictionary(group => group.Key, group => ProfileValue(group.First().Data, "title", "name") ?? "Grading Period");
                    var categoryNames = SchoologyApiClient.ReadRecords(categoryDocument?.RootElement ?? default,
                        "grading_category", "category", "categories")
                        .GroupBy(record => JsonLong(record.Data, "id", "grading_category_id", "category_id").ToString(
                            System.Globalization.CultureInfo.InvariantCulture))
                        .ToDictionary(group => group.Key, group => ProfileValue(group.First().Data, "title", "name") ?? "Category");
                    var assignments = SchoologyApiClient.ReadRecords(itemDocument?.RootElement ?? default,
                        "assignment", "assignments", "grade_item", "grade_items");
                    if (assignments.Count == 0)
                        assignments = SchoologyApiClient.ReadRecords(sectionData,
                            "assignment", "assignments", "grade_item", "grade_items");
                    var periodGroups = assignments.GroupBy(record =>
                        JsonLong(record.Data, "grading_period_id", "grading_period", "period_id").ToString(
                            System.Globalization.CultureInfo.InvariantCulture))
                        .OrderBy(group => periodNames.GetValueOrDefault(group.Key, group.Key));
                    foreach (var periodGroup in periodGroups)
                    {
                        var periodName = periodNames.GetValueOrDefault(periodGroup.Key,
                            periodGroup.Key == "0" ? "Ungraded" : "Grading Period");
                        var periodAssignments = periodGroup.ToArray();
                        AddGradeHierarchyRow(gradeRows, periodName, 0, periodChildren =>
                        {
                            var categoryGroups = periodAssignments.GroupBy(record =>
                                JsonLong(record.Data, "grading_category_id", "grading_category", "category_id").ToString(
                                    System.Globalization.CultureInfo.InvariantCulture))
                                .OrderBy(group => categoryNames.GetValueOrDefault(group.Key, group.Key));
                            foreach (var categoryGroup in categoryGroups)
                            {
                                var categoryName = categoryNames.GetValueOrDefault(categoryGroup.Key,
                                    categoryGroup.Key == "0" ? "Ungraded" : "Category");
                                var categoryAssignments = categoryGroup.ToArray();
                                AddGradeHierarchyRow(periodChildren, categoryName, 1, categoryChildren =>
                                {
                                    foreach (var assignment in categoryAssignments.OrderBy(record => record.Title,
                                        StringComparer.CurrentCultureIgnoreCase))
                                    {
                                        var id = JsonLong(assignment.Data, "id", "assignment_id", "assignmentId");
                                        var grade = id > 0 && gradesByAssignment.TryGetValue(id, out var found)
                                            ? found : default;
                                        AddGradeAssignmentRow(categoryChildren, course.Id, assignment.Data, grade, 2);
                                    }
                                });
                            }
                        });
                    }
                    if (assignments.Count == 0)
                        gradeRows.Children.Add(EmptyState("Assignment grade details are unavailable.",
                            itemDocument is null
                                ? "ClassWire denied access to this course's grade-item list (HTTP 403). The overall grade above is still shown."
                                : "ClassWire did not return any graded items for this course."));
                    loaded = true;
                }
                catch (Exception ex)
                {
                    gradeRows.Children.Clear();
                    gradeRows.Children.Add(EmptyState("Unable to load grades.", ex.Message));
                }
                finally { loading = false; }
            };
            courseList.Children.Add(courseWrapper);
        }
        if (_courses.Count == 0) courseList.Children.Add(EmptyState("No grades"));
        return root;
    }

    private static string NormalizeSchoologyWebUrl(string value)
    {
        var raw = value.Trim();
        if (raw.StartsWith("//", StringComparison.Ordinal)) raw = "https:" + raw;
        if (Uri.TryCreate(raw, UriKind.Absolute, out var absolute))
        {
            if (absolute.Scheme == Uri.UriSchemeHttp && IsSchoologyWebHost(absolute.Host))
                return new UriBuilder(absolute) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri.AbsoluteUri;
            return absolute.AbsoluteUri;
        }
        return new Uri(new Uri("https://app.schoology.com/"), raw.TrimStart('/')).AbsoluteUri;
    }

    private void RefreshThemeSurfaces()
    {
        UpdateSharedWebViewTheme();
        if (_sharedWebView?.Parent is not null) return;
        if (_activeNativeFileViewerRoot is not null)
        {
            _refreshNativeFileViewer?.Invoke();
            return;
        }
        if (_lastRenderedPage == "splash") return;
        if (_page == "login")
        {
            ShowLogin();
            return;
        }
        if (_page == "home")
        {
            ShowHome(_section);
            return;
        }
        if (_page is "Courses" or "Groups" or "School Profile")
        {
            var selected = _page == "Courses"
                ? _courses.FirstOrDefault(entity => entity.Id == _selectedEntityId)
                : _page == "Groups"
                    ? _groups.FirstOrDefault(entity => entity.Id == _selectedEntityId)
                    : GetSchoolProfileEntities().FirstOrDefault(entity => entity.Id == _selectedEntityId);
            ShowThreePane(_page, selected);
        }
    }

    private void UpdateSharedWebViewTheme()
    {
        if (_sharedWebView is null) return;
        try
        {
            var core = _sharedWebView.CoreWebView2;
            if (core is null) return;
            core.Profile.PreferredColorScheme =
                _root.ActualTheme == ElementTheme.Dark
                    ? CoreWebView2PreferredColorScheme.Dark
                    : CoreWebView2PreferredColorScheme.Light;
        }
        catch (InvalidOperationException) { }
    }

    private static bool IsSchoologyWebHost(string host)
    {
        host = host.ToLowerInvariant();
        return host == "schoology.com" || host.EndsWith(".schoology.com", StringComparison.Ordinal) ||
            host == "schoologyca.com" || host.EndsWith(".schoologyca.com", StringComparison.Ordinal) || host == "lms.lausd.net";
    }

    private async Task OpenSharedWebViewAsync(string rawUrl, string title, bool allowLtiExternal = false,
        System.Action? onBack = null, bool requiresBrowserSession = true,
        Func<Uri, Task<bool>>? navigationHandler = null)
    {
        await _sharedWebViewGate.WaitAsync();
        try
        {
            if (_session is null) throw new InvalidOperationException("Sign in to open ClassWire web content.");
            if (!Uri.TryCreate(NormalizeSchoologyWebUrl(rawUrl), UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidOperationException("Only secure HTTPS links can be opened in the shared ClassWire view.");
            if (!IsSchoologyWebHost(uri.Host) && !allowLtiExternal)
            {
                await Windows.System.Launcher.LaunchUriAsync(uri);
                return;
            }

            var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Schoology", "WebView2");
            Directory.CreateDirectory(dataFolder);
            _sharedWebViewEnvironment ??= await CoreWebView2Environment.CreateWithOptionsAsync(
                null, dataFolder, new CoreWebView2EnvironmentOptions());

            // Recreate the visual control while retaining the persistent browser profile.
            // This avoids reusing a CoreWebView2 after its previous Frame host was unloaded.
            var webView = new WebView2
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                MinWidth = 400,
                MinHeight = 320
            };
            await webView.EnsureCoreWebView2Async(_sharedWebViewEnvironment);
            var core = webView.CoreWebView2 ?? throw new InvalidOperationException(
                "The Microsoft Edge WebView2 runtime did not initialize. Repair or install the WebView2 Runtime and retry.");
            _sharedWebView = webView;
            UpdateSharedWebViewTheme();
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.IsStatusBarEnabled = false;

            if (!_sharedWebViewInitialized || _sharedWebViewSessionUserId != _session.UserId)
            {
                core.CookieManager.DeleteAllCookies();
                _sharedWebViewInitialized = false;
                _sharedWebViewSessionUserId = _session.UserId;
            }

            if (requiresBrowserSession)
            {
                var cookies = await _auth.GetWebSessionCookiesAsync(_session);
                foreach (var item in cookies)
                {
                    var cookie = core.CookieManager.CreateCookie(item.Name, item.Value, item.Domain, item.Path);
                    cookie.IsHttpOnly = item.HttpOnly;
                    cookie.IsSecure = item.Secure;
                    core.CookieManager.AddOrUpdateCookie(cookie);
                }
                var installedCookies = await core.CookieManager.GetCookiesAsync(_auth.WebSessionUrl);
                if (!installedCookies.Any(cookie => cookie.Name.StartsWith("SESS", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(cookie.Value)))
                    throw new InvalidOperationException("The shared ClassWire WebView did not receive a usable SESS cookie. Sign in again and retry.");
                _sharedWebViewInitialized = true;
            }
            else
            {
                // v2 course-app launch URLs carry their own single-use authorization.
                _sharedWebViewInitialized = true;
            }

            var statusHost = LoadingStatus("Loading…", out var status, 16);
            status.Foreground = ThemeBrush("SchoologyMutedTextBrush");
            core.NavigationStarting += async (_, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var destination)) return;
                if (destination.Scheme != Uri.UriSchemeHttps)
                {
                    args.Cancel = true;
                    return;
                }
                if (navigationHandler is not null && await navigationHandler(destination))
                {
                    args.Cancel = true;
                    return;
                }
                if (!allowLtiExternal && !IsSchoologyWebHost(destination.Host))
                {
                    args.Cancel = true;
                    await Windows.System.Launcher.LaunchUriAsync(destination);
                    return;
                }
                SetStatusText(status, "Loading…");
            };
            core.NavigationCompleted += (_, args) =>
                SetStatusText(status, args.IsSuccess ? " " : "Unable to load this page. Use Reload to try again.");
            core.NewWindowRequested += async (_, args) =>
            {
                args.Handled = true;
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var popup) ||
                    popup.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(popup.UserInfo)) return;
                if (allowLtiExternal || IsSchoologyWebHost(popup.Host))
                    core.Navigate(popup.AbsoluteUri);
                else
                    await Windows.System.Launcher.LaunchUriAsync(popup);
            };

            var host = new Grid { RowSpacing = 8, Margin = new Thickness(12) };
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var toolbar = new Grid { ColumnSpacing = 10 };
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var back = Action("‹ Back", (_, _) => (onBack ?? (() => ShowHome(_section)))());
            toolbar.Children.Add(back);
            var heading = Text(title, 16, true);
            Grid.SetColumn(heading, 1);
            toolbar.Children.Add(heading);
            Grid.SetColumn(statusHost, 2);
            toolbar.Children.Add(statusHost);
            var reload = Action("Reload", (_, _) => core.Reload());
            Grid.SetColumn(reload, 3);
            toolbar.Children.Add(reload);
            host.Children.Add(toolbar);
            Grid.SetRow(webView, 1);
            host.Children.Add(webView);
            if (_sectionFrame is not null) NavigateMainSection(host, title);
            else Reset(BuildApplicationShell(title, host));
            core.Navigate(uri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            await ShowApiErrorAsync("Unable to open ClassWire web content", ex.Message);
        }
        finally
        {
            _sharedWebViewGate.Release();
        }
    }

    private void ShowUserProfile(long userId)
    {
        if (_session is null || userId <= 0) return;
        if (_section != "Profile")
        {
            if (_appNavigation?.SelectedItem is NavigationViewItem selectedNavigationItem &&
                selectedNavigationItem.Tag is string selectedTag && !string.IsNullOrWhiteSpace(selectedTag))
                _profileReturnNavigationTag = selectedTag;
            _profileReturnSection = _section;
        }
        _activeProfileUserId = userId;
        _page = "home";
        _section = "Profile";
        UpdateTitleBarCreateButton("Profile", null);
        // Only the signed-in user's own profile corresponds to the Profile
        // navigation destination. Viewing another person's profile is a
        // contextual drill-in and must preserve the current navigation selection.
        if (userId == _session.UserId && _appNavigation is not null)
        {
            _syncingNavigationSelection = true;
            _appNavigation.SelectedItem = _appNavigation.MenuItems.OfType<NavigationViewItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, "Profile", StringComparison.Ordinal));
            _syncingNavigationSelection = false;
        }
        var page = BuildProfilePage();
        if (_sectionFrame is not null) NavigateMainSection(page, "Profile");
        else Reset(BuildApplicationShell("Profile", page));
    }

    private void ReturnFromUserProfile()
    {
        _activeProfileUserId = 0;
        if (_sectionFrame?.CanGoBack == true)
        {
            _section = _profileReturnSection;
            _page = "home";
            _sectionFrame.GoBack();
            return;
        }

        var destination = _profileReturnNavigationTag == "Home" ? "Recent Activity" : _profileReturnNavigationTag;
        ShowHome(destination);
    }

    private Grid BuildProfilePage()
    {
        var session = _session;
        var userId = _activeProfileUserId > 0 ? _activeProfileUserId : session?.UserId ?? 0;
        var root = new Grid { Margin = new Thickness(20), RowSpacing = 14 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        if (userId != session?.UserId)
        {
            var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var backButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { new SymbolIcon(Symbol.Back), Text("Back", 13, true) }
                },
                Padding = new Thickness(12, 7, 16, 7),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            AutomationProperties.SetAutomationId(backButton, "ProfileBackButton");
            AutomationProperties.SetName(backButton, "Back to previous page");
            backButton.Click += (_, _) => ReturnFromUserProfile();
            navigation.Children.Add(backButton);
            Grid.SetRow(navigation, 0);
            root.Children.Add(navigation);
        }

        var header = new Grid { ColumnSpacing = 14, Padding = new Thickness(14), Background = InAppAcrylicBrush() };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var avatarContent = new Grid();
        var avatarFallback = Text("U", 25, true);
        avatarFallback.HorizontalAlignment = HorizontalAlignment.Center;
        avatarFallback.VerticalAlignment = VerticalAlignment.Center;
        avatarFallback.TextAlignment = TextAlignment.Center;
        avatarContent.Children.Add(avatarFallback);
        var avatarImage = new Image
        {
            Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        avatarContent.Children.Add(avatarImage);
        var avatar = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(32),
            Background = ThemeBrush("SchoologySubtleBrush"), Child = avatarContent };
        header.Children.Add(avatar);
        var headerText = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var nameText = Text("Loading profile…", 21, true);
        var schoolText = Text("", 13);
        schoolText.Foreground = ThemeBrush("SchoologyMutedTextBrush");
        headerText.Children.Add(nameText);
        headerText.Children.Add(schoolText);
        Grid.SetColumn(headerText, 1);
        header.Children.Add(headerText);
        var headerSurface = Surface(header, new Thickness(8));
        Grid.SetRow(headerSurface, 1);
        root.Children.Add(headerSurface);

        var body = new Grid();
        Grid.SetRow(body, 2);
        root.Children.Add(body);

        var wide = new Grid { ColumnSpacing = 12 };
        for (var i = 0; i < 3; i++)
            wide.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var wideUpdates = new StackPanel { Spacing = 8 };
        var wideInfo = new StackPanel { Spacing = 8 };
        var wideBadges = new StackPanel { Spacing = 8 };
        Border MakePane(string title, StackPanel paneContent)
        {
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(Text(title, 17, true));
            panel.Children.Add(new ScrollViewer { Content = paneContent,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            return Surface(panel, new Thickness(12));
        }
        wide.Children.Add(MakePane("Updates", wideUpdates));
        var infoPane = MakePane("Info", wideInfo);
        Grid.SetColumn(infoPane, 1);
        wide.Children.Add(infoPane);
        var badgesPane = MakePane("Badges", wideBadges);
        Grid.SetColumn(badgesPane, 2);
        wide.Children.Add(badgesPane);

        var narrow = new Grid { RowSpacing = 8 };
        narrow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        narrow.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var selector = new SelectorBar { HorizontalAlignment = HorizontalAlignment.Stretch };
        var updatesTab = new SelectorBarItem { Text = "Updates" };
        var infoTab = new SelectorBarItem { Text = "Info" };
        var badgesTab = new SelectorBarItem { Text = "Badges" };
        selector.Items.Add(updatesTab);
        selector.Items.Add(infoTab);
        selector.Items.Add(badgesTab);
        selector.SelectedItem = updatesTab;
        Grid.SetRow(selector, 0);
        narrow.Children.Add(selector);
        var narrowFrame = new Frame { Background = new SolidColorBrush(Colors.Transparent) };
        Grid.SetRow(narrowFrame, 1);
        narrow.Children.Add(narrowFrame);

        JsonElement userData = default;
        IReadOnlyList<SchoologyApiRecord> updateRecords = [];
        IReadOnlyList<SchoologyApiRecord> badgeRecords = [];
        string? updatesError = null;
        string? badgesError = null;
        string? profileError = null;
        var loaded = false;

        StackPanel BuildTabContent(string tab)
        {
            var panel = new StackPanel { Spacing = 8, Margin = new Thickness(8) };
            if (tab == "Info")
            {
                if (profileError is not null)
                    panel.Children.Add(EmptyState("Unable to load profile.", profileError));
                else
                {
                AddProfileField(panel, "Email", ProfileValue(userData, "primary_email", "primaryEmail", "email"));
                AddProfileField(panel, "Username", ProfileValue(userData, "username"));
                AddProfileField(panel, "First Name", ProfileValue(userData, "first_name", "firstName", "name_first"));
                AddProfileField(panel, "Last Name", ProfileValue(userData, "last_name", "lastName", "name_last"));
                AddProfileField(panel, "School", ProfileValue(userData, "school_name", "school_title"));
                if (panel.Children.Count == 0) panel.Children.Add(EmptyState("No profile information available."));
                }
            }
            else if (tab == "Badges")
            {
                if (badgesError is not null) panel.Children.Add(EmptyState("Unable to load badges.", badgesError));
                else if (badgeRecords.Count == 0) panel.Children.Add(EmptyState("No badges."));
                else foreach (var badge in badgeRecords)
                {
                    var title = ProfileValue(badge.Data, "name", "title", "badge_name") ?? badge.Title;
                    var description = ProfileValue(badge.Data, "description", "body", "message");
                    var card = new StackPanel { Spacing = 5 };
                    card.Children.Add(Text(title, 14, true));
                    if (!string.IsNullOrWhiteSpace(description)) card.Children.Add(Text(StripMarkup(description), 12));
                    panel.Children.Add(Surface(card, new Thickness(12)));
                }
            }
            else
            {
                if (updatesError is not null) panel.Children.Add(EmptyState("Unable to load profile updates.", updatesError));
                else if (updateRecords.Count == 0) panel.Children.Add(EmptyState("No updates."));
                else foreach (var update in updateRecords)
                    panel.Children.Add(BuildUpdateCard("Profile", userId, update, userData));
            }
            return panel;
        }

        void ShowNarrowTab()
        {
            if (!loaded)
            {
                narrowFrame.Content = LoadingStatus("Loading profile…");
                return;
            }
            var tab = selector.SelectedItem == infoTab ? "Info" :
                selector.SelectedItem == badgesTab ? "Badges" : "Updates";
            narrowFrame.Content = new ScrollViewer { Content = BuildTabContent(tab),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }
        selector.SelectionChanged += (_, _) => ShowNarrowTab();
        ShowNarrowTab();

        body.Children.Add(wide);
        body.Children.Add(narrow);
        void UpdateLayout(double width)
        {
            var isWide = width >= 1000;
            wide.Visibility = isWide ? Visibility.Visible : Visibility.Collapsed;
            narrow.Visibility = isWide ? Visibility.Collapsed : Visibility.Visible;
        }
        body.SizeChanged += (_, args) => UpdateLayout(args.NewSize.Width);
        UpdateLayout(_root.ActualWidth);

        async Task LoadProfileAsync()
        {
            try
            {
                if (session is null || userId <= 0) throw new InvalidOperationException("Sign in to view this profile.");
                using var profileDocument = userId == session.UserId
                    ? await _api.GetProfileAsync(session)
                    : await _api.GetUserAsync(session, userId);
                var profileRoot = profileDocument.RootElement;
                userData = profileRoot.ValueKind == JsonValueKind.Object &&
                    profileRoot.TryGetProperty("user", out var nestedUser) ? nestedUser.Clone() : profileRoot.Clone();

                var displayName = ProfileValue(userData, "name_display", "display_name", "name", "username") ?? "ClassWire user";
                nameText.Text = displayName;
                avatarFallback.Text = displayName.Length > 0 ? displayName[..1].ToUpperInvariant() : "U";
                schoolText.Text = ProfileValue(userData, "school_name", "school_title") ??
                    (userData.ValueKind == JsonValueKind.Object && userData.TryGetProperty("school", out var school)
                        ? ProfileValue(school, "school_name", "title", "name") ?? "" : "");
                var picture = ProfileValue(userData, "picture_url", "pictureUrl", "picture", "photo_url", "avatar_url");
                if (!string.IsNullOrWhiteSpace(picture)) _ = LoadPostImageAsync(avatarImage, picture);

                try
                {
                    using var updatesDocument = await _auth.GetApiJsonAsync(session, $"users/{userId}/updates",
                        new Dictionary<string, string>
                        {
                            ["start"] = "0", ["limit"] = "20", ["with_attachments"] = "TRUE", ["richtext"] = "1"
                        });
                    updateRecords = SchoologyApiClient.ReadRecords(updatesDocument.RootElement, "update", "updates", "update_list");
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("No installed components were detected", StringComparison.OrdinalIgnoreCase))
                    {
                        updatesError = null;
                        updateRecords = [];
                        App.WriteStartupLog($"Profile updates are unavailable for user {userId}; showing the empty updates state.", ex);
                    }
                    else updatesError = ex.Message;
                }

                try
                {
                    using var badgesDocument = await _auth.GetApiJsonAsync(session, $"users/{userId}/badges", null);
                    badgeRecords = SchoologyApiClient.ReadRecords(badgesDocument.RootElement, "badge", "badges", "items");
                }
                catch (Exception ex) { badgesError = ex.Message; }

                loaded = true;
                wideUpdates.Children.Clear();
                wideInfo.Children.Clear();
                wideBadges.Children.Clear();
                foreach (var child in BuildTabContent("Updates").Children.ToArray()) wideUpdates.Children.Add(child);
                foreach (var child in BuildTabContent("Info").Children.ToArray()) wideInfo.Children.Add(child);
                foreach (var child in BuildTabContent("Badges").Children.ToArray()) wideBadges.Children.Add(child);
                ShowNarrowTab();
            }
            catch (Exception ex)
            {
                profileError = ex.Message;
                updatesError = ex.Message;
                badgesError = ex.Message;
                nameText.Text = "Profile";
                schoolText.Text = "";
                wideUpdates.Children.Clear();
                wideUpdates.Children.Add(EmptyState("Unable to load profile.", ex.Message));
                wideInfo.Children.Clear();
                wideInfo.Children.Add(EmptyState("Unable to load profile.", ex.Message));
                wideBadges.Children.Clear();
                wideBadges.Children.Add(EmptyState("Unable to load profile.", ex.Message));
                loaded = true;
                ShowNarrowTab();
            }
        }

        wideUpdates.Children.Add(LoadingStatus("Loading updates…"));
        wideInfo.Children.Add(LoadingStatus("Loading profile information…"));
        wideBadges.Children.Add(LoadingStatus("Loading badges…"));
        _ = LoadProfileAsync();
        return root;
    }

    private void AddProfileField(StackPanel panel, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var row = new StackPanel { Spacing = 3 };
        row.Children.Add(Text(label, 12, true));
        row.Children.Add(Text(value, 14));
        panel.Children.Add(Surface(row));
    }

    private static string? ProfileValue(System.Text.Json.JsonElement user, params string[] keys)
    {
        if (user.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
        foreach (var key in keys)
            if (user.TryGetProperty(key, out var value) &&
                value.ValueKind is System.Text.Json.JsonValueKind.String or System.Text.Json.JsonValueKind.Number)
                return value.ToString();
        return null;
    }

    private void ShowSearch()
    {
        _page = "Search";
        UpdateTitleBarCreateButton("Search", null);
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(24) };
        panel.Children.Add(Text("Search ClassWire", 22, true));
        var searchBox = new AutoSuggestBox { PlaceholderText = "Search courses, groups, people, and updates" };
        panel.Children.Add(searchBox);
        panel.Children.Add(Action("Back", (_, _) => ShowHome()));
        if (_sectionFrame is not null) NavigateMainSection(panel, "Search");
        else Reset(BuildApplicationShell("Recent Activity", panel));
    }
    private static string CreatePermissionPath(string kind, long entityId, string collection)
    {
        var realm = kind switch { "Groups" => "groups", "Profile" => "users", _ => "sections" };
        return kind == "Profile" ? $"v1/users/{entityId}/{collection}" : $"/v1/{realm}/{entityId}/{collection}";
    }

    private async Task<Dictionary<string, bool>> LoadPostPermissionMapAsync(SchoologyAuthService.Session session, bool includeUserEvents)
    {
        var requests = new List<string>();
        foreach (var course in _courses)
        foreach (var collection in new[] { "updates", "discussions", "events" })
            requests.Add(CreatePermissionPath("Courses", course.Id, collection));
        foreach (var group in _groups)
        foreach (var collection in new[] { "updates", "discussions", "events" })
            requests.Add(CreatePermissionPath("Groups", group.Id, collection));
        if (includeUserEvents) requests.Add(CreatePermissionPath("Profile", session.UserId, "events"));
        if (requests.Count == 0) return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        using var document = await _api.GetPostPermissionsAsync(session, requests);
        var responses = SchoologyApiClient.ReadRecords(document.RootElement, "response", "responses");
        var permissions = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var response in responses)
        {
            var location = ProfileValue(response.Data, "location");
            if (string.IsNullOrWhiteSpace(location)) continue;
            var responseCode = JsonLong(response.Data, "response_code", "responseCode");
            var body = ProfileValue(response.Data, "body") ?? string.Empty;
            permissions[location] = responseCode == 200 &&
                body.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Any(value => string.Equals(value, "POST", StringComparison.OrdinalIgnoreCase));
        }
        return permissions;
    }

    private List<(string Kind, SchoologyEntity Entity)> GetPostableDestinations(
        SchoologyAuthService.Session session, IReadOnlyDictionary<string, bool> permissions, string collection, bool includeUserEvents = false)
    {
        var result = new List<(string Kind, SchoologyEntity Entity)>();
        foreach (var course in _courses)
            if (permissions.GetValueOrDefault(CreatePermissionPath("Courses", course.Id, collection))) result.Add(("Courses", course));
        foreach (var group in _groups)
            if (permissions.GetValueOrDefault(CreatePermissionPath("Groups", group.Id, collection))) result.Add(("Groups", group));
        if (includeUserEvents && permissions.GetValueOrDefault(CreatePermissionPath("Profile", session.UserId, "events")))
            result.Add(("Profile", new SchoologyEntity(session.UserId, "My Account", string.Empty, null, default)));
        return result;
    }

    private async void ShowCreateMenu()
    {
        var session = _session;
        var context = _titleBarCreateContext;
        if (session is null || context.Length == 0) return;
        Dictionary<string, bool> permissions;
        try { permissions = await LoadPostPermissionMapAsync(session, includeUserEvents: context == "home"); }
        catch (Exception ex) { await ShowApiErrorAsync("Unable to check posting permissions", ex.Message); return; }
        if (context == "home")
        {
            var updates = GetPostableDestinations(session, permissions, "updates");
            var discussions = GetPostableDestinations(session, permissions, "discussions");
            var events = GetPostableDestinations(session, permissions, "events", includeUserEvents: true);
            var menu = new MenuFlyout();
            if (updates.Count > 0)
            {
                var item = new MenuFlyoutItem { Text = "Update", Icon = new SymbolIcon(Symbol.Edit) };
                item.Click += async (_, _) => await ShowCreateUpdateDialogCoreAsync();
                menu.Items.Add(item);
            }
            if (discussions.Count > 0)
            {
                var item = new MenuFlyoutItem { Text = "Discussion", Icon = new SymbolIcon(Symbol.Message) };
                item.Click += async (_, _) => await ShowCreateDiscussionOrEventDialogAsync(false);
                menu.Items.Add(item);
            }
            if (events.Count > 0)
            {
                var item = new MenuFlyoutItem { Text = "Event", Icon = new SymbolIcon(Symbol.Calendar) };
                item.Click += async (_, _) => await ShowCreateDiscussionOrEventDialogAsync(true);
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 0)
            {
                await ShowApiErrorAsync("Nothing to create", "Your account does not currently have permission to post an update, discussion, or event in any available destination.");
                return;
            }
            if (_titleBarCreateButton is not null) menu.ShowAt(_titleBarCreateButton);
            return;
        }
        var kind = context == "course-update" ? "Courses" : context == "group-update" ? "Groups" : string.Empty;
        var entity = kind == "Courses" ? _courses.FirstOrDefault(item => item.Id == _selectedEntityId) :
            kind == "Groups" ? _groups.FirstOrDefault(item => item.Id == _selectedEntityId) : null;
        if (entity is null || !permissions.GetValueOrDefault(CreatePermissionPath(kind, entity.Id, "updates")))
        {
            await ShowApiErrorAsync("Unable to create update", "You do not have permission to post an update in this course or group.");
            return;
        }
        var bodyComposer = CreateRichTextComposer("Write an update", "Share an update…",
            "TitleBarUpdateComposer", 120, 260);
        var bodyBox = bodyComposer.Editor;
        var content = new StackPanel { Spacing = 10, MinWidth = 360 };
        content.Children.Add(bodyComposer.Container);
        var attachments = AddPostAttachmentControls(content, session, "TitleBarUpdateComposer");
        var dialog = new ContentDialog { Title = context == "course-update" ? "Post course update" : "Post group update",
            Content = CreateDialogScrollViewport(content), PrimaryButtonText = "Post", CloseButtonText = "Cancel", XamlRoot = _root.XamlRoot,
            RequestedTheme = _highContrastEnabled ? ElementTheme.Default : _root.ActualTheme, DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var body = SerializeTextSubmission(bodyBox, out var plainText);
        if (string.IsNullOrWhiteSpace(plainText)) { await ShowApiErrorAsync("Unable to post update", "Write an update before posting."); return; }
        try
        {
            using var created = await CreateUpdateWithAttachmentsAsync(session, kind, entity, body, attachments);
            _selectedEntityTab = "Updates";
            ShowThreePane(kind, entity);
        }
        catch (Exception ex) { await ShowApiErrorAsync("Unable to post update", ex.Message); }
    }
}

