import XCTest
@testable import AgentWatchCore

/// `runtime = pi` is an added path. These tests keep the Piagent path's binding
/// and Terminal command byte for byte, and pin the Pi path's argv, environment
/// and directories. Everything lives under a disposable folder.
final class StudioManagedPiRuntimeTests: XCTestCase {
    let org = UUID(), owner = UUID(), team = UUID(), keyID = UUID(), instance = UUID(), epoch = UUID(), profileID = UUID()
    func configuration(runtime: String? = nil) throws -> (StudioProfile, StudioManifest) {
        let origin = try StudioOrigin("http://127.0.0.1:17921")
        let profile = try StudioProfile(origin: origin, id: origin.credentialSlotID(orgID: org, ownerID: owner, keyID: keyID, mode: .managed), connectionID: origin.profileID(orgID: org, ownerID: owner), keyID: keyID, credentialMode: .managed)
        let role = StudioHarnessRole(mode: "fixed", modelIDs: ["company-main"])
        let harness = StudioHarness(id: profileID, teamID: team, revision: 1, configuration: .init(main: role, research: role, review: nil))
        let user = StudioUser(id: owner, displayName: "Fixture", role: "member", active: true, version: 1, teamID: team)
        let manifest = StudioManifest(authority: StudioAuthority(instanceID: instance, epoch: epoch, generation: 1), harness: harness, runtime: runtime, schemaVersion: 2, revision: String(repeating: "a", count: 64), orgID: org, user: user, keyID: keyID, credentialMode: .managed, expiresAt: Date().addingTimeInterval(3600), refreshSeconds: 300, models: [], codexCatalog: .object([:]))
        return (profile, manifest)
    }
    func scratch() throws -> URL {
        let base = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".cache/watch-pi-runtime-" + UUID().uuidString)
        try FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: base) }
        return base
    }
    func write(_ text: String, _ file: URL, executable: Bool = false) throws {
        try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data(text.utf8).write(to: file)
        if executable { try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: file.path) }
    }
    func helper(_ base: URL) throws -> URL { let h = base.appendingPathComponent("agentwatch"); try write("#!/bin/sh\nexit 0\n", h, executable: true); return h }
    /// A bundle as piagent-bundle lays it out: Pi plus harness/sandbox/provider and manifest.json.
    func bundle(root: URL) throws {
        let dir = StudioManagedPiRuntime.bundleDirectory(root: root)
        try write("#!/bin/sh\nout=\"$PI_CODING_AGENT_DIR/dump\"\nenv | sort > \"$out.env\"\nprintf '%s\\n' \"$@\" > \"$out.argv\"\npwd > \"$out.pwd\"\n", dir.appendingPathComponent("pi/bin/pi"), executable: true)
        for name in ["harness", "sandbox", "provider"] { try write("export default {}\n", dir.appendingPathComponent("\(name)/index.js")) }
        var parts: [String: Any] = [:]
        for name in StudioManagedPiRuntime.parts {
            parts[name] = ["version": "1.0.0", "path": name, "sha256": try StudioManagedPiRuntime.hashDirectory(dir.appendingPathComponent(name))]
        }
        try Data(JSONSerialization.data(withJSONObject: ["schema": 1, "bundleVersion": "0.1.0", "piVersion": "1.3.0", "parts": parts])).write(to: dir.appendingPathComponent("manifest.json"))
    }
    func syncedRoot() throws -> (base: URL, root: URL, profile: StudioProfile) {
        let base = try scratch(), root = base.appendingPathComponent("ManagedPi")
        try bundle(root: root)
        let (profile, manifest) = try configuration(runtime: "pi")
        try StudioClientConfiguration.apply(StudioManagedPiRuntime.prepare(root: root, connection: profile, manifest: manifest, helper: helper(base)))
        return (base, root, profile)
    }

    func testRuntimeFieldDefaultsToPiagent() throws {
        XCTAssertEqual(StudioManagedRuntime.from(nil), .piagent)
        XCTAssertEqual(StudioManagedRuntime.from("piagent"), .piagent)
        XCTAssertEqual(StudioManagedRuntime.from("something-new"), .piagent)
        XCTAssertEqual(StudioManagedRuntime.from("pi"), .pi)
        let (_, plain) = try configuration()
        let encoded = String(decoding: try JSONEncoder().encode(plain), as: UTF8.self)
        XCTAssertFalse(encoded.contains("\"runtime\""), "an unchanged manifest encodes as before")
        XCTAssertNil(try JSONDecoder().decode(StudioManifest.self, from: Data(encoded.utf8)).runtime)
        let (_, pi) = try configuration(runtime: "pi")
        XCTAssertEqual(try JSONDecoder().decode(StudioManifest.self, from: JSONEncoder().encode(pi)).runtime, "pi")
    }

    func testDirectoryHashMatchesTheBundlesNodeImplementation() throws {
        let base = try scratch()
        for (path, text) in [("a.txt", "alpha"), ("sub/b.txt", "beta\n"), ("Zed/z.txt", "z"), ("_m", "m")] { try write(text, base.appendingPathComponent(path)) }
        XCTAssertEqual(try StudioManagedPiRuntime.hashDirectory(base), "7855bf1036f80fb9c73e557d5b4ff2ca72313675b25d1d3c5dd4f7a80d13970e")
    }

    /// The Piagent path: binding and Terminal command are exactly what they were
    /// before `runtime` existed (snapshot taken on the commit before this change).
    func testPiagentPathIsUnchangedWhateverTheRuntimeField() throws {
        var snapshots: [String] = []
        for runtime in [nil, "piagent", "unknown"] as [String?] {
            let base = try scratch(), agent = base.appendingPathComponent("agent"), sdk = base.appendingPathComponent("sdk"), platform = base.appendingPathComponent("platform")
            try write("#!/usr/bin/env node\nconsole.log(1);\n", platform.appendingPathComponent("scripts/piagent-studio.mjs"), executable: true)
            try write("#!/bin/sh\nexit 0\n", sdk.appendingPathComponent("dist/cli/index.js"), executable: true)
            try write(#"{"version":"0.87.1"}"#, sdk.appendingPathComponent("package.json"))
            try FileManager.default.createDirectory(at: agent, withIntermediateDirectories: true)
            let (profile, manifest) = try configuration(runtime: runtime)
            let plan = try StudioManagedConfiguration.prepare(directory: agent, connection: profile, manifest: manifest, helper: helper(base), runtimeRoot: platform, piExecutable: sdk.appendingPathComponent("dist/cli/index.js"))
            XCTAssertEqual(plan.edits.map { $0.file.lastPathComponent }, ["agent-watch-managed.json"])
            XCTAssertEqual(plan.edits[0].file.deletingLastPathComponent().path, agent.path, "the binding stays in the Pi directory the sync was given")
            try StudioClientConfiguration.apply(plan)
            let launch = try StudioManagedConfiguration.terminal(directory: agent, project: base, profileID: profile.id, launchDirectory: base.appendingPathComponent("launches"))
            let script = try String(contentsOf: launch.url, encoding: .utf8)
            let binding = String(decoding: plan.edits[0].after, as: UTF8.self)
            let pinned = try JSONSerialization.jsonObject(with: plan.edits[0].after) as? [String: Any] ?? [:]
            func normal(_ text: String) -> String {
                var out = text.replacingOccurrences(of: base.path, with: "<BASE>").replacingOccurrences(of: profile.id, with: "<PROFILE>")
                out = out.replacingOccurrences(of: #"[0-9a-f-]{36}\.command"#, with: "<UUID>.command", options: .regularExpression)
                if let node = pinned["node"] as? String { out = out.replacingOccurrences(of: node, with: "<NODE>") }
                if let sha = pinned["node_sha256"] as? String { out = out.replacingOccurrences(of: sha, with: "<NODESHA>") }
                return out
            }
            snapshots.append(normal(binding) + "\n----\n" + normal(script))
        }
        XCTAssertEqual(Set(snapshots).count, 1, "a manifest without, or with another, runtime opens exactly the same")
        XCTAssertEqual(snapshots[0], Self.piagentGolden)
    }
    static let piagentGolden = #"""
{
  "broker" : "<BASE>/agentwatch",
  "broker_sha256" : "306c6ca7407560340797866e077e053627ad409277d1b9da58106fce4cf717cb",
  "configuration_revision" : "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
  "entrypoint" : "<BASE>/platform/scripts/piagent-studio.mjs",
  "entrypoint_sha256" : "0728dcd9e81d836c886b1a2f9d201e0ee6806bc84c46bfa58642911a13242315",
  "model" : "agent-watch-auto",
  "node" : "<NODE>",
  "node_sha256" : "<NODESHA>",
  "origin" : "http://127.0.0.1:17921",
  "profile_id" : "<PROFILE>",
  "schema_version" : 1,
  "sdk_root" : "<BASE>/sdk"
}

----
#!/bin/sh
umask 077
/bin/rm -f -- '<BASE>/launches/<UUID>.command'
exec /usr/bin/env -i PATH=/usr/bin:/bin TERM=xterm-256color LANG=en_US.UTF-8 '<NODE>' '<BASE>/platform/scripts/piagent-studio.mjs' '--config' '<BASE>/agent/agent-watch-managed.json' '--project' '<BASE>'

"""#

    func testPiPathUsesTheBundlesOwnPiAndSeparateDirectories() throws {
        let (base, root, profile) = try syncedRoot()
        let plan = try StudioManagedPiRuntime.launch(root: root, profileID: profile.id)
        let bundle = StudioManagedPiRuntime.bundleDirectory(root: root)
        XCTAssertEqual(plan.executable, bundle.appendingPathComponent("pi/bin/pi").path)
        XCTAssertEqual(plan.arguments, ["--no-extensions", "-e", bundle.path + "/harness", "-e", bundle.path + "/sandbox", "-e", bundle.path + "/provider", "-e", "builtin:mcp"])
        XCTAssertEqual(Set(plan.environment.keys), ["PATH", "TERM", "LANG", "PI_CODING_AGENT_DIR", "PI_CODING_AGENT_SESSION_DIR", "AGENT_WATCH_MANAGED_BINDING"])
        XCTAssertEqual(plan.environment["PI_CODING_AGENT_DIR"], root.path + "/agent")
        XCTAssertEqual(plan.environment["PI_CODING_AGENT_SESSION_DIR"], root.path + "/ManagedSessions")
        XCTAssertEqual(plan.environment["PATH"], "/usr/bin:/bin", "no pi from PATH")
        for value in [plan.executable] + plan.arguments + Array(plan.environment.values) {
            XCTAssertFalse(value.contains("/.pi/") || value.contains("/.piagent"), value)
            XCTAssertFalse(value.hasPrefix("/usr/local") || value.hasPrefix("/opt/homebrew"), value)
        }
        XCTAssertTrue(FileManager.default.fileExists(atPath: root.path + "/ManagedSessions"))
        _ = base
    }

    func testPiPathDoesNotTouchThePiagentBindingOrItsDirectory() throws {
        let base = try scratch(), root = base.appendingPathComponent("ManagedPi"), agent = base.appendingPathComponent("agent")
        try FileManager.default.createDirectory(at: agent, withIntermediateDirectories: true)
        try write(#"{"schema_version":1,"old":true}"#, agent.appendingPathComponent("agent-watch-managed.json"))
        try write(#"{"providers":{}}"#, agent.appendingPathComponent("models.json"))
        let before = try snapshot(agent)
        try bundle(root: root)
        let (profile, manifest) = try configuration(runtime: "pi")
        try StudioClientConfiguration.apply(StudioManagedPiRuntime.prepare(root: root, connection: profile, manifest: manifest, helper: helper(base)))
        _ = try StudioManagedPiRuntime.launch(root: root, profileID: profile.id)
        XCTAssertEqual(try snapshot(agent), before)
        XCTAssertTrue(FileManager.default.fileExists(atPath: StudioManagedPiRuntime.bindingURL(root: root).path))
        XCTAssertNotEqual(StudioManagedPiRuntime.bindingURL(root: root).lastPathComponent, "agent-watch-managed.json")
    }
    func snapshot(_ dir: URL) throws -> [String: Data] {
        var out: [String: Data] = [:]
        for case let url as URL in FileManager.default.enumerator(at: dir, includingPropertiesForKeys: nil)! where !url.hasDirectoryPath { out[url.path] = try Data(contentsOf: url) }
        return out
    }

    func testHashMismatchRefusesToOpenAndSuggestsRepair() throws {
        let (base, root, profile) = try syncedRoot()
        let launches = base.appendingPathComponent("launches")
        try write("export default { tampered: true }\n", StudioManagedPiRuntime.bundleDirectory(root: root).appendingPathComponent("sandbox/index.js"))
        XCTAssertThrowsError(try StudioManagedPiRuntime.terminal(root: root, project: base, profileID: profile.id, launchDirectory: launches)) {
            XCTAssertEqual($0 as? StudioPiRuntimeError, .bundleMismatch(["hash-mismatch:sandbox"]))
            XCTAssertTrue($0.localizedDescription.contains("Sửa cài đặt"))
        }
        XCTAssertFalse(FileManager.default.fileExists(atPath: launches.path), "nothing was written to open")
        let (_, manifest) = try configuration(runtime: "pi")
        XCTAssertThrowsError(try StudioManagedPiRuntime.prepare(root: root, connection: profile, manifest: manifest, helper: helper(base))) { _ in }
    }

    func testBrokerOrPiChangeAfterSyncRefuses() throws {
        let (base, root, profile) = try syncedRoot()
        try write("#!/bin/sh\necho changed\n", base.appendingPathComponent("agentwatch"), executable: true)
        XCTAssertThrowsError(try StudioManagedPiRuntime.launch(root: root, profileID: profile.id)) { XCTAssertEqual($0 as? StudioPiRuntimeError, .bindingChanged) }
        XCTAssertThrowsError(try StudioManagedPiRuntime.launch(root: root, profileID: String(repeating: "f", count: 64)))
    }

    /// Runs the real Terminal command with a poisoned parent environment: the
    /// Pi process sees only the closed environment, and no key anywhere.
    func testKeyNeverReachesThePiProcess() throws {
        let (base, root, profile) = try syncedRoot()
        let command = try StudioManagedPiRuntime.terminal(root: root, project: base, profileID: profile.id, launchDirectory: base.appendingPathComponent("launches"))
        let script = try String(contentsOf: command.url, encoding: .utf8)
        for secret in ["as_live_", "as_device_", "as_run_", "SECRET"] { XCTAssertFalse(script.contains(secret)) }
        let process = Process()
        process.executableURL = command.url
        process.environment = ["PATH": "/usr/bin:/bin", "AGENT_WATCH_KEY": "as_live_SECRETSECRET", "OPENAI_API_KEY": "sk-SECRET", "HOME": base.path]
        try process.run(); process.waitUntilExit()
        XCTAssertEqual(process.terminationStatus, 0)
        let dump = root.appendingPathComponent("agent/dump")
        let env = try String(contentsOf: URL(fileURLWithPath: dump.path + ".env"), encoding: .utf8), argv = try String(contentsOf: URL(fileURLWithPath: dump.path + ".argv"), encoding: .utf8)
        XCTAssertFalse(env.contains("SECRET")); XCTAssertFalse(argv.contains("SECRET"))
        XCTAssertFalse(env.contains("AGENT_WATCH_KEY")); XCTAssertFalse(env.contains("OPENAI_API_KEY")); XCTAssertFalse(env.contains("HOME="))
        XCTAssertTrue(env.contains("PI_CODING_AGENT_SESSION_DIR=" + root.path + "/ManagedSessions"))
        XCTAssertEqual(argv.split(separator: "\n").first, "--no-extensions")
        XCTAssertEqual(try String(contentsOf: URL(fileURLWithPath: dump.path + ".pwd"), encoding: .utf8).trimmingCharacters(in: .whitespacesAndNewlines), base.path)
    }
}
