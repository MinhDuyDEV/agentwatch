import XCTest
@testable import AgentWatchCore

private actor SignalTransport: StudioHTTPTransport {
    var replies: [StudioHTTPResponse]
    var requests: [URLRequest] = []
    init(_ replies: [StudioHTTPResponse]) { self.replies = replies }
    func send(_ request: URLRequest, origin: StudioOrigin) async throws -> StudioHTTPResponse {
        requests.append(request)
        guard !replies.isEmpty else { throw StudioError.offline }
        return replies.removeFirst()
    }
}

private func prompt(_ text: String, source: SessionSource = .cli, folder: String = "/work/shop", session: String = "8d7f0f4e-1111-4222-8333-444455556666", at: TimeInterval = 0) -> PromptRecord {
    PromptRecord(id: "\(text.hashValue)-\(at)", timestamp: Date(timeIntervalSince1970: 1_790_000_000 + at), projectSlug: "-work-shop", projectDisplay: folder,
                 sessionUuid: session, text: text, score: PromptScorer.score(text), source: source)
}

final class MemorySignalsTests: XCTestCase {
    func testOnlyAMembersStandingWordsAreCandidates() {
        XCTAssertEqual(MemorySignals.candidate("không phải npm, luôn dùng pnpm trong web"), "không phải npm, luôn dùng pnpm trong web")
        XCTAssertNotNil(MemorySignals.candidate("From now on run go test ./... before you commit"))
        XCTAssertNil(MemorySignals.candidate("fix the checkout test"))
        XCTAssertNil(MemorySignals.candidate("ok"))
        // Injected context is not the member's words; credentials never leave.
        XCTAssertNil(MemorySignals.candidate("<system-reminder>Always do X</system-reminder>"))
        let redacted = MemorySignals.candidate("never print the key, api_key=sk-abcdefghijklmnop1234")
        XCTAssertNotNil(redacted); XCTAssertFalse(redacted!.contains("sk-abcdefghijklmnop1234"))
        XCTAssertEqual(MemorySignals.candidate("always " + String(repeating: "a", count: 3000))?.count, MemorySignals.maxText)
    }

    func testRepositoryKeysMatchPiagent() {
        XCTAssertEqual(MemorySignals.repoKey(remote: "https://github.com/Acme/Shop.git"), "github.com/acme/shop")
        XCTAssertEqual(MemorySignals.repoKey(remote: "git@github.com:acme/shop.git\n"), "github.com/acme/shop")
        XCTAssertEqual(MemorySignals.repoKey(remote: "ssh://git@gitlab.example.com:2222/team/app.git/"), "gitlab.example.com/team/app")
        XCTAssertEqual(MemorySignals.repoKey(remote: "https://user:token@github.com/acme/shop"), "github.com/acme/shop")
        for bad in ["", "/Users/me/repo", "file:///tmp/repo", "https://github.com/"] { XCTAssertNil(MemorySignals.repoKey(remote: bad), bad) }
    }

