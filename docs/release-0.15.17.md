# AgentWatch 0.15.17

Company helpers can run side by side. When Agent Studio 0.50.0 or later
advertises `helper_slots`, the managed broker holds up to three grants of one
helper role at once (slots 2 and 3 beside the role's first) and tells Piagent
1.46.1 or later with the `helper-slots` feature. Piagent asks for them only when
the member asks for several helpers of one role ("điều phối 3 subagent"), so
each one gets its own grant and Studio admits their requests in parallel.

With an older Studio nothing changes: the broker never sends a slot, and a
request for slot 2 or 3 is refused.

The full Swift test suite and the signed universal app packaging are release
gates.
