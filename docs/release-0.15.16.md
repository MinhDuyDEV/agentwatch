# AgentWatch 0.15.16

Piagent 1.46 runs from its own Pi home, `~/.piagent/agent`, so the member's
own `pi` in `~/.pi/agent` stays stock. Agent Watch now reads Pi sessions from
both places: `~/.pi/agent/sessions` (stock pi) and `~/.piagent/agent/sessions`
(Piagent; `PIAGENT_AGENT_DIR` overrides). Session lists, cost, coaching scans,
prompt history and the live poller cover both, and the Privacy screen lists the
second folder.

Piagent's control files (runtime record, company binding, status) stay in
`~/.pi/agent`, so company mode and the Windows build are unchanged. Cache keys
include the new folder, so the first open after updating rescans once.

The full Swift test suite and the signed universal app packaging are release
gates.
