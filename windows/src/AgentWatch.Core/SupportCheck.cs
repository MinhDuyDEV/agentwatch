using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AgentWatch;

public enum SupportState { ok, failed, note }

public sealed record SupportLine(string Area, SupportState State, string Label, string Detail);

// "Kiểm tra log": one pass over the three legs a company conversation needs on
// Windows (this app and its key, the route to Studio, Piagent and its
// dashboard in WSL), so support can see which leg fails. The report carries no
// key, no model name and no conversation content.
public static partial class SupportCheck
{
    public const string AgentWatch = "Agent Watch", Studio = "Agent Studio (đường nhận)", Piagent = "Piagent trong WSL";

    // The dashboard's facts, read in one WSL call; $1 is the Node Piagent runs
    // on and $2 the installed package.json. Every probe reports, none fails.
    const string DashboardFacts = """
        PATH="$(dirname "$1"):$PATH"
        echo "@@version@@"; "$1" -p 'require(process.argv[1]).version' "$2" 2>&1 | tail -n 1
        echo "@@binding@@"; cat "$HOME/.pi/agent/agent-watch-managed.json" 2>/dev/null
        echo "@@status@@"; piagent dashboard status --json 2>&1 | tail -n 1
        echo "@@doctor@@"; piagent dashboard doctor --json 2>&1 | tail -n 1
        echo "@@log@@"; find "$HOME/.pi" -maxdepth 5 -name gateway-stderr.log -size +0c -exec tail -c 600 {} \; 2>/dev/null
        echo "@@end@@"
        """;
    const string StudioDoctor = """
        PATH="$(dirname "$1"):$PATH"
        echo "@@doctor@@"; piagent studio --doctor 2>&1
        echo "@@end@@"
        """;

    [GeneratedRegex(@"^http://127\.0\.0\.1:\d{1,5}$")]
    private static partial Regex Loopback();

    static string Explain(Exception error) => error switch
    {
        StudioException studio => $"{StudioException.Describe(studio.Code)} [{studio.Code}{(studio.StudioCode is { } code ? ":" + code : "")}]",
        WslCommandException failed => $"Lệnh trong WSL lỗi ({failed.Detail}).",
        InvalidOperationException invalid => invalid.Message switch
        {
            "wsl-runtime-unreadable" or "wsl-runtime-invalid" => "Chưa thấy Piagent trong WSL: chạy lại lệnh cài. [" + invalid.Message + "]",
            "wsl-runtime-unsupported" => "Piagent hoặc Pi trong WSL chưa đúng bản (cần Pi 0.87.1): chạy `piagent-update` trong Ubuntu. [wsl-runtime-unsupported]",
            "wsl-root-user" => "Ubuntu đang mở bằng root: chạy lại lệnh cài để đặt tài khoản Linux. [wsl-root-user]",
            "wsl-timeout" => "WSL không trả lời sau 2 phút: mở Ubuntu một lần, hoặc chạy `wsl --shutdown` rồi thử lại. [wsl-timeout]",
            _ => $"[{invalid.Message}]",
        },
        System.ComponentModel.Win32Exception => "Không khởi chạy được wsl.exe: cài WSL2 rồi thử lại.",
        _ => $"[{error.GetType().Name}: {error.Message[..Math.Min(error.Message.Length, 200)]}]",
    };

    static string Section(string output, string name)
    {
        var start = output.IndexOf($"@@{name}@@", StringComparison.Ordinal);
        if (start < 0) return "";
        start += name.Length + 4;
        var end = output.IndexOf("@@", start, StringComparison.Ordinal);
        return (end < 0 ? output[start..] : output[start..end]).Trim();
    }

    static JsonObject? Json(string text)
    {
        try { return JsonNode.Parse(text) as JsonObject; } catch (System.Text.Json.JsonException) { return null; }
    }

    static string? Text(JsonObject? value, string name)
    {
        try { return value?[name]?.GetValue<string>(); } catch (Exception error) when (error is InvalidOperationException or FormatException) { return null; }
    }

