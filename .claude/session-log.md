## 2026-08-18

**Goal:** Update planning.md with current project state, then turn the remaining known gaps and unverified features into Trello tickets for tracking.

**Done:**
- Updated `planning.md` with a full "current state snapshot" section (§17) covering app identity, Data Sources screen, Results screen, data comparison mechanism, not-yet-built/deferred items, and a note flagging the progress-popup spinner change as unverified. Fixed stale cross-references in §6 and §11.
- Identified the Trello board in use ("Refactonauts") out of four candidate boards, confirmed the "ToDo" list and "Testing" label on it.
- Drafted and created 14 Trello cards on Refactonauts → ToDo, all assigned to Joseph Adams and labeled "Testing":
  - Build backlog (6): Exclusion rules (design+impl, deliberately kept as one catch-all card since scope isn't settled yet), CLI/headless mode, CSV/Excel/JSON export formats, Data tab UI polish, Column-resize memory, Elapsed-time display in progress popup.
  - Verification/QA (8): progress-popup spinner rendering, Cancel actually stopping in-flight SQL, per-table parallel comparison correctness, "Remember credentials" round-trip, HTML export accuracy, column resize/proportional scaling, Compare-button enablement logic, Data Sources↔Results toggle and Cancel-during-compare state.

**Decisions:**
- Exclusion rules are more complex than originally scoped (type-based defaults + per-table/column overrides + engine wiring) — deliberately collapsed into a single catch-all backlog card rather than three separate cards, to be split once the design is actually agreed.
- Trello tickets are split into two distinct groups (build backlog vs. verification/QA) per explicit user direction ("Both") — verification tickets exist for already-built features that haven't been click-tested yet, not just for net-new work.

**Left to do:**
- Everything on the 14 Trello cards, in priority order still to be decided by the user.
- The previously-flagged pending item: the `DataComparisonProgressWindow` spinner animation was written but not yet build-verified (app was locked during that edit) — now tracked as its own verification card.
- Longer-term planning.md items not yet turned into tickets or started: none outstanding — all known gaps are now represented on the board.

**Patterns noted:**
- When asked to draft external-system tickets (Trello), presenting the full draft list for review before creating anything worked well and matched the user's explicit "show me their contents" instruction — created only after an explicit "yes".
- When a proposed backlog item turns out to be more complex than first scoped, the user prefers a single catch-all card over several speculative sub-cards until the design is actually settled.
