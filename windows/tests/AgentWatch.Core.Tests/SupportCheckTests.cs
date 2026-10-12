using System.Security.Cryptography;
using System.Text.Json.Nodes;
using AgentWatch;
using Xunit;

namespace AgentWatch.Tests;

[Collection("data directory")]
public class SupportCheckTests : IDisposable
{
    const string Code = "https://studio.example.com#as_live_test-key_0123456789";
    readonly string directory = Directory.CreateTempSubdirectory("agentwatch-support-").FullName;
    public void Dispose() => Directory.Delete(directory, true);

    sealed class MemoryKeys : IStudioKeyStore
    {
        public readonly Dictionary<string, string> Values = [];
        public string? Load(string id) => Values.GetValueOrDefault(id);
        public void Save(string id, string key) => Values[id] = key;
        public void Delete(string id) => Values.Remove(id);
    }

    // Piagent in WSL as the two support scripts see it.
    sealed class Wsl(Func<JsonObject?> binding, string running = "1.22.0", string doctor = " ok   sandbox starts and runs a command — bash\nReady for company sessions. No model request was sent.") : IWsl
    {
        public readonly List<string[]> Commands = [];
        public Task<string> RunAsync(string? distro, string? input, params string[] command)
        {
            Commands.Add(command);
            var script = string.Join(' ', command);
            if (script.Contains("studio --doctor")) return Task.FromResult($"@@doctor@@\n{doctor}\n@@end@@\n");
            if (script.Contains("dashboard status")) return Task.FromResult(
                "@@version@@\n1.22.0\n@@binding@@\n" + binding()?.ToJsonString() + "\n@@status@@\n"
                + new JsonObject { ["state"] = "running", ["gateway"] = new JsonObject { ["origin"] = "http://127.0.0.1:41873", ["packageVersion"] = running } }.ToJsonString()
                + "\n@@doctor@@\n" + new JsonObject { ["ok"] = true, ["checks"] = new JsonObject { ["piHostVersion"] = true, ["gatewayHealth"] = true } }.ToJsonString()
                + "\n@@log@@\n@@end@@\n");
            return new FakeWsl().RunAsync(distro, input, command);
        }
    }

    async Task<(StudioProfileStore Profiles, MemoryKeys Keys, FakeStudio Studio, string Broker)> Connected()
    {
        var profiles = new StudioProfileStore(directory); var keys = new MemoryKeys(); var studio = new FakeStudio();
        await new StudioConnector(profiles, keys, new StudioClient(studio)).ConnectAsync(Code);
        var broker = Path.Combine(directory, "agentwatch.exe");
        File.WriteAllText(broker, "broker");
        return (profiles, keys, studio, broker);
    }