    static async Task<int> Fetch(Uri address)
    {
        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        using var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead);
        return (int)response.StatusCode;
    }

    // The check never throws: a step that breaks in an unforeseen way becomes
    // a line of the report. `progress` hears each line as it is known, so a
    // report written along the way shows the last step reached.
    public static async Task<IReadOnlyList<SupportLine>> RunAsync(StudioProfileStore profiles, IStudioKeyStore keys, StudioClient client, IWsl wsl,
        string? distro, string brokerPath, Func<Uri, Task<int>>? probe = null, Action<IReadOnlyList<SupportLine>>? progress = null)
    {
        var lines = new List<SupportLine>();
        void Add(string area, SupportState state, string label, string detail = "") { lines.Add(new(area, state, label, detail)); progress?.Invoke(lines); }
        try { await CollectAsync(Add, profiles, keys, client, wsl, distro, brokerPath, probe); }
        catch (Exception error) { Add(lines.LastOrDefault()?.Area ?? AgentWatch, SupportState.failed, "Kiểm tra dừng giữa chừng", Explain(error)); }
        return lines;
    }

    static async Task CollectAsync(Action<string, SupportState, string, string> add, StudioProfileStore profiles, IStudioKeyStore keys, StudioClient client, IWsl wsl,
        string? distro, string brokerPath, Func<Uri, Task<int>>? probe)
    {
        void Add(string area, SupportState state, string label, string detail = "") => add(area, state, label, detail);
        async Task<bool> Step(string area, string label, Func<Task<string>> run)
        {
            try { Add(area, SupportState.ok, label, await run()); return true; }
            catch (Exception error) { Add(area, SupportState.failed, label, Explain(error)); return false; }
        }

        Add(AgentWatch, SupportState.note, "Phiên bản", StudioClient.UserAgent);
        if (CrashLog.Last() is { } crash) Add(AgentWatch, SupportState.failed, "Lỗi app gần nhất", crash);
        StudioProfile? active = null; string? key = null;
        try
        {
            active = profiles.Active();
            key = active is null ? null : keys.Load(active.Id);
            if (active is null) Add(AgentWatch, SupportState.failed, "Kết nối đã lưu", "Chưa kết nối Studio: mở Thiết lập từng bước.");
            else if (key is null) Add(AgentWatch, SupportState.failed, "Key đã lưu", "Không đọc được key trên máy này: kết nối lại Studio.");
            else Add(AgentWatch, SupportState.ok, "Kết nối và key đã lưu", $"{active.Origin} · {(active.CredentialMode == StudioCredentialMode.managed ? "key công ty" : "key cá nhân")}");
        }
        catch (Exception error) { Add(AgentWatch, SupportState.failed, "Kết nối đã lưu", Explain(error)); }
        string? brokerDigest = null;
        try
        {
            using var stream = File.OpenRead(brokerPath);
            brokerDigest = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            Add(AgentWatch, SupportState.ok, "agentwatch.exe (Piagent gọi)", "có trên máy");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Add(AgentWatch, SupportState.failed, "agentwatch.exe (Piagent gọi)", "Không đọc được file cạnh app: cài lại Agent Watch."); }

        if (active is { } profile && key is { } secret)
        {
            // The number of granted models only: their names stay in Studio.
            await Step(Studio, "Studio trả lời và nhận key", async () =>
            {
                var connection = await client.ConnectAsync(profile.StudioOrigin, secret);
                return connection.Models is { } models ? $"{models.Length} model được cấp"
                    : "chưa tải được danh sách model" + (connection.ModelsError is { } failure ? $" [{failure}]" : "");
            });
            if (profile.CredentialMode == StudioCredentialMode.managed) await Step(Studio, "Cấu hình công ty (Harness)", async () =>
            {
                var manifest = await client.ConfigurationAsync(profile.StudioOrigin, secret);
                if (manifest.Harness is null) throw new StudioException(StudioError.permissionDenied);
                manifest.Validate(profile);
                return $"bản {manifest.Revision[..12]}";
            });
        }
        else Add(Studio, SupportState.note, "Chưa kiểm tra", "Cần kết nối Studio trước.");

        PiagentWslBinding.Runtime? runtime = null;
        await Step(Piagent, "WSL và bản cài Piagent", async () =>
        {
            runtime = await PiagentWslBinding.InspectAsync(wsl, string.IsNullOrWhiteSpace(distro) ? null : distro.Trim());
            if (runtime.User == "root") throw new InvalidOperationException("wsl-root-user");
            return $"{runtime.Distro} · user {runtime.User} · Pi {runtime.PiVersion}";
        });
        if (runtime is not { } found) return;
        await Step(Piagent, "WSL2, sandbox và Windows interop", async () => { await WslReadiness.CheckAsync(wsl, found.Distro); return "sẵn sàng"; });

        string facts;
        try
        {
            var package = found.Entrypoint[..found.Entrypoint.LastIndexOf("/scripts/", StringComparison.Ordinal)] + "/package.json";
            facts = await wsl.RunAsync(found.Distro, null, "bash", "-lic", DashboardFacts.ReplaceLineEndings("\n"), "agentwatch-support", found.Node, package);
        }
        catch (Exception error) { Add(Piagent, SupportState.failed, "Đọc trạng thái dashboard", Explain(error)); return; }

        var installed = Section(facts, "version");
        Add(Piagent, SupportState.note, "Bản Piagent đã cài", installed.Length is > 0 and <= 40 ? installed : "không đọc được");

        if (active is { CredentialMode: StudioCredentialMode.managed } bound)
        {
            var binding = Json(Section(facts, "binding"));
            var stale = new List<string>();
            if (binding is null) stale.Add("chưa có");
            else
            {
                if (Text(binding, "profile_id") != bound.Id) stale.Add("key khác");
                if (Text(binding, "entrypoint_sha256") != found.EntrypointSha256 || Text(binding, "node_sha256") != found.NodeSha256) stale.Add("Piagent vừa cập nhật");
                if (brokerDigest is not null && Text(binding, "broker_sha256") != brokerDigest) stale.Add("Agent Watch vừa cập nhật");
            }
            if (stale.Count == 0) Add(Piagent, SupportState.ok, "Nối Piagent với key công ty", "khớp bản đang cài");
            else Add(Piagent, SupportState.failed, "Nối Piagent với key công ty", $"{string.Join(", ", stale)}: bấm “Kiểm tra lại” để nối lại, rồi mở lại Piagent. [managed-launch-binding-changed]");
        }

        var status = Json(Section(facts, "status"));
        var gateway = status?["gateway"] as JsonObject;
        var origin = Text(gateway, "origin");
        if (Text(status, "state") != "running" || origin is null || !Loopback().IsMatch(origin))
            Add(Piagent, SupportState.note, "Dashboard", status?["staleDescriptor"]?.GetValue<bool>() == true
                ? "đã dừng, còn dấu vết phiên trước: mở lại Piagent." : "chưa chạy: bấm “Mở Piagent” rồi kiểm tra lại.");
        else
        {
            var running = Text(gateway, "packageVersion");
            if (running is not null && installed.Length > 0 && running != installed)
                Add(Piagent, SupportState.failed, "Dashboard", $"đang chạy bản {running}, bản đã cài là {installed}: trong Ubuntu chạy `piagent dashboard restart`.");
            else Add(Piagent, SupportState.ok, "Dashboard", $"đang chạy ở {origin}");
            // The page is served inside WSL and opened in a Windows browser.
            try
            {
                var code = await (probe ?? Fetch)(new Uri(origin + "/"));
                if (code == 200) Add(Piagent, SupportState.ok, "Windows mở được trang dashboard", $"HTTP {code}");
                else Add(Piagent, SupportState.failed, "Windows mở được trang dashboard", $"HTTP {code}: cổng này đang do chương trình khác trả lời; trong Ubuntu chạy `piagent dashboard restart`.");
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException)
            { Add(Piagent, SupportState.failed, "Windows mở được trang dashboard", "Windows không tới được cổng của WSL: chạy `wsl --shutdown` trong PowerShell rồi mở lại Piagent."); }
        }

        var doctor = Json(Section(facts, "doctor"));
        if (doctor?["checks"] is JsonObject checks)
        {
            var failed = checks.Where(check => check.Value is JsonValue value && value.TryGetValue<bool>(out var passed) && !passed).Select(check => check.Key).ToArray();
            if (failed.Length == 0) Add(Piagent, SupportState.ok, "Dashboard doctor", "đạt");
            else Add(Piagent, SupportState.failed, "Dashboard doctor", $"{string.Join(", ", failed)}: trong Ubuntu chạy `piagent dashboard doctor --repair`.");
        }
        else Add(Piagent, SupportState.failed, "Dashboard doctor", "Lệnh `piagent dashboard doctor` không trả kết quả: mở Ubuntu và chạy lệnh đó để xem lỗi.");

        var log = Section(facts, "log").ReplaceLineEndings(" ");
        if (log.Length > 0) Add(Piagent, SupportState.failed, "Lỗi lần khởi động dashboard gần nhất", log[..Math.Min(log.Length, 400)]);

        // Piagent's own check of the company route, from inside WSL: sandbox,
        // this broker answering with the key, Studio reachable from Ubuntu.
        if (active is { CredentialMode: StudioCredentialMode.managed })
        {
            try
            {
                var report = Section(await wsl.RunAsync(found.Distro, null, "bash", "-lic", StudioDoctor.ReplaceLineEndings("\n"), "agentwatch-support", found.Node), "doctor")
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var failures = report.Where(line => line.StartsWith("FAIL", StringComparison.Ordinal)).Select(line => line[4..].Trim()).ToArray();
                var verdict = report.LastOrDefault() ?? "";
                if (failures.Length > 0) Add(Piagent, SupportState.failed, "Kiểm tra phiên công ty (piagent studio --doctor)", string.Join(" | ", failures));
                else if (verdict.StartsWith("Ready", StringComparison.Ordinal)) Add(Piagent, SupportState.ok, "Kiểm tra phiên công ty (piagent studio --doctor)", "đạt");
                else Add(Piagent, SupportState.failed, "Kiểm tra phiên công ty (piagent studio --doctor)", verdict.Length > 0 ? verdict[..Math.Min(verdict.Length, 300)] : "không có kết quả");
            }
            catch (Exception error) { Add(Piagent, SupportState.failed, "Kiểm tra phiên công ty (piagent studio --doctor)", Explain(error)); }
        }
    }

    // What the member reads and copies for support.
    public static string Format(IReadOnlyList<SupportLine> lines, DateTimeOffset at, bool done = true)
    {
        var text = new StringBuilder();
        var broken = lines.Where(line => line.State == SupportState.failed).Select(line => line.Area).Distinct().ToArray();
        text.Append(!done ? "Đang kiểm tra… (chưa xong)" : broken.Length == 0 ? "Không thấy lỗi ở cả ba chặng." : "Có lỗi ở: " + string.Join(", ", broken) + ".");
        text.Append($"\nKiểm tra lúc {at:HH:mm dd/MM/yyyy}\n");
        foreach (var area in lines.Select(line => line.Area).Distinct())
        {
            text.Append('\n').Append(area.ToUpperInvariant()).Append('\n');
            foreach (var line in lines.Where(line => line.Area == area))
                text.Append(line.State switch { SupportState.ok => "  ✓ ", SupportState.failed => "  ✗ ", _ => "  · " })
                    .Append(line.Label).Append(line.Detail.Length > 0 ? " — " + line.Detail : "").Append('\n');
        }
        return text.ToString().TrimEnd();
    }

    // The report is also kept as a file, to read and send after the window is gone.
    public static string ReportFile => Path.Combine(AgentWatchPaths.DataDirectory, "support-check.txt");

    public static void Save(string report)
    {
        try { Directory.CreateDirectory(AgentWatchPaths.DataDirectory); File.WriteAllText(ReportFile, report + "\n", new UTF8Encoding(true)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* the report is still on screen */ }
    }
}

// An error nothing handled, kept where support can read it: the app used to
// close without a trace. Exception text only; keys never appear in one.
public static class CrashLog
{
    public static string File => Path.Combine(AgentWatchPaths.DataDirectory, "crash.log");

    public static void Write(Exception? error, DateTimeOffset? at = null)
    {
        try
        {
            Directory.CreateDirectory(AgentWatchPaths.DataDirectory);
            var text = error?.ToString() ?? "unknown error";
            System.IO.File.WriteAllText(File, $"{at ?? DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {StudioClient.UserAgent}\n{text[..Math.Min(text.Length, 8000)]}\n", new UTF8Encoding(true));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { /* nowhere left to say it */ }
    }

    // When it happened and its first line, for the support report.
    public static string? Last()
    {
        try
        {
            var lines = System.IO.File.ReadLines(File).Take(2).ToArray();
            return lines.Length == 0 ? null : string.Join(" · ", lines)[..Math.Min(string.Join(" · ", lines).Length, 300)] + $" (chi tiết: {File})";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }
}
