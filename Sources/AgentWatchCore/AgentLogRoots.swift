import Foundation

public struct AgentLogRoots: Sendable, Equatable {
    public var claudeProjects: String
    public var claudeDesktop: String
    public var codexSessions: String
    public var codexArchived: String
    public var piSessions: String
    /// Piagent's own Pi home (since Piagent 1.45): its personal sessions live
    /// here, while stock `pi` keeps ~/.pi/agent. Control files stay in ~/.pi/agent.
    public var piagentSessions: String

    /// Every Pi session root, stock pi first, without duplicates.
    public var piSessionRoots: [String] {
        piSessions == piagentSessions ? [piSessions] : [piSessions, piagentSessions]
    }

    public init(home: String = NSHomeDirectory(), environment: [String: String] = ProcessInfo.processInfo.environment) {
        let claude = environment["CLAUDE_CONFIG_DIR"] ?? home + "/.claude"
        let codex = environment["CODEX_HOME"] ?? home + "/.codex"
        let pi = environment["PI_CODING_AGENT_DIR"] ?? home + "/.pi/agent"
        claudeProjects = claude + "/projects"
        claudeDesktop = home + "/Library/Application Support/Claude/local-agent-mode-sessions"
        codexSessions = codex + "/sessions"
        codexArchived = codex + "/archived_sessions"
        piSessions = environment["PI_CODING_AGENT_SESSION_DIR"] ?? pi + "/sessions"
        piagentSessions = (environment["PIAGENT_AGENT_DIR"] ?? home + "/.piagent/agent") + "/sessions"
    }

    public static var current: AgentLogRoots { AgentLogRoots() }
}
