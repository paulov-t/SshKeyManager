using System.Diagnostics;
using System.Text;

namespace SshKeyManager.Services;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
{
    public string Combined => string.Join(
        Environment.NewLine,
        new[] { StdOut, StdErr }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();

    public bool Success => ExitCode == 0 && !TimedOut;
}

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? standardInput = null,
        int timeoutMs = 30_000,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, string.Empty, ex.Message, false);
        }

        if (standardInput is not null)
        {
            try { await process.StandardInput.WriteAsync(standardInput); }
            catch { /* process may already have exited */ }
        }

        try { process.StandardInput.Close(); }
        catch { /* ignore */ }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return new ProcessResult(-1, await SafeReadAsync(stdout), await SafeReadAsync(stderr), true);
        }

        return new ProcessResult(process.ExitCode, await SafeReadAsync(stdout), await SafeReadAsync(stderr), false);
    }

    private static async Task<string> SafeReadAsync(Task<string> task)
    {
        try { return await task.ConfigureAwait(false); }
        catch { return string.Empty; }
    }
}
