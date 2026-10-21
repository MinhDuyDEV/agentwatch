import Foundation

/// Team memory signals (Agent Studio docs/team-memory/FEEDBACK.md): a member's
/// correction or standing instruction to Claude Code or Codex, sent to the
/// member's own Studio, where team memory may propose it to the team's
/// administrators as a rule. Off unless the member turns it on. Only the
/// member's own prompt goes, cleaned of injected context and redacted, at most
/// 1,000 characters; Studio keeps no text, only a candidate rule it may write.
/// Piagent sessions are left out: Piagent sends its rounds itself.
public enum MemorySignals {
    public static let maxText = 1000
    public static let maxPerDay = 20
    public static let sources: Set<SessionSource> = [.cli, .desktop, .codex]

    // Same cues as Studio's judge gate (internal/memory/feedback.go), so a
    // prompt Studio would not judge is never sent.
    nonisolated(unsafe) static let cue = try! NSRegularExpression(pattern:
        #"\b(?:always|never|from now on|don'?t|do not|must|should(?:n'?t)?|instead|wrong|not what|remember(?: that)?|stop|prefer|avoid|make sure)\b|"#
        + #"từ (?:giờ|nay)|luôn(?: luôn)?|không bao giờ|đừng|không được|phải|thay vì|sai rồi|không phải|nhớ là|ghi nhớ|lần sau|chú ý|lưu ý|tránh"#,
        options: [.caseInsensitive])

    /// The member's words worth sending from one prompt, or nil.
    public static func candidate(_ raw: String) -> String? {
        guard ReportPromptText.origin(raw) == .employee else { return nil }
        var text = ReportPromptText.clean(raw)
            .replacingOccurrences(of: "[Ngữ cảnh tự động đã lược]", with: "")
            .replacingOccurrences(of: "[Ảnh đính kèm]", with: "")
            .trimmingCharacters(in: .whitespacesAndNewlines)
        guard text.count >= 8, text.count <= 4000 else { return nil }
        guard cue.firstMatch(in: text, range: NSRange(text.startIndex..., in: text)) != nil else { return nil }
        if text.count > maxText { text = String(text.prefix(maxText)) }
        return text
    }

    /// A repository key as Piagent and Studio name it (memory-repos.mjs).
    public static func repoKey(remote: String) -> String? {
        var s = remote.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !s.isEmpty else { return nil }
        if let m = s.wholeMatch(of: /^[\w.-]+@([^:\/]+):(.+)$/) {
            s = "\(m.1)/\(m.2)"
        } else {
            guard let url = URL(string: s), let scheme = url.scheme?.lowercased(), ["http", "https", "ssh", "git"].contains(scheme),
                  let host = url.host(percentEncoded: false), !host.isEmpty else { return nil }
            s = host + url.path(percentEncoded: false)
        }
        s = s.lowercased().replacing(/\.git\/?$/, with: "").replacing(/\/+$/, with: "").replacing(/\/{2,}/, with: "/")
        guard s.count <= 200, !s.contains(".."), s.wholeMatch(of: /^[a-z0-9][a-z0-9._-]*\/[a-z0-9._\/-]+$/) != nil else { return nil }
        return s
    }

    /// The origin remote of a working folder, read with git.
    public static func repoKey(directory: String) -> String? {
        var isDirectory: ObjCBool = false
        guard FileManager.default.fileExists(atPath: directory, isDirectory: &isDirectory), isDirectory.boolValue else { return nil }
        let process = Process(), pipe = Pipe()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/git")
        process.arguments = ["-C", directory, "remote", "get-url", "origin"]
        process.standardOutput = pipe; process.standardError = FileHandle.nullDevice
        do { try process.run() } catch { return nil }
        let deadline = Date().addingTimeInterval(3)
        while process.isRunning && Date() < deadline { usleep(20_000) }
        if process.isRunning { process.terminate(); return nil }
        guard process.terminationStatus == 0 else { return nil }
        let out = String(data: pipe.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? ""
        return repoKey(remote: out)
    }
}

public struct MemorySignalRequest: Encodable, Equatable, Sendable {
    public let repoKey: String
    public let conversationID: String?
    public let text: String
    public let source = "agentwatch"
    enum CodingKeys: String, CodingKey { case repoKey = "repo_key", conversationID = "conversation_id", text, source }
    public init(repoKey: String, conversationID: String?, text: String) {
        self.repoKey = repoKey
        // Studio's conversation ids: 8-80 of [A-Za-z0-9_-].
        self.conversationID = conversationID.flatMap { $0.wholeMatch(of: /^[A-Za-z0-9_-]{8,80}$/) != nil ? $0 : nil }
        self.text = text
    }
}

public enum MemorySignalOutcome: String, Sendable, Equatable {
    /// Studio took it (or has no judge, or the team keeps no conversations).
    case taken
    /// Memory is off for the team, or this Studio has no memory signals.
    case off
    case rateLimited
    case failed
}

public struct MemorySignalClient: Sendable {
    let transport: any StudioHTTPTransport
    public init(transport: any StudioHTTPTransport = StudioURLSessionTransport()) { self.transport = transport }

