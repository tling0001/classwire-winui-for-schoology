using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Microsoft.Win32;
using System.Text;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;

namespace Schoology.WinUI;

public partial class App : Application
{
    private Window? _window;
    private DispatcherQueue? _dispatcherQueue;
    private static Mutex? _instanceMutex;
    private static readonly long StartupDiagnosticsStartedAt = Environment.TickCount64;
    private static int _startupXamlExceptionCount;
    private static int _uiWatchdogStarted;
    private static int _uiHeartbeatQueued;
    private static int _uiStallDumpWritten;
    private static int _uiWasStalled;
    private static long _lastUiHeartbeatMilliseconds;
    private const uint MiniDumpWithFullMemory = 0x00000002;
    private const uint MiniDumpWithThreadInfo = 0x00001000;
    [DllImport("Dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MiniDumpWriteDump(
        IntPtr processHandle, uint processId, SafeFileHandle dumpFile, uint dumpType,
        IntPtr exceptionParam, IntPtr userStreamParam, IntPtr callbackParam);
    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    private const uint WmCopyData = 0x004A;
    [StructLayout(LayoutKind.Sequential)]
    private struct CopyDataStruct
    {
        public IntPtr Data;
        public int ByteCount;
        public IntPtr Buffer;
    }

    private static string? GetProtocolUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var candidate = value.Trim().Trim('"');
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, "schoology", StringComparison.OrdinalIgnoreCase)
                ? uri.AbsoluteUri : null;
    }

    private static bool ForwardProtocolUrl(IntPtr window, string url)
    {
        var textPointer = Marshal.StringToHGlobalUni(url);
        var dataPointer = Marshal.AllocHGlobal(Marshal.SizeOf<CopyDataStruct>());
        try
        {
            var data = new CopyDataStruct
            {
                Data = new IntPtr(0x534759),
                ByteCount = Encoding.Unicode.GetByteCount(url) + sizeof(char),
                Buffer = textPointer
            };
            Marshal.StructureToPtr(data, dataPointer, false);
            return SendMessage(window, WmCopyData, IntPtr.Zero, dataPointer) != IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(dataPointer);
            Marshal.FreeHGlobal(textPointer);
        }
    }
    internal static bool IsBackgroundLaunch { get; private set; }
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Schoology", "Logs", "startup.log");

    public App()
    {
        // Configure WER LocalDumps without replacing the CLR's native exception
        // filter. WinUI and the CLR own that filter chain; overriding it can
        // interfere with stowed-XAML exception reporting.
        try
        {
            ConfigureWindowsCrashDumps();
            WriteStartupLog("Per-user Windows Error Reporting crash dumps configured; CLR exception handling left intact.");
        }
        catch (Exception ex)
        {
            WriteStartupLog("Could not configure native crash diagnostics; managed diagnostics remain active.", ex);
        }

        // Observe first-chance XAML/WinRT exceptions before WinUI stows them and
        // terminates with STATUS_STOWED_EXCEPTION (0xC000027B). Keep this limited
        // to the first 20 seconds and the first 12 relevant exceptions.
        AppDomain.CurrentDomain.FirstChanceException += OnStartupFirstChanceException;

        // Register before InitializeComponent so XAML startup failures are captured.
        UnhandledException += (_, args) =>
        {
            WriteStartupLog($"Microsoft.UI.Xaml.Application.UnhandledException (Handled={args.Handled})", args.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteStartupLog($"AppDomain.UnhandledException (IsTerminating={args.IsTerminating})", args.ExceptionObject as Exception);

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            WriteStartupLog($"AppDomain.ProcessExit reached. ExitCode={Environment.ExitCode}.");

        TaskScheduler.UnobservedTaskException += (_, args) =>
            WriteStartupLog($"TaskScheduler.UnobservedTaskException (Observed={args.Observed})", args.Exception);

        WriteStartupLog("App constructor entered.");
        InitializeComponent();
        WriteStartupLog("App.InitializeComponent completed.");
    }

    private static void OnStartupFirstChanceException(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (Environment.TickCount64 - StartupDiagnosticsStartedAt > 20_000) return;

        var exception = args.Exception;
        var typeName = exception.GetType().FullName ?? string.Empty;
        var stackTrace = exception.StackTrace ?? string.Empty;
        var isXamlOrWinRt = typeName.Contains("Xaml", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("WinRT", StringComparison.OrdinalIgnoreCase) ||
            typeName.EndsWith("COMException", StringComparison.Ordinal) ||
            stackTrace.Contains("Microsoft.UI.Xaml", StringComparison.OrdinalIgnoreCase) ||
            stackTrace.Contains("WinRT", StringComparison.OrdinalIgnoreCase);
        if (!isXamlOrWinRt) return;

        var count = Interlocked.Increment(ref _startupXamlExceptionCount);
        if (count <= 12)
            WriteStartupLog($"Startup first-chance XAML/WinRT exception #{count} (type={typeName}, HRESULT=0x{exception.HResult:X8}).", exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        WriteStartupLog("OnLaunched entered.");
        if ((args.Arguments ?? string.Empty).Contains("--unregister-notifications", StringComparison.OrdinalIgnoreCase))
        {
            try { AppNotificationManager.Default.Unregister(); WriteStartupLog("Windows App Notifications unregistered for uninstall."); }
            catch (Exception ex) { WriteStartupLog("Could not unregister Windows App Notifications during uninstall.", ex); }
            Environment.Exit(0);
            return;
        }
        var protocolUrl = GetProtocolUrl(args.Arguments);
        if (protocolUrl is null)
        {
            foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
            {
                protocolUrl = GetProtocolUrl(argument);
                if (protocolUrl is not null) break;
            }
        }
        _instanceMutex = new Mutex(initiallyOwned: true, name: @"Local\Schoology.WinUI.Singleton", createdNew: out var isFirstInstance);
        if (!isFirstInstance)
        {
            var existingWindow = FindWindow(null, "ClassWire");
            if (existingWindow != IntPtr.Zero)
            {
                if (protocolUrl is not null && !ForwardProtocolUrl(existingWindow, protocolUrl))
                    WriteStartupLog("The existing instance did not acknowledge the incoming Schoology URL.");
                PostMessage(existingWindow, 0x8000 + 42, IntPtr.Zero, IntPtr.Zero);
            }
            WriteStartupLog(protocolUrl is null
                ? "Forwarded launch request to the existing background instance."
                : "Forwarded Schoology protocol URL to the existing instance.");
            GC.KeepAlive(_instanceMutex);
            return;
        }
        IsBackgroundLaunch = (args.Arguments ?? string.Empty).Contains("--background", StringComparison.OrdinalIgnoreCase);
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        try
        {
            var notificationManager = AppNotificationManager.Default;
            notificationManager.NotificationInvoked += OnNotificationInvoked;
            notificationManager.Register();
            WriteStartupLog("Windows App Notifications registered.");
        }
        catch (Exception notificationException)
        {
            WriteStartupLog("Windows App Notifications could not be registered; continuing without toast delivery.", notificationException);
        }
        try
        {
            WriteStartupLog("Creating MainWindow.");
            _window = new MainWindow(protocolUrl);
            WriteStartupLog("MainWindow constructor completed.");

            _window.Activate();
            WriteStartupLog("MainWindow.Activate completed.");
            if (_dispatcherQueue is not null)
                StartUiDispatcherWatchdog(_dispatcherQueue);
            if (IsBackgroundLaunch && _window is MainWindow mainWindow) mainWindow.HideToBackground();
        }
        catch (Exception ex)
        {
            WriteStartupLog("Exception caught in OnLaunched.", ex);
            throw;
        }
    }

    private static void ConfigureWindowsCrashDumps()
    {
        var dumpDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Schoology", "Logs", "CrashDumps");
        Directory.CreateDirectory(dumpDirectory);
        using var key = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\Windows Error Reporting\LocalDumps\ClassWire.exe",
            writable: true) ?? throw new InvalidOperationException("Could not create the per-user Windows Error Reporting settings.");
        key.SetValue("DumpFolder", dumpDirectory, RegistryValueKind.ExpandString);
        key.SetValue("DumpCount", 5, RegistryValueKind.DWord);
        key.SetValue("DumpType", 2, RegistryValueKind.DWord);
    }

    private static void StartUiDispatcherWatchdog(DispatcherQueue dispatcherQueue)
    {
        if (Interlocked.Exchange(ref _uiWatchdogStarted, 1) != 0) return;

        Interlocked.Exchange(ref _lastUiHeartbeatMilliseconds, Environment.TickCount64);
        WriteStartupLog("Independent UI-dispatcher watchdog started; it will capture a full dump if the UI thread stops processing work.");

        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

                if (Interlocked.Exchange(ref _uiHeartbeatQueued, 1) == 0)
                {
                    if (!dispatcherQueue.TryEnqueue(() =>
                    {
                        Interlocked.Exchange(ref _lastUiHeartbeatMilliseconds, Environment.TickCount64);
                        Interlocked.Exchange(ref _uiHeartbeatQueued, 0);
                        if (Interlocked.Exchange(ref _uiWasStalled, 0) != 0)
                            WriteStartupLog("UI dispatcher heartbeat resumed after a stall.");
                    }))
                    {
                        Interlocked.Exchange(ref _uiHeartbeatQueued, 0);
                    }
                }

                var stalledMilliseconds = Environment.TickCount64 -
                    Interlocked.Read(ref _lastUiHeartbeatMilliseconds);
                if (stalledMilliseconds < 5000 ||
                    Interlocked.Exchange(ref _uiStallDumpWritten, 1) != 0)
                    continue;

                Interlocked.Exchange(ref _uiWasStalled, 1);
                WriteStartupLog($"UI dispatcher has not processed queued work for {stalledMilliseconds} ms; capturing a full process dump.");
                WriteUiStallDump();
            }
        });
    }

    private static void WriteUiStallDump()
    {
        try
        {
            var dumpDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Schoology", "Logs", "CrashDumps");
            Directory.CreateDirectory(dumpDirectory);
            var dumpPath = Path.Combine(
                dumpDirectory,
                $"ui-dispatcher-stall-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.dmp");

            using var process = Process.GetCurrentProcess();
            using var dumpFile = new FileStream(
                dumpPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            var written = MiniDumpWriteDump(
                process.Handle,
                (uint)process.Id,
                dumpFile.SafeFileHandle,
                MiniDumpWithFullMemory | MiniDumpWithThreadInfo,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            if (!written)
            {
                var error = Marshal.GetLastWin32Error();
                dumpFile.Dispose();
                try { File.Delete(dumpPath); } catch { }
                WriteStartupLog($"MiniDumpWriteDump failed (Win32 error {error}).");
                return;
            }

            dumpFile.Flush(flushToDisk: true);
            WriteStartupLog($"Full UI-stall process dump saved: {dumpPath} ({dumpFile.Length:N0} bytes).");
        }
        catch (Exception ex)
        {
            WriteStartupLog("Could not capture the UI-stall process dump.", ex);
        }
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            if (_window is MainWindow mainWindow) mainWindow.ShowFromBackgroundNotification();
        });
    }

    internal static void WriteStartupLog(string message, Exception? exception = null)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var entry = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(" [")
                .Append(Environment.ProcessId)
                .Append("] ")
                .AppendLine(message);

            if (exception is not null)
            {
                for (Exception? current = exception; current is not null; current = current.InnerException)
                {
                    entry.Append("Exception detail: ")
                        .Append(current.GetType().FullName)
                        .Append(" | HRESULT=0x")
                        .Append(current.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture))
                        .Append(" | ")
                        .AppendLine(current.Message);
                    entry.AppendLine(current.StackTrace);
                }
                if (exception.Data.Count > 0)
                {
                    entry.AppendLine("Exception data:");
                    foreach (System.Collections.DictionaryEntry item in exception.Data)
                        entry.Append(item.Key).Append(" = ").AppendLine(item.Value?.ToString());
                }
                entry.AppendLine(exception.ToString());
            }

            File.AppendAllText(LogPath, entry.ToString());
        }
        catch
        {
            // Diagnostics must never introduce or worsen a startup failure.
        }
    }
}
