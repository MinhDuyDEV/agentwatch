using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentWatch;

// Runs a program inside a WSL distribution (wsl.exe -d <distro> -e …).
public interface IWsl
{
    Task<string> RunAsync(string? distro, string? input, params string[] command);
}

// A WSL command that failed, with what the member needs to see: its exit code
// and first error line (never the command's input).
public sealed class WslCommandException(string detail) : InvalidOperationException("wsl-command-failed")
{
    public string Detail { get; } = detail;
}

public sealed class WslExe : IWsl
{
    public async Task<string> RunAsync(string? distro, string? input, params string[] command)
    {
        var start = new ProcessStartInfo("wsl.exe") { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input is not null,
            UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false) };
        if (distro is not null) { start.ArgumentList.Add("-d"); start.ArgumentList.Add(distro); }
        start.ArgumentList.Add("-e");
        foreach (var part in command) start.ArgumentList.Add(part);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("wsl-unavailable");
        // Drain both pipes before writing stdin: WSL can fill stderr while
        // starting a distribution, otherwise neither process can make progress.
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        string output, errors;
        try
        {
            if (input is not null)
            {
                await process.StandardInput.BaseStream.WriteAsync(new UTF8Encoding(false).GetBytes(input), timeout.Token);
                process.StandardInput.Close();
            }
            await process.WaitForExitAsync(timeout.Token);
            output = await outputTask;
            errors = await errorTask;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            try { await Task.WhenAll(outputTask, errorTask); } catch (OperationCanceledException) { }
            throw new InvalidOperationException("wsl-timeout");
        }
        if (process.ExitCode != 0)
        {
            var error = errors.Replace("\0", "").Trim();
            if (error.Contains("no-runtime", StringComparison.Ordinal)) throw new InvalidOperationException("wsl-runtime-unreadable");
            var first = error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
            throw new WslCommandException($"exit {process.ExitCode}{(first.Length > 0 ? ": " + first[..Math.Min(first.Length, 200)] : "")}");
        }
        return output;
    }
}

// Company conversations on Windows run in WSL2: Piagent there starts this
// app's broker (a Windows program, through WSL's interop) and checks the
// binding Agent Watch writes into the distribution, as on macOS
// (StudioManagedConfiguration.swift): the Piagent launcher, the Node running
// it and the broker, each pinned by its SHA-256.
public static class PiagentWslBinding
{
    public sealed record Result(string Distro, string File, string Entrypoint, string Node, string Broker);
    // What a binding would pin, read inside WSL without writing anything.
    public sealed record Runtime(string Distro, string User, string Home, string Entrypoint, string Node, string SdkRoot, string EntrypointSha256, string NodeSha256, string PiVersion);

    // The script runs in WSL with the runtime Piagent recorded
    // (~/.pi/agent/piagent-runtime.json, written by every `piagent` command)
    // and prints the resolved paths and digests.
    const string Inspect = """
        set -eu
        file="$HOME/.pi/agent/piagent-runtime.json"
        [ -f "$file" ] || { echo "no-runtime" >&2; exit 3; }
        cat "$file"
        echo
        echo "--home--"
        printf '%s\n' "$HOME"
        echo "--user--"
        id -un
        """;

