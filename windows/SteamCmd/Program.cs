using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace CiTools.SteamCmd;

static class Program {
    const string STEAM_DIRECTORY = @"C:\steam";
    const string INSTALLER_PATH = @"C:\steamcmd\steamcmd-installer.exe";
    static readonly string steamCmdPath = Path.Combine(STEAM_DIRECTORY, "steamcmd.exe");
    static readonly string lockPath = Path.Combine(STEAM_DIRECTORY, ".steamcmd.lock");

    public static int Main(string[] args) {
        try {
            Directory.CreateDirectory(STEAM_DIRECTORY);
            using var invocationLock = AcquireInvocationLock();
            bool installed = EnsureSteamCmdInstalled();

            ConsoleCancelEventHandler keepWrapperAlive = (_, eventArgs) => eventArgs.Cancel = true;
            Console.CancelKeyPress += keepWrapperAlive;
            try {
                if (installed) {
                    RunSteamCmd(["+quit"]);
                }

                return RunSteamCmd(args);
            } finally {
                Console.CancelKeyPress -= keepWrapperAlive;
            }
        } catch (Exception exception) {
            Console.Error.WriteLine($"steamcmd: {exception.Message}");
            return 1;
        }
    }

    static int RunSteamCmd(IEnumerable<string> args) {
        var startInfo = new ProcessStartInfo(steamCmdPath) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
        foreach (string argument in args) {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Failed to start the SteamCMD installation.");
        process.WaitForExit();
        int exitCode = process.ExitCode;
        WaitForSteamCmdChildren();
        return exitCode;
    }

    static void WaitForSteamCmdChildren() {
        int quietChecks = 0;
        while (quietChecks < 2) {
            bool childFound = false;
            foreach (var process in Process.GetProcessesByName("steamcmd")) {
                using (process) {
                    try {
                        if (process.Id != Environment.ProcessId
                            && !process.HasExited
                            && process.MainModule?.FileName.Equals(steamCmdPath, StringComparison.OrdinalIgnoreCase) == true) {
                            childFound = true;
                        }
                    } catch (InvalidOperationException) {
                    } catch (Win32Exception) {
                    }
                }
            }

            quietChecks = childFound ? 0 : quietChecks + 1;
            Thread.Sleep(250);
        }
    }

    static FileStream AcquireInvocationLock() {
        while (true) {
            try {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            } catch (IOException) {
                Thread.Sleep(250);
            }
        }
    }

    static bool EnsureSteamCmdInstalled() {
        if (IsExecutable(steamCmdPath)) {
            return false;
        }

        if (!IsExecutable(INSTALLER_PATH)) {
            throw new InvalidDataException($"SteamCMD installer is missing or invalid: {INSTALLER_PATH}");
        }

        if (File.Exists(steamCmdPath)) {
            File.Delete(steamCmdPath);
        }

        string temporaryPath = Path.Combine(STEAM_DIRECTORY, $".steamcmd-{Guid.NewGuid():N}.tmp");
        try {
            File.Copy(INSTALLER_PATH, temporaryPath);
            File.Move(temporaryPath, steamCmdPath);
            return true;
        } finally {
            File.Delete(temporaryPath);
        }
    }

    static bool IsExecutable(string path) {
        try {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return stream.Length >= 1024 && stream.ReadByte() == 'M' && stream.ReadByte() == 'Z';
        } catch (IOException) {
            return false;
        } catch (UnauthorizedAccessException) {
            return false;
        }
    }
}