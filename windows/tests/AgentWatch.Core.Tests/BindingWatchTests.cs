using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AgentWatch;
using Xunit;

namespace AgentWatch.Tests;

public class BindingWatchTests : IDisposable
{
    readonly string home = Directory.CreateTempSubdirectory("agentwatch-probe-").FullName;
    public void Dispose() => Directory.Delete(home, true);

    // The probe script run by a real sh with HOME pointed at a temporary
    // folder, as WSL would run it (Linux and macOS only; CI on Windows skips).
    sealed class LocalShell(string home) : IWsl
    {
        public readonly List<string[]> Commands = [];
        public async Task<string> RunAsync(string? distro, string? input, params string[] command)
        {
            Commands.Add(command);
            var start = new ProcessStartInfo(command[0]) { RedirectStandardOutput = true, UseShellExecute = false };
            foreach (var part in command[1..]) start.ArgumentList.Add(part);
            start.Environment["HOME"] = home;
            using var process = Process.Start(start)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output;
        }
    }

    static bool HasShell => !OperatingSystem.IsWindows() && File.Exists("/bin/sh");
    static string Sha256(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();

    string Agent => Path.Combine(home, ".pi", "agent");
    (string Entrypoint, string Node) Install(string launcher)
    {
        var scripts = Directory.CreateDirectory(Path.Combine(home, "lib", "node_modules", "@piagent", "platform", "scripts")).FullName;
        var entrypoint = Path.Combine(scripts, "piagent-studio.mjs");
        var node = Path.Combine(home, "node");
        File.WriteAllText(entrypoint, launcher);
        File.WriteAllText(node, "node");
        Directory.CreateDirectory(Agent);
        // Compact, as Piagent writes it; the paths resolve through "..".
        File.WriteAllText(Path.Combine(Agent, "piagent-runtime.json"),
            new JsonObject { ["schema_version"] = 1, ["entrypoint"] = scripts + "/../scripts/piagent-studio.mjs", ["node"] = node, ["pi_sdk_root"] = home }.ToJsonString());
        return (entrypoint, node);
    }

    void Bind(string profile, string entrypoint, string node, string? entrypointSha256 = null) =>
        File.WriteAllText(Path.Combine(Agent, "agent-watch-managed.json"), new JsonObject
        {
            ["schema_version"] = 1, ["profile_id"] = profile, ["entrypoint"] = entrypoint, ["node"] = node,
            ["entrypoint_sha256"] = entrypointSha256 ?? Sha256(entrypoint), ["node_sha256"] = Sha256(node),
        }.ToJsonString(new() { WriteIndented = true }));

    [Fact]
    public async Task AFreshBindingIsNotStaleAndAnUpdatedLauncherIs()
    {
        if (!HasShell) return;
        var wsl = new LocalShell(home);
        Assert.Equal(new PiagentWslBinding.Probe(false, null), await PiagentWslBinding.ProbeAsync(wsl, "Ubuntu", "p1"));

        var (entrypoint, node) = Install("launcher 1.28.1");
        // No binding yet (never connected here): nothing to rebind.
        Assert.Null((await PiagentWslBinding.ProbeAsync(wsl, "Ubuntu", "p1")).Stale);
        // Bound as BindAsync pins them: the real paths (/private/var on macOS).
        var (resolved, resolvedNode) = Resolved(await new LocalShell(home).RunAsync(null, null, "sh", "-c", "realpath \"$1\" \"$2\"", "x", entrypoint, node));
        Bind("p1", resolved, resolvedNode);
        Assert.Null((await PiagentWslBinding.ProbeAsync(wsl, "Ubuntu", "p1")).Stale);

        File.WriteAllText(entrypoint, "launcher 1.28.2");
        var updated = await PiagentWslBinding.ProbeAsync(wsl, "Ubuntu", "p1");
        Assert.NotNull(updated.Stale);
        Assert.Contains(Sha256(entrypoint), updated.Stale);
        Assert.Equal("profile", (await PiagentWslBinding.ProbeAsync(wsl, "Ubuntu", "p2")).Stale);

        // The script writes nothing and never names the package it looks for,
        // so its own command line is not taken for a running Piagent.
        Assert.DoesNotContain(wsl.Commands, command => command.Any(part => part.Contains("piagent", StringComparison.Ordinal)));
        Assert.DoesNotContain(wsl.Commands, command => command.Any(part => part.Contains('\r')));
    }

    static (string, string) Resolved(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return (lines[0], lines[1]);
    }

    // What a WSL answer means, without a shell.
    sealed class Answer(string output) : IWsl
    {
        public Task<string> RunAsync(string? distro, string? input, params string[] command) => Task.FromResult(output);
    }

    static string Output(JsonObject? binding, string current, bool active = false) =>
        (active ? "@@active@@\n" : "") + "@@current@@\n" + current + "\n@@binding@@\n" + (binding?.ToJsonString() ?? "{oops") + "\n\n@@end@@\n";

    [Fact]
    public async Task AnythingUnreadableCountsAsStaleSoTheFullConnectDecides()
    {
        var (e, n) = (new string('e', 64), new string('f', 64));
        var binding = new JsonObject { ["profile_id"] = "p1", ["entrypoint"] = "/a/piagent-studio.mjs", ["node"] = "/usr/bin/node", ["entrypoint_sha256"] = e, ["node_sha256"] = n };
        var current = $"/a/piagent-studio.mjs\n/usr/bin/node\n{e}\n{n}";
        Assert.Equal(new PiagentWslBinding.Probe(true, null), await PiagentWslBinding.ProbeAsync(new Answer(Output(binding, current, active: true)), "Ubuntu", "p1"));
        Assert.Equal("unreadable", (await PiagentWslBinding.ProbeAsync(new Answer(Output(null, current)), "Ubuntu", "p1")).Stale);
        Assert.Equal("unreadable", (await PiagentWslBinding.ProbeAsync(new Answer("@@current@@\n"), "Ubuntu", "p1")).Stale);
        // Piagent removed or its runtime file gone: nothing to hash.
        Assert.Equal("unresolved", (await PiagentWslBinding.ProbeAsync(new Answer(Output(binding, "\n")), "Ubuntu", "p1")).Stale);
        // Node moved (another Node install) with the same launcher.
        Assert.NotNull((await PiagentWslBinding.ProbeAsync(new Answer(Output(binding, $"/a/piagent-studio.mjs\n/opt/node/bin/node\n{e}\n{n}")), "Ubuntu", "p1")).Stale);
    }

    [Fact]
    public void ANewStaleStateRebindsAtOnceTheSameOneOnceAMinute()
    {
        var watch = new PiagentBindingWatch();
        var now = DateTimeOffset.Parse("2026-10-18T10:00:00+07:00");
        Assert.False(watch.ShouldRebind(null, now));
        Assert.True(watch.ShouldRebind("a", now));
        Assert.False(watch.ShouldRebind("a", now.AddSeconds(5)));
        // Another update while Studio was offline: at once.
        Assert.True(watch.ShouldRebind("b", now.AddSeconds(10)));
        Assert.False(watch.ShouldRebind("b", now.AddSeconds(69)));
        Assert.True(watch.ShouldRebind("b", now.AddSeconds(70)));
        Assert.Equal(PiagentBindingWatch.ActiveInterval, PiagentBindingWatch.Next(new(true, null)));
        Assert.Equal(PiagentBindingWatch.IdleInterval, PiagentBindingWatch.Next(new(false, null)));
        Assert.Equal(PiagentBindingWatch.IdleInterval, PiagentBindingWatch.Next(null));
    }

    [Fact]
    public void ReadsRunningDistributionsInEitherEncoding()
    {
        Assert.Equal(["Ubuntu", "Debian"], PiagentBindingWatch.ParseDistributions(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("Ubuntu\r\nDebian\r\n")).ToArray()));
        Assert.Equal(["Ubuntu"], PiagentBindingWatch.ParseDistributions(Encoding.Unicode.GetBytes("Ubuntu\r\n")));
        Assert.Equal(["Ubuntu-24.04"], PiagentBindingWatch.ParseDistributions(Encoding.UTF8.GetBytes("Ubuntu-24.04\n")));
        Assert.Empty(PiagentBindingWatch.ParseDistributions([]));
    }
}
