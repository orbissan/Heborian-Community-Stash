# Community stash history — 1.12.1

Click **HISTORY** beside **COMMUNITY STASH**. The window shows player name with a profile icon first, followed by action, item name and actual quantity; local date/time is the last column. Events appear newest first, with 12 per page. Search matches player names, item names and names of contained items. The server stores time in UTC; the client displays it in the player's local time zone.

- **ALL TRANSFERS**: deposits and withdrawals.
- **DEPOSITS / WITHDRAWALS**: one direction only.
- **ARRANGEMENT**: moves, rotations, splits and merges within the community stash.
- **SEARCH** or Enter: apply the search text.
- **REFRESH**: return to the newest events. Paging keeps a fixed revision boundary so new operations do not shift the current result set.
- **X / Escape**: close the window and return to the inventory without resetting scroll positions.

History starts with new operations after upgrading. It does not reconstruct names or events from old internal transaction records. Item names are stored as provided by the server at the time of the operation. The table displays plain names without rich-text markup or ItemInfo star/stat decorations. Native template icons fit a uniform bordered 1x1 cell, preserving aspect ratio. The actual transferred count appears at the bottom right; the name and category are beside it. Rows are dark green for deposits, dark red for withdrawals, and neutral for arrangement events. The action text remains visible independently of color. Pages scroll with fixed-height rows. Previews represent the base item template, not its historical attachment configuration; contents and attachment details remain in the server archive. Missing templates leave the text history readable.

## Server configuration

Inside `CommunityWarehouse` in `user/mods/Heborian/config.json`:

```json
"History": {
  "Enabled": true,
  "Public": true,
  "RetentionDays": 30
}
```

Missing settings use those defaults. Existing configuration files are not overwritten by the package; add the object manually to customize it, preserving the other settings and valid JSON commas.

`Public: false` means server-owner access through files only. There is no hidden client administrator bypass. `Enabled: false` stops new recording and hides/denies the history UI/API, without deleting existing archive records. Previously committed queued records can still be archived during recovery.

Retention accepts 1–3650 UTC calendar days including today. Expired archive files are cleaned on subsequent archive activity/history access, not by a background timer. Retention never deletes inventory items. Restart after configuration changes.

## Storage and recovery

Each completed event is an atomic JSON file under `user/mods/Heborian/data/history/`. It includes the stable profile ID, nickname captured at the time, UTC timestamp, request ID, revision, action, item/template IDs, name and actual amount. Relevant events include source/target positions, orientation or contained-item metadata. Public API responses omit the technical profile/item/request IDs.

The warehouse commit persists the event in an outbox together with the new inventory state. Only then is it archived. A failed archive write leaves the committed event queued for retry; it does not undo a completed transfer or silently lose its history. Restarting replays the outbox idempotently. If archiving remains broken and 1,024 events accumulate, further mutations are blocked until the administrator repairs storage/restarts. History queries report an error while committed events remain unarchived rather than showing an apparently complete list.

Rejected requests, cancelled drags and simply picking up an icon do not create successful events. Merges between personal and shared stacks are shown as deposits/withdrawals of the actual moved quantity. Personal-to-personal merges do not appear in community history.

Back up the entire `data` directory together with player profiles while the server is stopped. This archive is operational history, not a tamper-proof security system: the server owner controls its files. Avoid publishing archive files containing player IDs without considering that information.

## Validation

Automated checks cover failed warehouse commits, archive failure/outbox recovery, repeated restart replay, filters, fixed-boundary paging, retention and public ID omission. The isolated SPT API test covers partial/full transfers, arrangement events, authoritative actor names, invalid requests, session checks, restart, private mode and disabled mode. Concurrent mutation tests verify one history event per committed operation.

The Unity window compiles against SPT 4.1.6. Visual layout, text input, Escape, paging and preservation of scroll position still need an in-game check; automated server checks do not validate those interactions.

Version 1.12.1 uses consistent two-unit gaps matching the window background. New events capture remaining/maximum medication, food/drink, resource or durability values beside the item name. Reduced values are red. Old events show quantity only because their condition was not recorded.

Condition counters use the same StaticIcons attribute sprites as the native Flea Market: HpResource, FoodResource, FuelResource, Durability and RepairResource. New key records store remaining uses (maximum minus uses consumed); repair kits store remaining repair resource. Existing 1.11.5 records retain their archived values. No proprietary icon files are redistributed.

Paid transfers (1.12.1+) display the charged amount and currency alongside the item category. These are captured at transfer time and survive later price changes. Free and legacy events have no fee field.
