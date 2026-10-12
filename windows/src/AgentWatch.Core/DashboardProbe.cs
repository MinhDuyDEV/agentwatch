using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AgentWatch;

// Opens the Piagent dashboard the way the member's Windows browser does: signs
// in with a one-time launch link, then asks for what the page asks for when it
// opens, through WSL's localhost forwarding. A page that keeps loading is one
// of these requests failing or never answering; the page itself does not say
// which. Only statuses, error codes, counts and timings are reported.
public static partial class DashboardProbe
{
    public sealed record Result(string Label, bool Ok, string Detail);
    public delegate Task<string> Socket(Uri address, string origin, string cookie, CancellationToken cancel);

    // What the page reads on opening, in its order; the first two keep its
    // "Đang mở Piagent…" screen up until they answer.
    static readonly (string Label, string Path)[] Reads =
    [
        ("Trang đọc phiên đăng nhập", "/api/v1/browser-session"),
        ("Trang đọc danh sách hội thoại", "/api/v1/session-catalog"),
        ("Trang đọc trạng thái đang chạy", "/api/v1/session-live-state"),
        ("Trang đọc tuỳ chọn cuộc trò chuyện mới", "/api/v1/session-creation-options"),
        ("Trang đọc trạng thái công ty", "/api/v1/managed"),
    ];
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    [GeneratedRegex(@"^(http://127\.0\.0\.1:\d{1,5})/#bootstrap=([A-Za-z0-9_-]{20,200})$")]
    private static partial Regex Launch();

    // The origin and the one-time capability of a launch link, or null.
    public static (string Origin, string Capability)? Parse(string? launchUrl) =>
        launchUrl is not null && Launch().Match(launchUrl) is { Success: true } match ? (match.Groups[1].Value, match.Groups[2].Value) : null;

    static string? Code(byte[] body)
    {
        try { return JsonNode.Parse(body)?["error"]?["code"]?.GetValue<string>(); } catch (Exception) { return null; }
    }

    static string Seconds(Stopwatch watch) => $"{watch.Elapsed.TotalSeconds:0.0}s";

    public static async Task<IReadOnlyList<Result>> RunAsync(string origin, string capability, HttpMessageHandler? handler = null, Socket? socket = null, TimeSpan? wait = null)
    {
        var results = new List<Result>();
        var limit = wait ?? Wait;
        using var client = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };

        async Task<(int Status, byte[] Body, string? Cookie)> Send(HttpRequestMessage request)
        {
            using var cancel = new CancellationTokenSource(limit);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            using var response = await client.SendAsync(request, cancel.Token);
            var cookie = response.Headers.TryGetValues("Set-Cookie", out var values) ? values.FirstOrDefault()?.Split(';', 2)[0] : null;
            return ((int)response.StatusCode, await response.Content.ReadAsByteArrayAsync(cancel.Token), cookie);
        }
        // "không trả lời" is the page's endless loading: it waits without limit.
        static string Unanswered(Exception error, Stopwatch watch, TimeSpan limit) => error is OperationCanceledException
            ? $"không trả lời sau {limit.TotalSeconds:0}s: trang sẽ quay mãi ở bước này"
            : $"mất kết nối sau {Seconds(watch)} [{error.GetType().Name}]";