    public func send(origin: StudioOrigin, key: String, _ signal: MemorySignalRequest) async -> MemorySignalOutcome {
        guard StudioClient.validKey(key), let body = try? JSONEncoder().encode(signal) else { return .failed }
        var request = URLRequest(url: origin.url(path: "v1/memory/signal"))
        request.httpMethod = "POST"; request.httpBody = body
        request.setValue("Bearer " + key, forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue(StudioClient.userAgent, forHTTPHeaderField: "User-Agent")
        guard let response = try? await transport.send(request, origin: origin) else { return .failed }
        switch response.status {
        case 200:
            struct Answer: Decodable { let status: String }
            let status = (try? JSONDecoder().decode(Answer.self, from: response.body))?.status
            return status == "rate_limited" ? .rateLimited : .taken
        case 204, 404: return .off
        case 429: return .rateLimited
        default: return .failed
        }
    }
}

/// Which prompts were sent (by audit key), and how many today: a prompt goes
/// once, at most `maxPerDay` a day. Kept in the app's defaults, ids only.
public struct MemorySignalLedger: Codable, Equatable, Sendable {
    public var sent: [String] = []
    public var day = ""
    public var today = 0
    static let keep = 2000
    public init() {}

    public static func load(_ defaults: UserDefaults) -> MemorySignalLedger {
        defaults.data(forKey: "memorySignals.ledger").flatMap { try? JSONDecoder().decode(Self.self, from: $0) } ?? MemorySignalLedger()
    }
    public func save(_ defaults: UserDefaults) {
        if let data = try? JSONEncoder().encode(self) { defaults.set(data, forKey: "memorySignals.ledger") }
    }
    mutating func record(_ key: String, on date: Date) {
        let d = Self.dayString(date)
        if d != day { day = d; today = 0 }
        today += 1
        sent.append(key)
        if sent.count > Self.keep { sent.removeFirst(sent.count - Self.keep) }
    }
    func room(on date: Date) -> Int { Self.dayString(date) == day ? max(0, MemorySignals.maxPerDay - today) : MemorySignals.maxPerDay }
    static func dayString(_ date: Date) -> String {
        let c = Calendar(identifier: .gregorian).dateComponents(in: .current, from: date)
        return String(format: "%04d-%02d-%02d", c.year ?? 0, c.month ?? 0, c.day ?? 0)
    }
}

public struct MemorySignalScanResult: Equatable, Sendable {
    public var sent = 0, skipped = 0
    public var off = false
}

public enum MemorySignalScan {
    /// Sends the new candidate prompts, oldest first. `repoKey` resolves a
    /// session's folder; `send` delivers one signal. Stops when memory is off
    /// or Studio limits the rate; a failure is retried next time.
    public static func run(prompts: [PromptRecord], ledger: inout MemorySignalLedger, now: Date = Date(),
                           repoKey: (String) -> String?, send: (MemorySignalRequest) async -> MemorySignalOutcome) async -> MemorySignalScanResult {
        var result = MemorySignalScanResult()
        var repos: [String: String?] = [:]
        let sent = Set(ledger.sent)
        for prompt in prompts.sorted(by: { $0.timestamp < $1.timestamp }) where MemorySignals.sources.contains(prompt.source) && !sent.contains(prompt.auditKey) {
            guard ledger.room(on: now) > 0 else { break }
            guard let text = MemorySignals.candidate(prompt.text) else { continue }
            let folder = prompt.projectDisplay
            if repos[folder] == nil { repos[folder] = .some(repoKey(folder)) }
            guard let key = repos[folder] ?? nil else { result.skipped += 1; continue }
            switch await send(MemorySignalRequest(repoKey: key, conversationID: prompt.sessionUuid, text: text)) {
            case .taken: ledger.record(prompt.auditKey, on: now); result.sent += 1
            case .off: result.off = true; return result
            case .rateLimited: return result
            case .failed: result.skipped += 1
            }
        }
        return result
    }
}

/// Runs the scan every 15 minutes while the member has it on, with the
/// active direct-key Studio profile (a company key reaches memory only inside
/// a Piagent run, so it has nothing to send here).
@MainActor @Observable public final class MemorySignalService {
    public static let shared = MemorySignalService()
    public static let interval: TimeInterval = 15 * 60
    public static let enabledKey = "memorySignals.enabled"
    public private(set) var lastRun: Date?
    public private(set) var lastSent = 0
    public private(set) var note = ""
    private var loop: Task<Void, Never>?
    private let defaults: UserDefaults

    public init(defaults: UserDefaults = StudioPreferences.applicationDefaults) { self.defaults = defaults }

    public var enabled: Bool {
        get { defaults.bool(forKey: Self.enabledKey) }
        set { defaults.set(newValue, forKey: Self.enabledKey); newValue ? start() : stop() }
    }

