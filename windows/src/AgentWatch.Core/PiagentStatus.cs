using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentWatch;

// Piagent conversations' states (Piagent 1.34+): the dashboard and the company
// runtime in WSL each write one small file per conversation under
// ~/.pi/agent/status/{dashboard,company}/ — working, waiting on the member
// (an approval, a question, a sign-in), done, stopped or failed. A file holds
// the conversation's title and project name, never a prompt or an answer.
// Same rules as the macOS app (AgentWatchCore/PiagentStatus.swift).
public sealed record PiagentStatus(
    [property: JsonPropertyName("conversation")] string Conversation,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("company")] bool Company,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("kind")] string? Kind = null,
    [property: JsonPropertyName("ended")] string? Ended = null,
    [property: JsonPropertyName("helpers")] int Helpers = 0,
    [property: JsonPropertyName("since")] string Since = "",
    [property: JsonPropertyName("updated_at")] string UpdatedAt = "");

public enum PiagentAlertKind { Approval, Question, SignIn, Done, Stopped, Failed }

public sealed record PiagentAlert(PiagentAlertKind Kind, string Conversation, string Title, string Project, bool Company)
{
    public string Heading => Kind switch
    {
        PiagentAlertKind.Approval => "Agent đang chờ bạn duyệt",
        PiagentAlertKind.Question => "Agent đang hỏi bạn",
        PiagentAlertKind.SignIn => "Agent cần bạn đăng nhập",
        PiagentAlertKind.Done => "Agent đã xong",
        PiagentAlertKind.Stopped => "Agent đã dừng",
        _ => "Lượt của agent bị lỗi",
    };
    public string Line => Project.Length == 0 ? Title : $"{Title} · {Project}";
}

public static class PiagentStatusReader
{
    // Every conversation's latest state; a file that does not parse, or is
    // larger than a status file can be, is skipped.
    public static IReadOnlyList<PiagentStatus> Read(string agentDir)
    {
        var latest = new Dictionary<string, PiagentStatus>();
        foreach (var folder in new[] { "dashboard", "company" })
        {
            var dir = Path.Combine(agentDir, "status", folder);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            {
                try
                {
                    if (new FileInfo(file).Length > 16_384) continue;
                    var status = JsonSerializer.Deserialize<PiagentStatus>(File.ReadAllText(file));
                    if (status is null || string.IsNullOrEmpty(status.Conversation)) continue;
                    if (latest.TryGetValue(status.Conversation, out var seen) && string.CompareOrdinal(seen.UpdatedAt, status.UpdatedAt) >= 0) continue;
                    latest[status.Conversation] = status;
                }
                catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { }
            }
        }
        return latest.Values.OrderByDescending(s => s.UpdatedAt, StringComparer.Ordinal).ToList();
    }
}

// Successive readings to alerts: a conversation that starts waiting on the
// member (also at the first reading: they must act), and one whose turn ended
// since the last reading (not at the first, so a start replays nothing).
public sealed class PiagentStatusTracker
{
    Dictionary<string, PiagentStatus> last = [];
    bool started;

    public IReadOnlyList<PiagentAlert> Update(IReadOnlyList<PiagentStatus> list)
    {
        var alerts = new List<PiagentAlert>();
        var next = new Dictionary<string, PiagentStatus>();
        foreach (var status in list)
        {
            next[status.Conversation] = status;
            last.TryGetValue(status.Conversation, out var before);
            PiagentAlert Alert(PiagentAlertKind kind) => new(kind, status.Conversation, status.Title, status.Project, status.Company);
            if (status.State == "blocked" && (before?.State != "blocked" || before.Kind != status.Kind))
            {
                if (status.Kind == "permission") alerts.Add(Alert(PiagentAlertKind.Approval));
                else if (status.Kind == "question") alerts.Add(Alert(PiagentAlertKind.Question));
                else if (status.Kind == "auth") alerts.Add(Alert(PiagentAlertKind.SignIn));
            }
            else if (started && before is not null && (before.State is "working" or "blocked") && status.State is "done" or "error")
            {
                alerts.Add(Alert(status.Ended switch
                {
                    "stopped" => PiagentAlertKind.Stopped,
                    "failed" => PiagentAlertKind.Failed,
                    _ => status.State == "error" ? PiagentAlertKind.Failed : PiagentAlertKind.Done,
                }));
            }
        }
        last = next;
        started = true;
        return alerts;
    }

    public IReadOnlyList<PiagentStatus> Waiting() => last.Values.Where(s => s.State == "blocked").OrderBy(s => s.Since, StringComparer.Ordinal).ToList();
}
