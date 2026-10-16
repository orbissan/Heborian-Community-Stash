# Container audit — 1.11.12

## Root cause found in the follow-up audit

1.11.11 protected drag/render exceptions but did not correct the profile deletion response. Full deposits emitted `items.del` for the root AND every descendant. That is unsafe for EFT's native response consumer.

Inspection of the installed SPT 4.1.6 game assembly shows `ProfileUpdatesHandler.ApplyStashChanges` snapshots all player items before applying deletions. `ManageDeletedItems` filters that snapshot by deleted IDs and invokes `ItemManipulator.DiscardWithoutRestrictions` on each match. It does not follow the order of the server's deletion array. Removing a container detaches its subtree. Attempting to discard a descendant afterwards calls `ContainerExtensions.GetOwner` on its detached parent, which throws `Trying to get owner of discarded item`. A failed response handler can leave our completion callback outstanding and both grids blocked awaiting confirmation.

The fix emits ONLY the deposited root ID in the client deletion delta. Server profile changes, the durable journal and warehouse storage still include the complete tree. Withdrawal still sends every item needed to reconstruct the tree. Empty containers and individual items use the same single-root response. Merely reversing the deletion array would not fix the native enumeration order.

The client now logs the outgoing request and completion callback. If completion has not arrived after 20 seconds, it displays an explicit confirmation-wait message and records the request ID. It does not blindly retry or unlock a potentially desynchronized profile.

## Regression evidence

`tools/Test-NativeDeletion.ps1` uses inert objects from the ORIGINAL installed game assembly and executes its real `ContainerExtensions.GetOwner` implementation. It simulates the detach step from the native deletion pass, without launching Unity or touching a profile. The old root-plus-descendants protocol fails at depths 2 and 4; root-only deletion succeeds. A single item also succeeds. This is a targeted native traversal reproduction, not a complete Unity UI test.

`tools/Test-ContainerRuntime.ps1` checks 16 container templates in empty/partial/full states (48 cases), with root-only client deltas and removal of ALL descendants from the persisted server profile. It checks exact IDs, states, positions, nested containers, multiple grids, stale/invalid requests and a two-player handoff. It also reproduces the reported sequence using the ordinary SPT `Move` API: deposit empty wallet, withdraw it, insert 20 roubles, deposit the filled wallet, withdraw it again.

`tools/Test-CategoryRuntime.ps1` checks weapon trees, deposit restrictions and journal recovery. Its earlier assertion that every descendant should appear in `items.del` was incorrect and has been replaced with the root-only client contract. Full server tree persistence remains checked.

The latest inspected client log confirms 1.11.11. It has startup profile errors but no exception stack for the final transfer. The server screenshot shows the last deposit committed at revision 534, followed by no warehouse refresh; that is consistent with an outstanding native completion callback. The specific UI session was not replayed inside Unity. An in-game check with 1.11.12 remains necessary, especially with third-party client mods.

## Previous UI hardening

### 1.11.11 findings

Client drag setup, movement, per-frame updates and drop handling previously had no exception boundary. A failure after the drag state was assigned could leave `IsDragging` true and both ScrollRects disabled. Snapshot processing then continued to be skipped. Drag failures now cancel the gesture, clear its logical state before visual cleanup, restore scrolling, show an error and log the exception. No automatic transfer retry is introduced.

A failure to construct one native item tree previously propagated into the entire inventory renderer (including when scrolling into a new row during a drag). Failed items now keep a disabled, occupied placeholder; healthy items remain usable. The placeholder cannot be dragged, quick-transferred or used as a merge target. The Refresh button rebuilds it so repaired data can be rendered again. Native previews continue to use cloned snapshot data.

The inspected live log identifies client 1.11.9 and reports malformed profile data during initial deserialization: missing parents and overlapping ammunition stack-slot positions. These errors are evidence of invalid data, not proof that a particular warehouse transfer caused it. No live profile was rewritten. The exact reported freeze was not reproduced in Unity.

## Automated coverage

`tools/Test-ContainerRuntime.ps1` runs against the isolated SPT server on port 6975, using copied profiles and restoring fixture state afterwards. It tests 16 actual templates in three states (empty, partial and full): wallet, money case, key tool, ammunition case, medicine case, food case, item case, multi-grid Paratus backpack, magazine case, weapon case, grenade case, injector case, keycard holder, document case, Gamma container and tactical rig.

Checks include complete deposit/withdraw response deltas; exact descendant IDs, templates, upd state, parents, slots and internal positions after a roundtrip; stale request rejection; invalid placement rejection; multiple grids; and a backpack containing a wallet containing currency. The nested case is also passed between two profiles and returned. Grid contents are selected against installed template filters.

`tools/Test-CategoryRuntime.ps1` additionally checks complete weapon attachment trees, category restrictions, and pending-journal recovery before and after profile saving in both directions. `verify.ps1` covers storage, atomic commits, history, packing, stack rules, native bindings and package contents.

## Limits and in-game confirmation

The API simulations do not execute Unity event dispatch, native profile-delta application or third-party client mods. They cannot prove every modded container or every corrupted profile is safe. No claim of reproducing the user's exact freeze is made.

After installing the client build, verify empty and filled wallet/case/backpack transfers, rotation, dragging while scrolling, and a second player withdrawing a nested container. Verify that a known invalid item's placeholder does not prevent dragging a healthy item and that Refresh can rebuild repaired data. If an operation remains pending or the native game reports inventory errors, capture the current BepInEx and EFT error logs before restarting.
