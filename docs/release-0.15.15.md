# AgentWatch 0.15.15

The macOS history cache tracks the estimated memory cost of each cached file
once, avoiding repeated walks over every cached prompt on insertion. Replacement
and eviction still enforce the same 128-entry and 32 MiB estimated limits.
SQLite persistence, parsing, scoring and token accounting are unchanged.

Regression checks cover replacing and shrinking entries, oversized entries,
count eviction, disk reopen, incremental refresh and exact scan totals. The full
Release suite and universal signed app packaging are release gates.

The isolated 1,000-insertion cache-bookkeeping fixture improved from a median
25.00 ms to 0.67 ms. This is a component measurement, not whole-app startup.
The native UI harness also builds with both legacy SwiftPM and Swift 6.4 product
layouts; no interactive native performance result is claimed.
