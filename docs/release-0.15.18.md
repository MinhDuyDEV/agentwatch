# AgentWatch 0.15.18

A company (managed) key can now select Pi as well as Piagent. For Pi, Agent
Watch writes one file, `agent-watch-studio.json`, in the Pi agent directory: a
pointer to the managed broker (profile id, Studio origin, broker path and its
SHA-256). It holds no key, device token or model, and Pi's `settings.json`,
`models.json` and extensions are never touched. Restore removes the file
through its own receipt. A Pi extension can use it to reach the Harness
without Piagent; the schema is documented in
`docs/studio-one-click-sync-design.md`.

Pi and Piagent in one folder are synced and restored separately for a company
key. The five-second stale watch also checks the broker pinned by the Pi
binding, so an app update rewrites it quickly. Direct-key behaviour is
unchanged.

The full Swift test suite and the signed universal app packaging are release
gates.
