using System.Diagnostics;

namespace SqlAccess.Api.Cicd.Services;

public interface IBuildService
{
    /// <summary>Runs a shell command in workingDir, streaming each output line to log. Returns the exit code.</summary>
    Task<int> RunAsync(string workingDir, string command, Func<string, string, Task> log, CancellationToken ct);
}

public sealed class BuildService : IBuildService
{
    public async Task<int> RunAsync(string workingDir, string command, Func<string, string, Task> log, CancellationToken ct)
    {
        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "/bin/bash",
            Arguments = isWindows ? $"/c {command}" : $"-lc \"{command.Replace("\"", "\\\"")}\"",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var pending = new List<Task>();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) pending.Add(log("Info", e.Data)); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) pending.Add(log("Warning", e.Data)); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync(ct);
        try { await Task.WhenAll(pending); } catch { /* individual log failures are non-fatal */ }

        return proc.ExitCode;
    }
}