    public func start() {
        guard enabled, loop == nil else { return }
        loop = Task { @MainActor [weak self] in
            while !Task.isCancelled {
                await self?.runOnce()
                try? await Task.sleep(for: .seconds(Self.interval))
            }
        }
    }
    public func stop() { loop?.cancel(); loop = nil }

    public func runOnce(now: Date = Date()) async {
        guard enabled else { return }
        guard let profile = try? StudioPreferences(defaults: defaults).load(), profile.credentialMode == .direct else {
            note = "Cần kết nối Studio bằng key trực tiếp (key CLI)."; return
        }
        guard let key = StudioCredentialCommand.savedKey(profileID: profile.id) else { note = "Chưa đọc được key Studio trong Keychain."; return }
        var ledger = MemorySignalLedger.load(defaults)
        let origin = profile.origin, client = MemorySignalClient()
        let outcome: (MemorySignalLedger, MemorySignalScanResult) = await Task.detached(priority: .utility) { [ledger] in
            var ledger = ledger
            let prompts = PromptHistory.loadPrompts(in: now.addingTimeInterval(-2 * 86400)..<now)
            let result = await MemorySignalScan.run(prompts: prompts, ledger: &ledger, now: now, repoKey: MemorySignals.repoKey(directory:)) {
                await client.send(origin: origin, key: key, $0)
            }
            return (ledger, result)
        }.value
        ledger = outcome.0; ledger.save(defaults)
        lastRun = now; lastSent = outcome.1.sent
        note = outcome.1.off ? "Team memory đang tắt cho team của bạn, hoặc Studio này chưa nhận lời sửa." : ""
    }
}

/// What team memory handed a Piagent conversation: the briefs Piagent kept
/// in the session file (custom entries "agent-watch-memory"), read locally.
public struct PiMemoryBrief: Equatable, Sendable {
    public let at: Date?
    public let text: String
    public let facts: Int
}
public struct PiSessionMemory: Equatable, Sendable {
    public var sessionID: String?
    public var cwd: String?
    public var briefs: [PiMemoryBrief] = []
}
public enum PiSessionMemoryReader {
    public static let entryType = "agent-watch-memory"
    public static func read(_ url: URL) -> PiSessionMemory {
        var out = PiSessionMemory()
        guard let data = try? Data(contentsOf: url, options: .mappedIfSafe), data.count <= 256 << 20 else { return out }
        let iso = ISO8601DateFormatter(); iso.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        for line in data.split(separator: UInt8(ascii: "\n")) {
            guard line.count < 4 << 20, let obj = try? JSONSerialization.jsonObject(with: Data(line)) as? [String: Any] else { continue }
            let type = obj["type"] as? String
            if type == "session" {
                out.sessionID = obj["id"] as? String; out.cwd = obj["cwd"] as? String; continue
            }
            // appendCustomMessageEntry, or a custom message in a message entry.
            let entry: [String: Any]? = type == "custom_message" ? obj : (type == "message" ? obj["message"] as? [String: Any] : nil)
            guard let entry, entry["customType"] as? String == entryType else { continue }
            let text: String
            if let s = entry["content"] as? String { text = s }
            else if let parts = entry["content"] as? [[String: Any]] { text = parts.compactMap { $0["text"] as? String }.joined(separator: "\n") }
            else { continue }
            let facts = (entry["details"] as? [String: Any])?["facts"] as? Int ?? 0
            let at = (obj["timestamp"] as? String).flatMap { iso.date(from: $0) }
            out.briefs.append(PiMemoryBrief(at: at, text: text, facts: facts))
        }
        return out
    }
}

/// Studio's record of the same conversation (POST /v1/memory/served): how
/// often notes were handed over and how members rated them.
public struct MemoryServed: Decodable, Equatable, Sendable {
    public struct Item: Decodable, Equatable, Sendable {
        public let kind: String, id: String
        public let title: String?
        public let times, helpful, harmful, outdated: Int
        public let leftOut: Bool
        enum CodingKeys: String, CodingKey { case kind, id, title, times, helpful, harmful, outdated, leftOut = "left_out" }
    }
    public let serves, rated: Int
    public let items: [Item]
    public let judging: Bool
}

extension MemorySignalClient {
    /// Only a direct key reaches memory outside a Piagent run; nil otherwise.
    public func served(origin: StudioOrigin, key: String, repoKey: String, conversation: String) async -> MemoryServed? {
        guard StudioClient.validKey(key), conversation.wholeMatch(of: /^[A-Za-z0-9_-]{8,80}$/) != nil,
              let body = try? JSONSerialization.data(withJSONObject: ["repo_key": repoKey, "conversation_id": conversation]) else { return nil }
        var request = URLRequest(url: origin.url(path: "v1/memory/served"))
        request.httpMethod = "POST"; request.httpBody = body
        request.setValue("Bearer " + key, forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue(StudioClient.userAgent, forHTTPHeaderField: "User-Agent")
        guard let response = try? await transport.send(request, origin: origin), response.status == 200 else { return nil }
        return try? JSONDecoder().decode(MemoryServed.self, from: response.body)
    }
}
