using System.Diagnostics;
using System.Text;

namespace AgentWatch;

// After a Piagent update inside WSL the binding still pins the old launcher by
// hash, and `piagent studio` refuses to start (managed-launch-binding-changed)
// until it is written again; Piagent waits up to 90 s for that. The app checks
// every few seconds and rebinds at once, with the same checks as a manual
// connect, like the macOS app (StudioSyncStore): a new stale state at once,
// the same one a rebind did not fix (Studio offline) once a minute.
public sealed class PiagentBindingWatch
{
    // While a Piagent process runs: seconds matter. Otherwise slower, so the
    // check alone never keeps an idle distribution running (WSL stops one a
    // few seconds after its last process).
    public static readonly TimeSpan ActiveInterval = TimeSpan.FromSeconds(5), IdleInterval = TimeSpan.FromSeconds(30), RetryInterval = TimeSpan.FromMinutes(1);

    string? lastStale;
    DateTimeOffset lastRebind = DateTimeOffset.MinValue;

    public bool ShouldRebind(string? stale, DateTimeOffset now)
    {
        if (stale is null) return false;
        if (stale == lastStale && now - lastRebind < RetryInterval) return false;
        (lastStale, lastRebind) = (stale, now);
        return true;
    }

    public static TimeSpan Next(PiagentWslBinding.Probe? probe) => probe?.Active == true ? ActiveInterval : IdleInterval;

    // The distributions running now (wsl.exe -l --running -q), read without
    // starting one; null when WSL did not answer, so nothing is checked.
    public static async Task<IReadOnlyList<string>?> RunningDistributionsAsync()
    {
        try
        {
            var start = new ProcessStartInfo("wsl.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var part in new[] { "-l", "--running", "-q" }) start.ArgumentList.Add(part);
            start.Environment["WSL_UTF8"] = "1";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var process = Process.Start(start);
            if (process is null) return null;
            using var output = new MemoryStream();
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await errors;
            return process.ExitCode == 0 ? ParseDistributions(output.ToArray()) : null;
        }
        catch (Exception error) when (error is OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException) { return null; }
    }

    // wsl.exe writes UTF-16 unless WSL_UTF8 is honoured; names are one per line.
    public static IReadOnlyList<string> ParseDistributions(byte[] output)
    {
        var text = output.Length >= 2 && (output[0] == 0xFF || Array.IndexOf(output, (byte)0) >= 0) ? Encoding.Unicode.GetString(output) : Encoding.UTF8.GetString(output);
        return text.Replace("﻿", "").Replace("\0", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
