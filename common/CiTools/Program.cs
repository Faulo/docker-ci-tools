using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace CiTools;

static class Program {
    const long DEFAULT_TIMEOUT_SECONDS = 86_400;
    static readonly TimeSpan terminationGracePeriod = TimeSpan.FromSeconds(10);

    public static async Task<int> Main(string[] args) {
        string executable = ResolveExecutable(
            Environment.ProcessPath,
            Environment.GetCommandLineArgs(),
            ReadKernelExecutableArgument());
        try {
            string[] command = CommandRouter.Route(executable, args);
            return await RunCiToolsAsync(command, RuntimeCredentials.Resolve());
        } catch (Exception exception) {
            await Console.Error.WriteLineAsync($"{executable}: {exception.Message}");
            return 1;
        }
    }

    internal static string ResolveExecutable(
        string? processPath,
        IReadOnlyList<string> commandLineArguments,
        string? kernelExecutableArgument = null) {
        string path = kernelExecutableArgument
                      ?? (commandLineArguments.Count > 0
                          ? commandLineArguments[0]
                          : processPath ?? "ci-tools-launcher");
        return Path.GetFileNameWithoutExtension(path);
    }

    static string? ReadKernelExecutableArgument() {
        if (!OperatingSystem.IsLinux()) {
            return null;
        }

        try {
            byte[] commandLine = File.ReadAllBytes("/proc/self/cmdline");
            int terminator = Array.IndexOf(commandLine, (byte)0);
            return Encoding.UTF8.GetString(commandLine, 0, terminator >= 0 ? terminator : commandLine.Length);
        } catch {
            return null;
        }
    }

    static async Task<int> RunCiToolsAsync(string[] args, RuntimeCredentials credentials) {
        long timeoutSeconds = ParseTimeout();
        string invocationId = Guid.NewGuid().ToString("N")[..12];
        ChildProcess? child = null;

        using var cancellation = new CancellationTokenSource();
        using var signals = SignalHandlers.Register(cancellation);

        try {
            child = ProcessTree.StartComposer(args, invocationId, credentials);
            child.Resume();

            var processExit = child.process.WaitForExitAsync();
            var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
            var timeoutTask = timeoutSeconds == 0
                ? Task.Delay(Timeout.InfiniteTimeSpan)
                : WaitForTimeoutAsync(timeoutSeconds);
            var completed = await Task.WhenAny(processExit, cancellationTask, timeoutTask);

            if (completed == timeoutTask) {
                await child.TerminateAsync(terminationGracePeriod);
                return 124;
            }

            if (completed == cancellationTask) {
                await child.TerminateAsync(terminationGracePeriod);
                return 130;
            }

            await processExit;
            return child.process.ExitCode;
        } finally {
            child?.Dispose();
        }
    }

    static long ParseTimeout() =>
        ParseTimeout(Environment.GetEnvironmentVariable("CI_TOOLS_CALL_TIMEOUT"));

