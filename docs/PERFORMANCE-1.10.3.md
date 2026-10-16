# Large inventory performance — 1.10.3

## Cause and changes

The expensive part of the measured transfer flow was the following inventory snapshot, not the deposit itself. For every stash root, the old snapshot path rebuilt an SPT lookup of the entire inventory to determine its size, traversed the inventory repeatedly to find children, and repeated those searches for transfer eligibility. This made large inventories disproportionately expensive.

The new path builds a parent index once per snapshot, traverses each item's descendants using that index, and passes just that complete tree to SPT's native size calculation. Insurance membership uses a request-local set. Nothing is cached across requests, so moves, attachments, quantities and deposit permissions are evaluated from current server state. Mutation validation, revision checks, profile persistence and the recovery journal remain in place.

On the client, new snapshots reconcile existing native item views. Unchanged views and grid textures are retained; removed/changed items are replaced, and new visible items are added. Comparison includes quantities, positions, rotation, permissions, Found in Raid, condition and nested item data. Personal inventory virtualization and retention of the dragged source view continue to apply.

After an operation, the panel keeps its previous views while waiting for the authoritative snapshot, with item interaction disabled. This avoids clearing/recreating all icons between responses without allowing a second gesture against an obsolete revision. No optimistic ownership changes are introduced.

## Local measurements

SPT 4.1.6, isolated server on this workstation, synthetic 1×1 ammunition stacks, empty shared stash at the start of each size scenario. Three warm samples per scenario. Timings include the local HTTP round trip, decompression and parsing in the PowerShell harness; they do not measure Unity frame time or internet latency.

| Personal stash roots | Snapshot after deposit, 1.10.2 | Snapshot after deposit, 1.10.3 |
| --- | ---: | ---: |
| 100 | 28 ms | 15 ms |
| 500 | 211 ms | 38 ms |
| 1,500 | 682 ms | 97 ms |
| 3,000 | 2,334 ms | 192 ms |

At 3,000 items, this snapshot step was approximately 12 times faster. The separately measured deposit averaged 129 ms before and 175 ms after in these small samples; deposit latency itself was not established as improved. It still includes durable profile/storage writes. This is not a promise of a particular in-game end-to-end latency.

## Validation and limits

- Complete snapshot parity against 1.10.2 for two copied profiles, with categories enabled and disabled, nested container contents and folded equipment.
- Client comparison tests cover unchanged snapshots and changes to quantity, position, rotation, permissions, Found in Raid and nested contents.
- Runtime regression checks cover snapshot coordinates, concurrent withdrawals, deposit restrictions, composite items, large currency splits and stack merges.
- The client UI changes compile and pass helper/binding checks, but still need an in-game check of drag/drop, scroll position, changed item graphics and reconnect/error handling.
- Network payload still contains the complete snapshot. Very large container trees and a densely populated shared grid can still be expensive. Shared views are reused but are not yet virtualized.

The earlier concurrency harness also exposed an upstream SPT `EventOutputHolder` duplicate-key exception when it sent multiple simultaneous first-time requests from the same profile. That burst does not follow the game's per-profile inventory queue. The harness now uses two concurrent players, with one outstanding mutation per profile, for 48 rounds/96 requests. This does not establish support for multiple clients sharing one profile.

Measurement files and runtime logs stay under `test-results/` and are excluded from distribution. This build includes the 1.10.2 audit fixes and is prepared locally; it is not automatically installed or published.