    func testTheClientPostsTheSignalAndReadsOff() async throws {
        let transport = SignalTransport([StudioHTTPResponse(status: 200, contentType: "application/json", body: Data(#"{"status":"queued"}"#.utf8)),
                                         StudioHTTPResponse(status: 204, contentType: "", body: Data()),
                                         StudioHTTPResponse(status: 200, contentType: "application/json", body: Data(#"{"status":"rate_limited"}"#.utf8))])
        let client = MemorySignalClient(transport: transport), origin = try StudioOrigin("https://studio.example")
        let signal = MemorySignalRequest(repoKey: "github.com/acme/shop", conversationID: "8d7f0f4e-1111-4222-8333-444455556666", text: "đừng dùng npm")
        let first = await client.send(origin: origin, key: "as_live_fixture_key", signal)
        XCTAssertEqual(first, .taken)
        let second = await client.send(origin: origin, key: "as_live_fixture_key", signal)
        XCTAssertEqual(second, .off)
        let third = await client.send(origin: origin, key: "as_live_fixture_key", signal)
        XCTAssertEqual(third, .rateLimited)
        let request = await transport.requests[0]
        XCTAssertEqual(request.url?.absoluteString, "https://studio.example/v1/memory/signal")
        XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer as_live_fixture_key")
        let body = try JSONSerialization.jsonObject(with: request.httpBody!) as! [String: String]
        XCTAssertEqual(body, ["repo_key": "github.com/acme/shop", "conversation_id": "8d7f0f4e-1111-4222-8333-444455556666", "text": "đừng dùng npm", "source": "agentwatch"])
        XCTAssertNil(MemorySignalRequest(repoKey: "r/x", conversationID: "bad id!", text: "x").conversationID)
    }

    func testAScanSendsEachCandidateOnceWithinTheDailyCap() async {
        let prompts = [
            prompt("luôn chạy go test ./... trước khi commit", at: 1),
            prompt("fix the bug", at: 2),
            prompt("never edit web/dist by hand", source: .piagent, at: 3),
            prompt("don't touch migrations that ran", folder: "/not/a/repo", at: 4),
            prompt("use pnpm instead of npm", source: .codex, at: 5),
        ]
        var ledger = MemorySignalLedger(), sent: [MemorySignalRequest] = []
        let repo: (String) -> String? = { $0 == "/work/shop" ? "github.com/acme/shop" : nil }
        var result = await MemorySignalScan.run(prompts: prompts, ledger: &ledger, repoKey: repo) { sent.append($0); return .taken }
        XCTAssertEqual(result.sent, 2); XCTAssertEqual(result.skipped, 1)
        XCTAssertEqual(sent.map(\.text), ["luôn chạy go test ./... trước khi commit", "use pnpm instead of npm"])
        result = await MemorySignalScan.run(prompts: prompts, ledger: &ledger, repoKey: repo) { sent.append($0); return .taken }
        XCTAssertEqual(result.sent, 0); XCTAssertEqual(sent.count, 2)
        // Memory off: stop at once, nothing recorded.
        var fresh = MemorySignalLedger()
        result = await MemorySignalScan.run(prompts: prompts, ledger: &fresh, repoKey: repo) { _ in .off }
        XCTAssertTrue(result.off); XCTAssertTrue(fresh.sent.isEmpty)
        // The daily cap.
        let many = (0..<30).map { prompt("always run make check \($0)", at: Double(10 + $0)) }
        var capped = MemorySignalLedger()
        result = await MemorySignalScan.run(prompts: many, ledger: &capped, repoKey: repo) { _ in .taken }
        XCTAssertEqual(result.sent, MemorySignals.maxPerDay)
    }
}

final class PiSessionMemoryTests: XCTestCase {
    func testBriefsAreReadFromTheSessionFile() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("pi-memory-\(UUID().uuidString).jsonl")
        defer { try? FileManager.default.removeItem(at: url) }
        let lines = [
            #"{"type":"session","version":3,"id":"8d7f0f4e-1111-4222-8333-444455556666","timestamp":"2026-10-21T08:00:00.000Z","cwd":"/work/shop"}"#,
            #"{"type":"message","id":"a","timestamp":"2026-10-21T08:00:01.000Z","message":{"role":"custom","customType":"agent-watch-memory","content":"Team rules: use cents","display":false,"details":{"facts":3}}}"#,
            #"{"type":"custom_message","customType":"agent-watch-memory","content":[{"type":"text","text":"Refreshed brief"}],"details":{"facts":1},"id":"b","timestamp":"2026-10-21T09:00:00.000Z"}"#,
            #"{"type":"custom_message","customType":"other","content":"x","id":"c"}"#,
            "not json",
        ]
        try lines.joined(separator: "\n").write(to: url, atomically: true, encoding: .utf8)
        let memory = PiSessionMemoryReader.read(url)
        XCTAssertEqual(memory.sessionID, "8d7f0f4e-1111-4222-8333-444455556666")
        XCTAssertEqual(memory.cwd, "/work/shop")
        XCTAssertEqual(memory.briefs.map(\.text), ["Team rules: use cents", "Refreshed brief"])
        XCTAssertEqual(memory.briefs.map(\.facts), [3, 1])
    }

    func testServedIsReadFromStudio() async throws {
        let body = #"{"serves":2,"rated":1,"judging":true,"items":[{"kind":"rule","id":"r1","title":"Cents","times":2,"helpful":1,"harmful":0,"outdated":0,"left_out":false},{"kind":"fact","id":"f1","times":1,"helpful":0,"harmful":1,"outdated":0,"left_out":true}]}"#
        let transport = SignalTransport([StudioHTTPResponse(status: 200, contentType: "application/json", body: Data(body.utf8)), StudioHTTPResponse(status: 403, contentType: "application/json", body: Data())])
        let client = MemorySignalClient(transport: transport), origin = try StudioOrigin("https://studio.example")
        let served = await client.served(origin: origin, key: "as_live_fixture_key", repoKey: "github.com/acme/shop", conversation: "8d7f0f4e-1111-4222-8333-444455556666")
        XCTAssertEqual(served?.serves, 2); XCTAssertEqual(served?.items.last?.leftOut, true)
        let refused = await client.served(origin: origin, key: "as_live_fixture_key", repoKey: "github.com/acme/shop", conversation: "8d7f0f4e-1111-4222-8333-444455556666")
        XCTAssertNil(refused)
    }
}