    internal static long ParseTimeout(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return DEFAULT_TIMEOUT_SECONDS;
        }

        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds)
            || seconds < 0
            || seconds > (long)TimeSpan.MaxValue.TotalSeconds) {
            throw new ArgumentException($"Invalid CI_TOOLS_CALL_TIMEOUT: {SanitizeText(value, 80)}");
        }

        return seconds;
    }

    static async Task WaitForTimeoutAsync(long timeoutSeconds) {
        var remaining = TimeSpan.FromSeconds(timeoutSeconds);
        var maximumDelay = TimeSpan.FromDays(30);
        while (remaining > TimeSpan.Zero) {
            var delay = remaining < maximumDelay ? remaining : maximumDelay;
            await Task.Delay(delay);
            remaining -= delay;
        }
    }

    internal static string SanitizeText(string? value, int maximumLength) {
        if (string.IsNullOrEmpty(value)) {
            return "-";
        }

        string normalized = value.Replace('\r', '_').Replace('\n', '_').Replace('\t', '_');
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    static ProcessStartInfo ComposerStartInfo(bool redirectOutput) {
        ProcessStartInfo startInfo;
        string composerProject;
        string composerVendorDirectory;
        if (OperatingSystem.IsWindows()) {
            startInfo = new ProcessStartInfo("php.exe");
            startInfo.ArgumentList.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ComposerSetup", "bin", "composer.phar"));
            composerProject = @"C:\ci-tools\composer.json";
            composerVendorDirectory = @"C:\ci-tools\vendor";
        } else {
            startInfo = new ProcessStartInfo("composer");
            composerProject = "/ci-tools/composer.json";
            composerVendorDirectory = "/ci-tools/vendor";
        }

        startInfo.Environment["COMPOSER"] = composerProject;
        startInfo.Environment["COMPOSER_VENDOR_DIR"] = composerVendorDirectory;
        startInfo.WorkingDirectory = Environment.CurrentDirectory;
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = redirectOutput;
        startInfo.RedirectStandardError = redirectOutput;
        startInfo.CreateNoWindow = redirectOutput;
        return startInfo;
    }

    internal static ProcessStartInfo ComposerStartInfo(
        string[] args,
        bool redirectOutput = false,
        RuntimeCredentials? credentials = null) {
        var startInfo = ComposerStartInfo(redirectOutput);
        credentials?.ApplyTo(startInfo);
        foreach (string argument in args) {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}

static class CommandRouter {
    static readonly string[] commands = ["steam-buildfile", "steam-login"];

    internal static string[] Route(string executable, string[] arguments) {
        string? command = commands.SingleOrDefault(candidate => candidate.Equals(executable, StringComparison.OrdinalIgnoreCase));
        return command is null
            ? throw new ArgumentException($"Unsupported CI tool launcher name: {Program.SanitizeText(executable, 80)}")
            : ["exec", command, "--", .. arguments];
    }
}

sealed class ChildProcess : IDisposable {
    readonly IDisposable? nativeResource;
    readonly Action resume;
    bool resumed;

    internal ChildProcess(Process process, int processGroupId, string? jobName, Action resume, IDisposable? nativeResource) {
        this.process = process;
        this.processGroupId = processGroupId;
        this.jobName = jobName;
        this.resume = resume;
        this.nativeResource = nativeResource;
    }

    internal Process process { get; }
    internal int processGroupId { get; }
    internal string? jobName { get; }

    public void Dispose() {
        process.Dispose();
        nativeResource?.Dispose();
    }

    internal void Resume() {
        if (!resumed) {
            resume();
            resumed = true;
        }
    }

    internal Task TerminateAsync(TimeSpan gracePeriod) => ProcessTree.TerminateChildAsync(this, gracePeriod);
}

static class ProcessTree {
    internal static ChildProcess StartComposer(string[] args, string invocationId, RuntimeCredentials credentials) {
        return OperatingSystem.IsWindows()
            ? WindowsProcessTree.Start(Program.ComposerStartInfo(args, credentials: credentials), invocationId)
            : UnixProcessTree.Start(Program.ComposerStartInfo(args, credentials: credentials));
    }

    internal static Task TerminateChildAsync(ChildProcess child, TimeSpan gracePeriod) => OperatingSystem.IsWindows()
        ? WindowsProcessTree.TerminateAsync(child.process, child.jobName, gracePeriod)
        : UnixProcessTree.TerminateAsync(child.process, child.processGroupId, gracePeriod);
}

static class UnixProcessTree {
    const int SIG_TERM = 15;
    const int SIG_KILL = 9;

    [DllImport("libc", SetLastError = true)]
    static extern int kill(int pid, int signal);

    internal static ChildProcess Start(ProcessStartInfo composer) {
        var startInfo = new ProcessStartInfo("/usr/bin/setsid") { UseShellExecute = false, WorkingDirectory = composer.WorkingDirectory };
        startInfo.Environment.Clear();
        foreach (var variable in composer.Environment) {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        startInfo.ArgumentList.Add("--wait");
        startInfo.ArgumentList.Add(composer.FileName);
        foreach (string argument in composer.ArgumentList) {
            startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Composer process group.");
        return new ChildProcess(process, process.Id, null, () => { }, null);
    }

    internal static async Task TerminateAsync(Process? process, int processGroupId, TimeSpan gracePeriod) {
        if (processGroupId <= 0) {
            return;
        }

        kill(-processGroupId, SIG_TERM);
        if (process is not null && await WaitForExitAsync(process, gracePeriod)) {
            return;
        }

        await Task.Delay(gracePeriod);
        kill(-processGroupId, SIG_KILL);
        if (process is not null) {
            await WaitForExitAsync(process, TimeSpan.FromSeconds(2));
        }
    }

    static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout) {
        if (process.HasExited) {
            return true;
        }

        var wait = process.WaitForExitAsync();
        return await Task.WhenAny(wait, Task.Delay(timeout)) == wait;
    }
}

static class WindowsProcessTree {
    const uint CREATE_SUSPENDED = 0x00000004;
    const uint CREATE_NEW_PROCESS_GROUP = 0x00000200;
    const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    const uint STARTF_USE_STD_HANDLES = 0x00000100;
    const uint JOB_OBJECT_EXTENDED_LIMIT_INFORMATION_CLASS = 9;
    const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    const uint JOB_OBJECT_TERMINATE = 0x0008;
    const uint JOB_OBJECT_QUERY = 0x0004;
    const uint CTRL_BREAK_EVENT = 1;
    const int STD_INPUT_HANDLE = -10;
    const int STD_OUTPUT_HANDLE = -11;
    const int STD_ERROR_HANDLE = -12;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateJobObject(IntPtr attributes, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetInformationJobObject(IntPtr job, uint informationClass, IntPtr information, uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcess(
        string? applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GenerateConsoleCtrlEvent(uint controlEvent, uint processGroupId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool TerminateJobObject(IntPtr job, uint exitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr OpenJobObject(uint desiredAccess, bool inheritHandle, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetStdHandle(int standardHandle);

    internal static ChildProcess Start(ProcessStartInfo startInfo, string invocationId) {
        string jobName = "ci-tools-" + invocationId;
        IntPtr job = CreateJobObject(IntPtr.Zero, jobName);
        if (job == IntPtr.Zero) {
            throw NativeError("Failed to create Windows Job Object");
        }

        try {
            ConfigureKillOnClose(job);
            var startupInfo = new StartupInfo {
                size = Marshal.SizeOf<StartupInfo>(),
                flags = STARTF_USE_STD_HANDLES,
                standardInput = GetStdHandle(STD_INPUT_HANDLE),
                standardOutput = GetStdHandle(STD_OUTPUT_HANDLE),
                standardError = GetStdHandle(STD_ERROR_HANDLE)
            };
            var commandLine = new StringBuilder(BuildCommandLine(startInfo));
            IntPtr environment = Marshal.StringToHGlobalUni(BuildEnvironmentBlock(startInfo));
            ProcessInformation processInformation;
            try {
                if (!CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero, true,
                        CREATE_SUSPENDED | CREATE_NEW_PROCESS_GROUP | CREATE_UNICODE_ENVIRONMENT,
                        environment, startInfo.WorkingDirectory, ref startupInfo, out processInformation)) {
                    throw NativeError("Failed to create suspended Composer process");
                }
            } finally {
                Marshal.FreeHGlobal(environment);
            }

            if (!AssignProcessToJobObject(job, processInformation.process)) {
                CloseHandle(processInformation.thread);
                CloseHandle(processInformation.process);
                throw NativeError("Failed to assign Composer to Windows Job Object");
            }

            var process = Process.GetProcessById((int)processInformation.processId);
            CloseHandle(processInformation.process);
            var jobHandle = new NativeHandle(job);
            job = IntPtr.Zero;
            return new ChildProcess(process, 0, jobName, () => {
                if (ResumeThread(processInformation.thread) == uint.MaxValue) {
                    throw NativeError("Failed to resume Composer process");
                }

                CloseHandle(processInformation.thread);
            }, jobHandle);
        } finally {
            if (job != IntPtr.Zero) {
                CloseHandle(job);
            }
        }
    }

    internal static async Task TerminateAsync(Process? process, string? jobName, TimeSpan gracePeriod) {
        if (string.IsNullOrWhiteSpace(jobName)) {
            return;
        }

        IntPtr job = OpenJobObject(JOB_OBJECT_TERMINATE | JOB_OBJECT_QUERY, false, jobName);
        if (job == IntPtr.Zero) {
            return;
        }

        try {
            if (process is not null && !process.HasExited) {
                GenerateConsoleCtrlEvent(CTRL_BREAK_EVENT, (uint)process.Id);
                if (await WaitForExitAsync(process, gracePeriod)) {
                    return;
                }
            }

            TerminateJobObject(job, 124);
            if (process is not null) {
                await WaitForExitAsync(process, TimeSpan.FromSeconds(2));
            }
        } finally {
            CloseHandle(job);
        }
    }

    static void ConfigureKillOnClose(IntPtr job) {
        var information = new JobObjectExtendedLimitInformation { basicLimitInformation = new JobObjectBasicLimitInformation { limitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE } };
        int size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        IntPtr pointer = Marshal.AllocHGlobal(size);
        try {
            Marshal.StructureToPtr(information, pointer, false);
            if (!SetInformationJobObject(job, JOB_OBJECT_EXTENDED_LIMIT_INFORMATION_CLASS, pointer, (uint)size)) {
                throw NativeError("Failed to configure Windows Job Object");
            }
        } finally {
            Marshal.FreeHGlobal(pointer);
        }
    }

    static string BuildCommandLine(ProcessStartInfo startInfo) {
        var values = new List<string> { startInfo.FileName };
        values.AddRange(startInfo.ArgumentList);
        return string.Join(" ", values.Select(QuoteArgument));
    }

    static string BuildEnvironmentBlock(ProcessStartInfo startInfo) =>
        string.Join('\0', startInfo.Environment
            .OrderBy(variable => variable.Key, StringComparer.OrdinalIgnoreCase)
            .Select(variable => $"{variable.Key}={variable.Value}")) + "\0\0";

    static string QuoteArgument(string argument) {
        if (argument.Length > 0 && !argument.Any(character => char.IsWhiteSpace(character) || character == '"')) {
            return argument;
        }

        var result = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char character in argument) {
            if (character == '\\') {
                backslashes++;
            } else if (character == '"') {
                result.Append('\\', (backslashes * 2) + 1).Append('"');
                backslashes = 0;
            } else {
                result.Append('\\', backslashes).Append(character);
                backslashes = 0;
            }
        }

        result.Append('\\', backslashes * 2).Append('"');
        return result.ToString();
    }

    static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout) {
        if (process.HasExited) {
            return true;
        }

        var wait = process.WaitForExitAsync();
        return await Task.WhenAny(wait, Task.Delay(timeout)) == wait;
    }

    static Exception NativeError(string message) => new InvalidOperationException($"{message}: {Marshal.GetLastWin32Error()}");

    sealed class NativeHandle(IntPtr handle) : IDisposable {
        public void Dispose() => CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo {
        public int size;
        public string? reserved;
        public string? desktop;
        public string? title;
        public uint x;
        public uint y;
        public uint xSize;
        public uint ySize;
        public uint xCountChars;
        public uint yCountChars;
        public uint fillAttribute;
        public uint flags;
        public ushort showWindow;
        public ushort reserved2;
        public IntPtr reserved2Pointer;
        public IntPtr standardInput;
        public IntPtr standardOutput;
        public IntPtr standardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInformation {
        public IntPtr process;
        public IntPtr thread;
        public uint processId;
        public uint threadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JobObjectBasicLimitInformation {
        public long perProcessUserTimeLimit;
        public long perJobUserTimeLimit;
        public uint limitFlags;
        public UIntPtr minimumWorkingSetSize;
        public UIntPtr maximumWorkingSetSize;
        public uint activeProcessLimit;
        public UIntPtr affinity;
        public uint priorityClass;
        public uint schedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IoCounters {
        public ulong readOperationCount;
        public ulong writeOperationCount;
        public ulong otherOperationCount;
        public ulong readTransferCount;
        public ulong writeTransferCount;
        public ulong otherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JobObjectExtendedLimitInformation {
        public JobObjectBasicLimitInformation basicLimitInformation;
        public IoCounters ioInfo;
        public UIntPtr processMemoryLimit;
        public UIntPtr jobMemoryLimit;
        public UIntPtr peakProcessMemoryUsed;
        public UIntPtr peakJobMemoryUsed;
    }
}

static class SignalHandlers {
    internal static IDisposable Register(CancellationTokenSource cancellation) {
        ConsoleCancelEventHandler consoleHandler = (_, eventArgs) => {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += consoleHandler;

        PosixSignalRegistration? terminate = null;
        PosixSignalRegistration? interrupt = null;
        if (!OperatingSystem.IsWindows()) {
            terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => {
                context.Cancel = true;
                cancellation.Cancel();
            });
            interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context => {
                context.Cancel = true;
                cancellation.Cancel();
            });
        }

        return new Registration(() => {
            Console.CancelKeyPress -= consoleHandler;
            terminate?.Dispose();
            interrupt?.Dispose();
        });
    }

    sealed class Registration(Action dispose) : IDisposable {
        public void Dispose() => dispose();
    }
}