        string cookie;
        var watch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, origin + "/api/v1/bootstrap")
                { Content = new StringContent(new JsonObject { ["capability"] = capability }.ToJsonString(), Encoding.UTF8, "application/json") };
            request.Headers.TryAddWithoutValidation("Origin", origin);
            var (status, body, issued) = await Send(request);
            if (status != 200 || issued is null)
            {
                results.Add(new("Đăng nhập trang dashboard", false, $"HTTP {status}{(Code(body) is { } code ? " " + code : "")}"));
                return results;
            }
            cookie = issued;
            results.Add(new("Đăng nhập trang dashboard", true, $"HTTP 200 · {Seconds(watch)}"));
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException)
        { results.Add(new("Đăng nhập trang dashboard", false, Unanswered(error, watch, limit))); return results; }

        foreach (var (label, path) in Reads)
        {
            watch.Restart();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, origin + path);
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
                var (status, body, _) = await Send(request);
                var detail = $"HTTP {status}{(status != 200 && Code(body) is { } code ? " " + code : "")} · {Seconds(watch)}";
                var ok = status == 200;
                if (ok && path == "/api/v1/session-catalog")
                {
                    JsonObject? catalog = null;
                    try { catalog = JsonNode.Parse(body) as JsonObject; } catch (System.Text.Json.JsonException) { /* reported below */ }
                    var state = catalog?["state"]?.ToString();
                    ok = state == "ready";
                    detail += ok ? $" · {(catalog?["sessions"] as JsonArray)?.Count ?? 0} hội thoại" : $" · trạng thái {state ?? "không đọc được"}{(catalog?["reasonCode"] is { } reason ? " " + reason : "")}";
                }
                if (ok && watch.Elapsed > TimeSpan.FromSeconds(8)) detail += " · chậm";
                results.Add(new(label, ok, detail));
            }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException)
            { results.Add(new(label, false, Unanswered(error, watch, limit))); }
        }

        // Sending and live updates use one WebSocket; without it the page says "reconnecting".
        watch.Restart();
        try
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var hello = await (socket ?? Hello)(new Uri(origin.Replace("http:", "ws:", StringComparison.Ordinal) + "/api/v1/gateway"), origin, cookie, cancel.Token);
            results.Add(new("Kết nối trực tiếp (WebSocket)", hello == "hello", hello == "hello" ? $"đã nối · {Seconds(watch)}" : $"Gateway trả lời “{hello}” thay vì hello"));
        }
        catch (Exception error) when (error is WebSocketException or OperationCanceledException or IOException or HttpRequestException)
        {
            results.Add(new("Kết nối trực tiếp (WebSocket)", false, error is OperationCanceledException ? "không trả lời sau 15s"
                : $"bị từ chối sau {Seconds(watch)} [{(error as WebSocketException)?.WebSocketErrorCode.ToString() ?? error.GetType().Name}]"));
        }
        return results;
    }

    // The first frame of the Gateway's protocol after our connect frame.
    static async Task<string> Hello(Uri address, string origin, string cookie, CancellationToken cancel)
    {
        using var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol("piagent.gateway.v1");
        socket.Options.SetRequestHeader("Origin", origin);
        socket.Options.SetRequestHeader("Cookie", cookie);
        socket.Options.Proxy = null;
        await socket.ConnectAsync(address, cancel);
        var connect = new JsonObject { ["schemaVersion"] = 1, ["version"] = "piagent-gateway-protocol-v1", ["messageType"] = "connect", ["clientRef"] = "agentwatch-support",
            ["minimumProtocol"] = 1, ["maximumProtocol"] = 1, ["lastEventSequence"] = null, ["catalogRevision"] = null };
        await socket.SendAsync(Encoding.UTF8.GetBytes(connect.ToJsonString()), WebSocketMessageType.Text, true, cancel);
        var buffer = new byte[16_384]; using var frame = new MemoryStream();
        while (true)
        {
            var read = await socket.ReceiveAsync(buffer, cancel);
            if (read.MessageType == WebSocketMessageType.Close) return $"đóng {(int?)socket.CloseStatus} {socket.CloseStatusDescription}".Trim();
            frame.Write(buffer, 0, read.Count);
            if (frame.Length > 131_072) return "khung quá lớn";
            if (!read.EndOfMessage) continue;
            string? type = null;
            try { type = JsonNode.Parse(frame.ToArray())?["messageType"]?.GetValue<string>(); } catch (Exception) { /* not the protocol */ }
            try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", cancel); } catch (WebSocketException) { /* already closing */ }
            return type ?? "không đúng giao thức";
        }
    }
}