    public static async Task<Runtime> InspectAsync(IWsl wsl, string? distro = null)
    {
        distro ??= (await wsl.RunAsync(null, null, "sh", "-c", "printf %s \"$WSL_DISTRO_NAME\"")).Trim();
        if (distro.Length == 0) throw new InvalidOperationException("wsl-distro-unknown");
        // LF only: a Windows checkout gives this source CRLF, and sh then
        // reads "set -eu\r" ("set: Illegal option -", the member's bind failure).
        var inspected = await wsl.RunAsync(distro, null, "sh", "-c", Inspect.ReplaceLineEndings("\n"));
        var parts = inspected.Split("--home--", 2);
        if (parts.Length != 2) throw new InvalidOperationException("wsl-runtime-unreadable");
        var tail = parts[1].Split("--user--", 2);
        var (home, user) = (tail[0].Trim(), tail.Length == 2 ? tail[1].Trim() : "");
        var runtime = JsonNode.Parse(parts[0]) as JsonObject;
        string Path(string name) => runtime?[name]?.GetValue<string>() is { } value && value.StartsWith('/') ? value : throw new InvalidOperationException("wsl-runtime-invalid");
        if (runtime?["schema_version"]?.GetValue<int>() != 1) throw new InvalidOperationException("wsl-runtime-invalid");
        var (entrypoint, node, sdk) = (Path("entrypoint"), Path("node"), Path("pi_sdk_root"));
        // Real paths, digests and the Pi host's version, read inside WSL.
        var facts = (await wsl.RunAsync(distro, null, "sh", "-c",
            "set -eu; e=$(realpath \"$1\"); n=$(realpath \"$2\"); s=$(realpath \"$3\"); printf '%s\\n%s\\n%s\\n' \"$e\" \"$n\" \"$s\"; sha256sum \"$e\" \"$n\" | cut -d ' ' -f 1; \"$n\" -p 'require(process.argv[1]).version' \"$s/package.json\"",
            "piagent-binding", entrypoint, node, sdk)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (facts.Length != 6 || !facts[0].EndsWith("/scripts/piagent-studio.mjs", StringComparison.Ordinal) || facts[5] != "0.87.1"
            || facts[3].Length != 64 || facts[4].Length != 64) throw new InvalidOperationException("wsl-runtime-unsupported");
        return new Runtime(distro, user, home, facts[0], facts[1], facts[2], facts[3], facts[4], facts[5]);
    }

    public static async Task<Result> BindAsync(StudioProfile profile, StudioManifest manifest, string brokerWindowsPath, IWsl wsl, string? distro = null)
    {
        if (profile.CredentialMode != StudioCredentialMode.managed || manifest.Harness is null) throw new StudioException(StudioError.permissionDenied);
        manifest.Validate(profile);
        var runtime = await InspectAsync(wsl, distro);
        distro = runtime.Distro;
        var broker = (await wsl.RunAsync(distro, null, "wslpath", "-u", brokerWindowsPath)).Trim();
        if (!broker.StartsWith("/mnt/", StringComparison.Ordinal)) throw new InvalidOperationException("wsl-broker-path");
        var binding = new JsonObject
        {
            ["schema_version"] = 1, ["model"] = "agent-watch-auto", ["profile_id"] = profile.Id, ["origin"] = profile.Origin,
            ["node"] = runtime.Node, ["entrypoint"] = runtime.Entrypoint, ["node_sha256"] = runtime.NodeSha256, ["entrypoint_sha256"] = runtime.EntrypointSha256,
            ["sdk_root"] = runtime.SdkRoot, ["broker"] = broker, ["broker_sha256"] = Sha256(brokerWindowsPath),
            ["configuration_revision"] = manifest.Revision,
        };
        await wsl.RunAsync(distro, binding.ToJsonString(new() { WriteIndented = true }) + "\n", "sh", "-c",
            "set -eu; umask 077; d=\"$HOME/.pi/agent\"; mkdir -p \"$d\"; cat > \"$d/agent-watch-managed.json.tmp\"; mv \"$d/agent-watch-managed.json.tmp\" \"$d/agent-watch-managed.json\"");
        return new Result(distro, runtime.Home + "/.pi/agent/agent-watch-managed.json", runtime.Entrypoint, runtime.Node, broker);
    }

    // What a stale-binding check saw: whether a Piagent process runs in the
    // distribution (check again in seconds) and, when a manual connect would
    // now write a different binding, a fingerprint of what is installed.
    public sealed record Probe(bool Active, string? Stale);

    // One cheap read for the background check, writing nothing: the launcher
    // and Node the runtime file names (resolved, with their SHA-256) next to
    // the binding. No binding is never stale. The pgrep pattern is bracketed
    // and the runtime file's name split so this script never matches itself.
    const string ProbeScript = """
        set -u
        d="$HOME/.pi/agent"
        [ -f "$d/agent-watch-managed.json" ] || { echo "@@none@@"; exit 0; }
        pgrep -u "$(id -u)" -f '[p]iagent' >/dev/null 2>&1 && echo "@@active@@"
        p=pi; r="$d/${p}agent-runtime.json"
        field() { sed -n "s/.*\"$1\"[[:space:]]*:[[:space:]]*\"\([^\"]*\)\".*/\1/p" "$r" 2>/dev/null | head -n 1; }
        e=$(realpath "$(field entrypoint)" 2>/dev/null) || e=
        n=$(realpath "$(field node)" 2>/dev/null) || n=
        echo "@@current@@"; printf '%s\n%s\n' "$e" "$n"
        [ -n "$e" ] && [ -n "$n" ] && sha256sum "$e" "$n" 2>/dev/null | cut -d ' ' -f 1
        echo "@@binding@@"; cat "$d/agent-watch-managed.json"
        echo; echo "@@end@@"
        """;

    // Only a trigger: a stale result leads to the full BindAsync, which runs
    // every check a manual connect runs, so this decides nothing it pins.
    public static async Task<Probe> ProbeAsync(IWsl wsl, string distro, string profileId)
    {
        var output = await wsl.RunAsync(distro, null, "sh", "-c", ProbeScript.ReplaceLineEndings("\n"));
        if (output.Contains("@@none@@", StringComparison.Ordinal)) return new(false, null);
        var active = output.Contains("@@active@@", StringComparison.Ordinal);
        string Section(string name)
        {
            var start = output.IndexOf($"@@{name}@@", StringComparison.Ordinal);
            if (start < 0) return "";
            start += name.Length + 4;
            var end = output.IndexOf("@@", start, StringComparison.Ordinal);
            return output[start..(end < 0 ? output.Length : end)].Trim();
        }
        if (!output.Contains("@@end@@", StringComparison.Ordinal)) return new(active, "unreadable");
        JsonObject? binding;
        try { binding = JsonNode.Parse(Section("binding")) as JsonObject; }
        catch (JsonException) { binding = null; }
        string? Text(string name) => binding?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        if (binding is null) return new(active, "unreadable");
        if (Text("profile_id") != profileId) return new(active, "profile");
        var current = Section("current").Split('\n', StringSplitOptions.TrimEntries);
        if (current.Length != 4 || current.Any(part => part.Length == 0) || current[2].Length != 64 || current[3].Length != 64) return new(active, "unresolved");
        var (entrypoint, node, entrypointSha256, nodeSha256) = (current[0], current[1], current[2], current[3]);
        if (Text("entrypoint") == entrypoint && Text("node") == node && Text("entrypoint_sha256") == entrypointSha256 && Text("node_sha256") == nodeSha256)
            return new(active, null);
        return new(active, string.Join(':', entrypoint, node, entrypointSha256, nodeSha256));
    }

    // The distribution last bound, kept so a later start of the app (after an
    // update changed agentwatch.exe) writes the binding again.
    public static string RememberedFile => Path.Combine(AgentWatchPaths.DataDirectory, "wsl.json");

    public static void Remember(string distro)
    {
        Directory.CreateDirectory(AgentWatchPaths.DataDirectory);
        var temporary = RememberedFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { distro }));
            File.Move(temporary, RememberedFile, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static string? Remembered()
    {
        try { using var document = JsonDocument.Parse(File.ReadAllText(RememberedFile)); return document.RootElement.GetProperty("distro").GetString(); }
        catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException or InvalidOperationException or UnauthorizedAccessException) { return null; }
    }

    static string Sha256(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