    static JsonObject Binding(string profile, string broker) => new()
    {
        ["profile_id"] = profile, ["entrypoint_sha256"] = new string('e', 64), ["node_sha256"] = new string('f', 64),
        ["broker_sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(broker))).ToLowerInvariant(),
    };

    [Fact]
    public async Task AHealthyMachinePassesEveryLegWithoutNamingModelsOrTheKey()
    {
        var (profiles, keys, studio, broker) = await Connected();
        var wsl = new Wsl(() => Binding(profiles.Active()!.Id, broker));
        var lines = await SupportCheck.RunAsync(profiles, keys, new StudioClient(studio), wsl, "Ubuntu", broker, _ => Task.FromResult(200));
        Assert.DoesNotContain(lines, line => line.State == SupportState.failed);
        Assert.Equal([SupportCheck.AgentWatch, SupportCheck.Studio, SupportCheck.Piagent], lines.Select(line => line.Area).Distinct());
        var report = SupportCheck.Format(lines, DateTimeOffset.UnixEpoch);
        Assert.StartsWith("Không thấy lỗi ở cả ba chặng.", report);
        Assert.Contains("1 model được cấp", report);
        Assert.DoesNotContain("Sol", report); Assert.DoesNotContain("gpt-6-sol", report); Assert.DoesNotContain("as_live", report);
        Assert.DoesNotContain(wsl.Commands, command => command.Any(part => part.Contains('\r')));
    }

    [Fact]
    public async Task NamesTheLegThatFailsAndStillChecksTheOthers()
    {
        var (profiles, keys, studio, broker) = await Connected();
        studio.Override = (_, _, _) => (401, "{}");
        var bound = Binding(profiles.Active()!.Id, broker);
        var wsl = new Wsl(() => bound, running: "1.21.0", doctor: " FAIL Agent Watch answers with the company key — managed-broker:invalidKey\nNot ready: 1 required check(s) failed.");
        File.WriteAllText(broker, "broker after an update");
        var lines = await SupportCheck.RunAsync(profiles, keys, new StudioClient(studio), wsl, null, broker, _ => throw new HttpRequestException("refused"));
        string Failed(string label) => Assert.Single(lines, line => line.Label == label && line.State == SupportState.failed).Detail;
        Assert.Contains("[invalidKey]", Failed("Studio trả lời và nhận key"));
        Assert.Contains("Agent Watch vừa cập nhật", Failed("Nối Piagent với key công ty"));
        Assert.Contains("1.21.0", Failed("Dashboard"));
        Assert.Contains("wsl --shutdown", Failed("Windows mở được trang dashboard"));
        Assert.Contains("managed-broker:invalidKey", Failed("Kiểm tra phiên công ty (piagent studio --doctor)"));
        Assert.DoesNotContain(lines, line => line.Area == SupportCheck.AgentWatch && line.State == SupportState.failed);
        Assert.StartsWith($"Có lỗi ở: {SupportCheck.Studio}, {SupportCheck.Piagent}.", SupportCheck.Format(lines, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public async Task WithoutAConnectionOrPiagentItSaysSoInsteadOfThrowing()
    {
        var lines = await SupportCheck.RunAsync(new StudioProfileStore(directory), new MemoryKeys(), new StudioClient(new FakeStudio()),
            new FailingWsl(), null, Path.Combine(directory, "missing.exe"));
        Assert.Contains(lines, line => line.Area == SupportCheck.AgentWatch && line.Label == "Kết nối đã lưu" && line.State == SupportState.failed);
        Assert.Contains(lines, line => line.Area == SupportCheck.Piagent && line.State == SupportState.failed && line.Detail.Contains("[wsl-runtime-unreadable]"));
    }

    sealed class BrokenKeys : IStudioKeyStore
    {
        public string? Load(string id) => throw new NotSupportedException("no key store here");
        public void Save(string id, string key) { }
        public void Delete(string id) { }
    }

    [Fact]
    public async Task AnUnforeseenFailureIsALineOfTheReportAndEachStepIsReportedAsItEnds()
    {
        var (profiles, _, studio, broker) = await Connected();
        var seen = new List<int>();
        var lines = await SupportCheck.RunAsync(profiles, new BrokenKeys(), new StudioClient(studio), new FailingWsl(), null, broker, progress: known => seen.Add(known.Count));
        Assert.Contains(lines, line => line.State == SupportState.failed && line.Detail.Contains("NotSupportedException"));
        Assert.Equal(Enumerable.Range(1, lines.Count), seen);
        Assert.StartsWith("Đang kiểm tra", SupportCheck.Format(lines, DateTimeOffset.UnixEpoch, done: false));
    }

    [Fact]
    public async Task TheLastUnhandledErrorIsKeptAndShownInTheReport()
    {
        var previous = Environment.GetEnvironmentVariable("AGENTWATCH_DATA_DIR");
        Environment.SetEnvironmentVariable("AGENTWATCH_DATA_DIR", directory);
        try
        {
            Assert.Null(CrashLog.Last());
            CrashLog.Write(new InvalidOperationException("layout broke"), DateTimeOffset.UnixEpoch);
            Assert.Contains("InvalidOperationException: layout broke", CrashLog.Last());
            var lines = await SupportCheck.RunAsync(new StudioProfileStore(directory), new MemoryKeys(), new StudioClient(new FakeStudio()), new FailingWsl(), null, "missing.exe");
            Assert.Contains(lines, line => line.Label == "Lỗi app gần nhất" && line.Detail.Contains("layout broke"));
            SupportCheck.Save(SupportCheck.Format(lines, DateTimeOffset.UnixEpoch));
            Assert.Contains("Lỗi app gần nhất", File.ReadAllText(SupportCheck.ReportFile));
        }
        finally { Environment.SetEnvironmentVariable("AGENTWATCH_DATA_DIR", previous); }
    }

    sealed class FailingWsl : IWsl
    {
        public Task<string> RunAsync(string? distro, string? input, params string[] command) => throw new InvalidOperationException("wsl-runtime-unreadable");
    }
}
