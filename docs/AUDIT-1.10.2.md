# Stability audit — 1.10.2

Date: 2026-10-16. Baseline: 1.10.1. Target: SPT 4.1.6.

This is a local audit build, not confirmation of a published release. Server tests ran against an isolated SPT instance with copied fixtures. Client checks compiled the plugin and verified helpers/native bindings; interactive Unity gameplay was not tested in this audit.

## Findings and fixes

| Priority | Finding | Resolution and evidence |
| --- | --- | --- |
| High | Failed warehouse writes could leave in-memory ownership, revision or pending state ahead of the persisted file. | Begin/commit/relocation now prepare a separate state and publish it in memory only after the atomic file replacement succeeds. Fault injection by exclusively locking the temporary file reproduced inconsistent memory on 1.10.1; the new checks pass. Restart recovery already worked in the baseline. This finding alone does not establish item duplication. |
| Medium | Whole and partial withdrawals ignored bonus rows in the player's stash, although the UI displayed them. | All three paths now use SPT's actual stash dimensions. An HTTP fixture with the base area full and two empty bonus rows failed on 1.10.1 with “Your personal stash has no free space.” Both withdrawal types succeed on 1.10.2. |
| Medium | Full operation history grew indefinitely and was repeatedly serialized/copied. | Keep the latest 1,024 completed records on successful writes. Regression checks verify pruning preserves items, revision semantics and the newest records. Pending recovery is retained. Global revision checks still reject stale requests after pruning. |
| Medium | Closing and reopening the tab could briefly reuse an old personal-stash snapshot. | Closing clears the cached snapshot and detaches the old refresh task. Code review and build verified; tab reopening still needs an in-game check. |
| Medium | Failed rendering could mark a snapshot as rendered and prevent an automatic retry for identical data. | Cache is committed only after successful rendering; a later snapshot can retry after clearing the previous rendering error. Manual Refresh already reset these fields. Code review and build verified; forced transient rendering failure still needs an in-game check. |

## Verification performed

- Build and standard `verify.ps1`: passed, including server storage/configuration checks, client helpers, SPT API bindings, plugin metadata and package contents.
- Storage fault injection: failed journal begin, failed commit and failed relocation preserve the prior in-memory state; restarting with a pending journal commits exactly once.
- Concurrency: 96 HTTP mutation requests in 12 batches across two profiles. Eight requests per batch attempted the same item at the same revision; exactly one committed. Alternating whole and partial withdrawals preserved all 120 ammunition units and unique ownership of every item ID. This is a bounded concurrency test, not a many-player capacity benchmark.
- Concurrent merging: exactly one same-revision operation committed and quantities were conserved. Also checked capacity, remainders, stale requests, recent request replay, incompatible Found in Raid state, self-merge and invalid quantities.
- Large stacks: rubles, dollars, euros and ammunition; selected-amount deposits, shared splits and withdrawals preserved quantities.
- Stash snapshot: real personal positions/dimensions, rotations, composite items and blocked items.
- Configuration: deposit-category changes allow withdrawal of existing items; weapons/container trees retain their children; shrinking rows hides items without deletion and growing restores them.
- Recovery fixtures: partial transfers, merges and complete weapon trees in both directions, with the profile at the before-save or after-save state. Restart recovery preserved quantities and IDs and committed once. These are prepared interruption states, not arbitrary power-cut testing.

Local evidence is in `test-results/audit-1.10.2/`, which is excluded from source packages because fixtures/logs may contain copied profile information. Tests restore their copied profiles/configuration/state on completion. The audit did not install into the live game or publish to GitHub.

## Remaining risks and limits

1. **Large shared inventories (medium, open):** the shared grid constructs views for all shared items, unlike the virtualized personal grid. A densely populated warehouse near the 1,024-row maximum could cause frame-time and memory problems. Measure this in Unity before advertising support for very large filled warehouses; shared-grid virtualization is a follow-up improvement.
2. **Network interruption (unverified):** disconnecting during a queued client operation relies on SPT callback/error handling. Test loss of connection before and after server commit, reconnect, and reopening the tab; do not assume a request timed out before it committed.
3. **Custom item failures (open):** a persistent native rendering failure for a modded item can still prevent the panel from rendering. Automatic retry helps transient failures but is not an item-level fallback.
4. **Durability boundary:** the warehouse journal and player profiles are separate files. The journal supports recovery of interrupted operations; it is not one atomic filesystem transaction covering both. Disk corruption, arbitrary power loss and every external mod interaction are not proven safe by these tests.
5. **Deployment boundary:** concurrency protection is for one SPT server process. Do not run multiple server processes against the same warehouse/profile files. Use one connected client per player profile. Other mods can mutate profile data outside this module's semaphore.

## Before public release

Test two actual game clients against the same isolated server: simultaneous withdrawal/merge, large currency splitting and cancellation, reopening after changing the normal stash, scrolling/rotation while dragging, disconnect/reconnect during transfer, and a densely populated shared warehouse. Back up warehouse data and profiles together before upgrading. These checks are still outstanding; the audit does not certify the mod as bug-free.
