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
    sealed class Wsl(Func<JsonObject?> binding, string running = "1.22.0", string samples = "16 S epoll_wait,", string doctor = " ok   sandbox starts and runs a command — bash\nReady for company sessions. No model request was sent.") : IWsl
    {
        public readonly List<string[]> Commands = [];
        public Task<string> RunAsync(string? distro, string? input, params string[] command)
        {
            Commands.Add(command);
            var script = string.Join(' ', command);
            if (script.Contains("@@process@@")) return Task.FromResult(
                "@@process@@piagent-dashboard.mjs|pid 927, chạy 94s, CPU 2.2%|16 S epoll_wait,||/home/dev|\n"
                + $"@@process@@piagent-studio.mjs|pid 950, chạy 92s, CPU 5.2%|{samples}|S 3 /init /mnt/c/agentwatch.exe managed-broker,|/home/dev/probe|1 /mnt/c/x.jsonl,\n@@end@@\n");
            if (script.Contains("dashboard open --no-open")) return Task.FromResult("@@launch@@\n" + new JsonObject { ["state"] = "running", ["launchUrl"] = "http://127.0.0.1:41873/#bootstrap=" + new string('c', 43) }.ToJsonString() + "\n@@end@@\n");
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
        var lines = await SupportCheck.RunAsync(profiles, keys, new StudioClient(studio), wsl, "Ubuntu", broker, _ => Task.FromResult(200),
            page: (origin, capability) => Task.FromResult<IReadOnlyList<DashboardProbe.Result>>([new("Trang đọc danh sách hội thoại", origin == "http://127.0.0.1:41873" && capability.Length == 43, "HTTP 200 · 0.1s · 3 hội thoại")]));
        Assert.DoesNotContain(lines, line => line.State == SupportState.failed);
        Assert.Equal([SupportCheck.AgentWatch, SupportCheck.Studio, SupportCheck.Piagent], lines.Select(line => line.Area).Distinct());
        var report = SupportCheck.Format(lines, DateTimeOffset.UnixEpoch);
        Assert.StartsWith("Không thấy lỗi ở cả ba chặng.", report);
        Assert.Contains("1 model được cấp", report); Assert.Contains("✓ Trang đọc danh sách hội thoại — HTTP 200 · 0.1s · 3 hội thoại", report);
        Assert.DoesNotContain(new string('c', 43), report);
        Assert.Contains("· Tiến trình Gateway công ty — pid 950, chạy 92s, CPU 5.2% · 16 mẫu trong 8s: 16 S epoll_wait · tiến trình con: S 3 /init /mnt/c/agentwatch.exe managed-broker", report);
        Assert.DoesNotContain("Sol", report); Assert.DoesNotContain("gpt-6-sol", report); Assert.DoesNotContain("as_live", report);
        Assert.DoesNotContain(wsl.Commands, command => command.Any(part => part.Contains('\r')));
    }

    [Fact]
    public async Task NamesTheLegThatFailsAndStillChecksTheOthers()
    {
        var (profiles, keys, studio, broker) = await Connected();
        studio.Override = (_, _, _) => (401, "{}");
        var bound = Binding(profiles.Active()!.Id, broker);
        var wsl = new Wsl(() => bound, running: "1.21.0", samples: "9 D read /mnt/c/x.jsonl,7 S epoll_wait,", doctor: " FAIL Agent Watch answers with the company key — managed-broker:invalidKey\nNot ready: 1 required check(s) failed.");
        File.WriteAllText(broker, "broker after an update");
        var lines = await SupportCheck.RunAsync(profiles, keys, new StudioClient(studio), wsl, null, broker, _ => throw new HttpRequestException("refused"),
            page: (_, _) => Task.FromResult<IReadOnlyList<DashboardProbe.Result>>([new("Trang đọc danh sách hội thoại", false, "không trả lời sau 30s: trang sẽ quay mãi ở bước này")]));
        string Failed(string label) => Assert.Single(lines, line => line.Label == label && line.State == SupportState.failed).Detail;
        Assert.Contains("[invalidKey]", Failed("Studio trả lời và nhận key"));
        Assert.Contains("Agent Watch vừa cập nhật", Failed("Nối Piagent với key công ty"));
        Assert.Contains("1.21.0", Failed("Dashboard"));
        Assert.Contains("wsl --shutdown", Failed("Windows mở được trang dashboard"));
        Assert.Contains("quay mãi", Failed("Trang đọc danh sách hội thoại"));
        Assert.Contains("9 D read /mnt/c/x.jsonl; 7 S epoll_wait", Failed("Tiến trình Gateway công ty"));
        Assert.Contains("bị chặn chờ đĩa/file ở 9/16 mẫu", Failed("Tiến trình Gateway công ty"));
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

public class DashboardProbeTests
{
    // The dashboard as the page meets it: a sign-in, then its reads.
    sealed class Dashboard(Func<string, (int Status, string Body)?> answer) : HttpMessageHandler
    {
        public readonly List<(string Path, string? Cookie, string? Origin)> Requests = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add((path, request.Headers.TryGetValues("Cookie", out var cookie) ? cookie.First() : null, request.Headers.TryGetValues("Origin", out var origin) ? origin.First() : null));
            if (answer(path) is not { } custom) { await Task.Delay(Timeout.Infinite, cancel); throw new InvalidOperationException(); }
            var response = new HttpResponseMessage((System.Net.HttpStatusCode)custom.Status) { Content = new StringContent(custom.Body) };
            if (path == "/api/v1/bootstrap" && custom.Status == 200) response.Headers.TryAddWithoutValidation("Set-Cookie", "piagent_webui_session_41873=abc.def; HttpOnly; SameSite=Strict; Path=/");
            return response;
        }
    }
    const string Origin = "http://127.0.0.1:41873";

    [Fact]
    public void ALaunchLinkGivesItsOriginAndCapabilityAndNothingElseDoes()
    {
        Assert.Equal((Origin, new string('c', 43)), DashboardProbe.Parse(Origin + "/#bootstrap=" + new string('c', 43)));
        Assert.Null(DashboardProbe.Parse("http://example.com:80/#bootstrap=" + new string('c', 43)));
        Assert.Null(DashboardProbe.Parse(Origin + "/")); Assert.Null(DashboardProbe.Parse(null));
    }

    [Fact]
    public async Task SignsInAsTheBrowserDoesAndReportsEachReadOfThePage()
    {
        var dashboard = new Dashboard(path => path == "/api/v1/session-catalog" ? (200, "{\"state\":\"ready\",\"sessions\":[{},{}]}") : (200, "{}"));
        var results = await DashboardProbe.RunAsync(Origin, "capability", dashboard, (address, origin, cookie, _) =>
            Task.FromResult(address.AbsoluteUri == "ws://127.0.0.1:41873/api/v1/gateway" && origin == Origin && cookie == "piagent_webui_session_41873=abc.def" ? "hello" : "wrong"));
        Assert.All(results, result => Assert.True(result.Ok, result.Label + ": " + result.Detail));
        Assert.Contains(results, result => result.Label == "Trang đọc danh sách hội thoại" && result.Detail.EndsWith("2 hội thoại"));
        Assert.Equal(Origin, dashboard.Requests[0].Origin);
        Assert.All(dashboard.Requests.Skip(1), request => Assert.Equal("piagent_webui_session_41873=abc.def", request.Cookie));
        Assert.DoesNotContain(results, result => result.Detail.Contains("abc.def") || result.Detail.Contains("capability"));
    }

    [Fact]
    public async Task ARefusedOrUnansweredReadIsNamedWithItsCode()
    {
        var dashboard = new Dashboard(path => path switch
        {
            "/api/v1/session-catalog" => (503, "{\"error\":{\"code\":\"session-catalog-unavailable\"}}"),
            "/api/v1/session-creation-options" => null,
            _ => (200, "{}"),
        });
        var results = await DashboardProbe.RunAsync(Origin, "capability", dashboard, (_, _, _, _) => throw new System.Net.WebSockets.WebSocketException("refused"), TimeSpan.FromMilliseconds(200));
        string Failed(string label) => Assert.Single(results, result => result.Label == label && !result.Ok).Detail;
        Assert.StartsWith("HTTP 503 session-catalog-unavailable", Failed("Trang đọc danh sách hội thoại"));
        Assert.Contains("không trả lời", Failed("Trang đọc tuỳ chọn cuộc trò chuyện mới"));
        Assert.Contains("bị từ chối", Failed("Kết nối trực tiếp (WebSocket)"));
        Assert.Contains(results, result => result.Label == "Trang đọc trạng thái công ty" && result.Ok);
        var refused = await DashboardProbe.RunAsync(Origin, "capability", new Dashboard(_ => (403, "{\"error\":{\"code\":\"bootstrap-rejected\"}}")));
        Assert.Equal("HTTP 403 bootstrap-rejected", Assert.Single(refused).Detail);
    }
}
