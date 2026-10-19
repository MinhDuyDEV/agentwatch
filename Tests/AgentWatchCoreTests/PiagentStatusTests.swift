import XCTest
@testable import AgentWatchCore

// Piagent's per-conversation state files: read newest per conversation from
// both Gateways' folders, skip what does not parse; alert when a
// conversation starts waiting on the member (also at the first reading) and
// when its turn ends (not at the first reading).
final class PiagentStatusTests: XCTestCase {
    private func status(_ id: String, _ state: String, kind: String? = nil, ended: String? = nil, at: String = "2026-10-20T01:00:00Z") -> PiagentStatus {
        PiagentStatus(conversation: id, title: "Fix refunds", project: "shop", company: true, state: state, kind: kind, ended: ended, since: at, updated_at: at)
    }

    func testReaderTakesNewestAcrossFoldersAndSkipsBadFiles() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("piagent-status-\(UUID().uuidString)", isDirectory: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        for folder in ["dashboard", "company"] {
            try FileManager.default.createDirectory(at: dir.appendingPathComponent("status/\(folder)"), withIntermediateDirectories: true)
        }
        let write = { (folder: String, name: String, value: PiagentStatus) in
            try JSONEncoder().encode(value).write(to: dir.appendingPathComponent("status/\(folder)/\(name).json"))
        }
        try write("dashboard", "a", status("a", "working", at: "2026-10-20T01:00:00Z"))
        try write("company", "a", status("a", "blocked", kind: "permission", at: "2026-10-20T01:00:05Z"))
        try write("company", "b", status("b", "done", ended: "done", at: "2026-10-20T00:59:00Z"))
        try Data("not json".utf8).write(to: dir.appendingPathComponent("status/company/c.json"))
        try Data(repeating: 32, count: 20_000).write(to: dir.appendingPathComponent("status/company/d.json"))
        let list = PiagentStatusReader.read(agentDir: dir)
        XCTAssertEqual(list.map(\.conversation), ["a", "b"])
        XCTAssertEqual(list.first?.kind, "permission")
    }

    func testAlertsOnWaitingAndOnEndings() {
        var tracker = PiagentStatusTracker()
        // First reading: an old ending is not replayed; a waiting conversation is.
        XCTAssertEqual(tracker.update([status("a", "done", ended: "done"), status("b", "blocked", kind: "question")]).map(\.kind), [.question])
        XCTAssertEqual(tracker.update([status("a", "working"), status("b", "blocked", kind: "question")]), [], "still the same question")
        XCTAssertEqual(tracker.update([status("a", "blocked", kind: "permission"), status("b", "working")]).map(\.kind), [.approval])
        let ends = tracker.update([status("a", "done", ended: "done"), status("b", "done", ended: "stopped")])
        XCTAssertEqual(ends.map(\.kind), [.done, .stopped])
        XCTAssertEqual(ends.first?.line, "Fix refunds · shop")
        XCTAssertEqual(tracker.update([status("a", "working")]).count, 0)
        XCTAssertEqual(tracker.update([status("a", "error", ended: "failed")]).map(\.kind), [.failed])
        XCTAssertEqual(tracker.update([status("a", "blocked", kind: "auth")]).map(\.heading), ["Agent cần bạn đăng nhập"])
        XCTAssertEqual(tracker.waiting().map(\.conversation), ["a"])
    }
}
