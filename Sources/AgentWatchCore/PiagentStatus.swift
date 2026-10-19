// Piagent conversations' states (Piagent 1.34+): the dashboard and the
// company runtime each write one small file per conversation under
// ~/.pi/agent/status/{dashboard,company}/ — working, waiting on the member
// (an approval, a question, a sign-in), done, stopped or failed. A file
// holds the conversation's title and project name, never a prompt or answer.
// Agent Watch reads them and tells the member what needs them.

import Foundation

public struct PiagentStatus: Codable, Equatable, Sendable {
    public let conversation: String
    public let title: String
    public let project: String
    public let company: Bool
    public let state: String          // idle, working, blocked, done, error
    public let kind: String?          // permission, question, auth (blocked)
    public let ended: String?         // done, stopped, failed
    public let helpers: Int
    public let since: String
    public let updated_at: String

    public init(conversation: String, title: String, project: String, company: Bool, state: String,
                kind: String? = nil, ended: String? = nil, helpers: Int = 0, since: String = "", updated_at: String = "") {
        self.conversation = conversation; self.title = title; self.project = project; self.company = company
        self.state = state; self.kind = kind; self.ended = ended; self.helpers = helpers
        self.since = since; self.updated_at = updated_at
    }
}

public enum PiagentStatusReader {
    public static var defaultAgentDir: URL {
        FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".pi/agent", isDirectory: true)
    }

    /// Every conversation's latest state; a file that does not parse, or is
    /// larger than a status file can be, is skipped.
    public static func read(agentDir: URL = defaultAgentDir) -> [PiagentStatus] {
        var latest: [String: PiagentStatus] = [:]
        let fm = FileManager.default
        for folder in ["dashboard", "company"] {
            let dir = agentDir.appendingPathComponent("status/\(folder)", isDirectory: true)
            guard let names = try? fm.contentsOfDirectory(atPath: dir.path) else { continue }
            for name in names where name.hasSuffix(".json") {
                let url = dir.appendingPathComponent(name)
                guard let size = (try? fm.attributesOfItem(atPath: url.path))?[.size] as? Int, size <= 16_384,
                      let data = try? Data(contentsOf: url),
                      let status = try? JSONDecoder().decode(PiagentStatus.self, from: data) else { continue }
                if let seen = latest[status.conversation], seen.updated_at >= status.updated_at { continue }
                latest[status.conversation] = status
            }
        }
        return latest.values.sorted { $0.updated_at > $1.updated_at }
    }
}

/// What to tell the member about one conversation.
public struct PiagentStatusAlert: Equatable, Sendable {
    public enum Kind: String, Sendable { case approval, question, signIn, done, stopped, failed }
    public let kind: Kind
    public let conversation: String
    public let title: String
    public let project: String
    public let company: Bool

    public var heading: String {
        switch kind {
        case .approval: return "Agent đang chờ bạn duyệt"
        case .question: return "Agent đang hỏi bạn"
        case .signIn: return "Agent cần bạn đăng nhập"
        case .done: return "Agent đã xong"
        case .stopped: return "Agent đã dừng"
        case .failed: return "Lượt của agent bị lỗi"
        }
    }
    public var line: String { project.isEmpty ? title : "\(title) · \(project)" }
}

/// Turns successive readings into alerts: a conversation that starts waiting
/// on the member (always, also at the first reading: they must act), and one
/// whose turn ended since the last reading (not at the first reading, so an
/// Agent Watch start does not replay old endings).
public struct PiagentStatusTracker: Sendable {
    private var last: [String: PiagentStatus] = [:]
    private var started = false

    public init() {}

    public mutating func update(_ list: [PiagentStatus]) -> [PiagentStatusAlert] {
        var alerts: [PiagentStatusAlert] = []
        var next: [String: PiagentStatus] = [:]
        for status in list {
            next[status.conversation] = status
            let before = last[status.conversation]
            let alert = { (kind: PiagentStatusAlert.Kind) in
                PiagentStatusAlert(kind: kind, conversation: status.conversation, title: status.title, project: status.project, company: status.company)
            }
            if status.state == "blocked", before?.state != "blocked" || before?.kind != status.kind {
                switch status.kind {
                case "permission": alerts.append(alert(.approval))
                case "question": alerts.append(alert(.question))
                case "auth": alerts.append(alert(.signIn))
                default: break
                }
            } else if started, let before, before.state == "working" || before.state == "blocked",
                      status.state == "done" || status.state == "error" {
                switch status.ended {
                case "stopped": alerts.append(alert(.stopped))
                case "failed": alerts.append(alert(.failed))
                default: alerts.append(status.state == "error" ? alert(.failed) : alert(.done))
                }
            }
        }
        last = next
        started = true
        return alerts
    }

    /// Conversations waiting on the member now.
    public func waiting() -> [PiagentStatus] { last.values.filter { $0.state == "blocked" }.sorted { $0.since < $1.since } }
}
