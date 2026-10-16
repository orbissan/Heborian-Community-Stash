# Configuration

This guide is included next to `config.json` and `Heborian.dll` in the installation package. Edit `config.json` to change settings; this document is only a reference.

## Server

The active file is `user/mods/Heborian/config.json`, next to `Heborian.dll`. In the combined installation layout, prepend `SPT_Runtime/`.

The server creates this file with defaults on first startup if it does not exist. Existing configuration is preserved during updates; missing options use defaults.

Stop the server, edit valid JSON, save, and restart. Use the exact property names shown below. JSON does not support comments: do not add // comments, trailing commas or extra description properties. Use `true` / `false` for switches, unquoted whole numbers for counts and prices, and double quotes for text. Unknown properties and invalid values can prevent the module from loading.

| Setting | Default | Purpose |
| --- | --- | --- |
| `MainMenuBanner.Enabled` | `true` | Show the Heborian main menu banner. |
| `MainMenuBanner.Title` | `"Heborian"` | Banner title; the mod version is appended automatically. |
| `CommunityWarehouse.Enabled` | `true` | Enable shared storage on this server. |
| `CommunityWarehouse.FoundInRaidOnly` | `false` | Require Found in Raid on every deposited item, including contents and attachments. Withdrawals are unaffected. |
| `CommunityWarehouse.Rows` | `32` | Active height, from 1 to 1024 rows. Width is fixed at 16. |

### AllowedDeposits

Each option below belongs inside `CommunityWarehouse.AllowedDeposits`.

| Category | Default | Examples / scope |
| --- | --- | --- |
| `Weapons` | `false` | Weapons |
| `Gear` | `false` | Armor, plates, backpacks, rigs, headwear and headsets |
| `Tools` | `true` | Tools and multitools |
| `Food` | `true` | Food and drinks |
| `Medicine` | `true` | Medical items |
| `Ammo` | `true` | Ammunition and ammo boxes |
| `Barter` | `true` | Barter items not assigned to a more specific category |
| `WeaponMods` | `false` | Weapon parts and attachments |
| `Keys` | `false` | Keys |
| `Money` | `false` | Currencies |
| `Containers` | `false` | Storage containers other than gear such as backpacks |
| `Other` | `false` | Items outside these categories |

Rules apply to new deposits, including the contents and attachments of an item. For example, a loaded weapon may also need `WeaponMods` and `Ammo` enabled. Insured, quest and manually locked items cannot be deposited, including those inside containers. Unlock protected items in the normal inventory first. Locked personal stacks also cannot receive merges from the community stash. Existing shared items remain withdrawable.

Changing a category to `false` does not delete existing shared items or prevent withdrawing them. Players still need space in their personal stash.

### Changing the height

`Rows: 2` creates an active area of 16 × 2 cells. Items extending below that boundary are hidden as whole items and cannot be accessed until the stash is tall enough again. Their positions and contents are preserved. Any visible cells occupied by a partially hidden item remain reserved.

## History

All options below are inside `CommunityWarehouse.History`.

| Setting | Default | Allowed values / effect |
| --- | --- | --- |
| `Enabled` | `true` | Record successful operations; `false` stops new records. |
| `Public` | `true` | Allow players to view history; `false` restricts access to server archive files. Does not enable recording by itself. |
| `RetentionDays` | `30` | Whole number from 1 to 3650; how long history is retained, in UTC calendar days. |


Add this object inside `CommunityWarehouse` in the server's `config.json`:

```json
"History": {
  "Enabled": true,
  "Public": true,
  "RetentionDays": 30
}
```

These defaults also apply when the object is absent from an existing configuration. `Enabled` controls recording of new operations; `Public` permits players to open the history window. With `Public: false`, recording continues, but the button is hidden and the API denies access; the server owner reads the archive files directly. `RetentionDays` accepts 1–3650 and uses UTC calendar days. Restart the server after changes. See [the history guide](HISTORY.md).

## Client settings

The client file is `BepInEx/config/local.heborian.client.cfg`:

- `[General]` → `Enabled`: enable the plugin.
- `[Modules.HeborianMenu]` → `Enabled`: enable the HEBORIAN tab.
- `[Modules.CommunityWarehouse]` → `Enabled`: enable the warehouse interface.

The warehouse interface requires the menu module. Restart the game after changing client settings. Client settings do not override the server's deposit rules or stash dimensions.

## Stored data

Shared items are persisted in `user/mods/Heborian/data/warehouse.json`, with backup/recovery files alongside it. Back up the entire `data` directory together with `user/profiles` while the server is stopped. Do not delete storage files to change configuration.

Starting with 1.10.2, the storage file retains the latest 1,024 completed operations. Older history is trimmed on the next successful write; stored items and any pending recovery operation are preserved. This history is not a permanent administrator audit log.

Starting with 1.11.0, the separate user-facing history is stored under `data/history/`. Its retention is controlled by `History.RetentionDays`, independently of the 1,024 internal recovery records. Backups of the entire `data` folder include both.

## Found in Raid deposits

Set `CommunityWarehouse.FoundInRaidOnly` to `true` in the server's `user/mods/Heborian/config.json` to accept only items marked Found in Raid. Default: `false` (also used when omitted).

This is an additional deposit restriction alongside `AllowedDeposits`. It applies to whole stacks, split deposits, merges into the shared stash, and every contained item/attachment. Missing or false `SpawnedInSession` does not qualify. Ineligible personal items appear disabled in the UI.

Existing community items are never removed or converted. Withdrawals (including partial withdrawals and merges into personal stacks) and organization within the community stash remain available under their usual rules. Stop the server before editing and restart afterwards. The installer does not overwrite your active config.

## Transfer fees

All options below are inside `CommunityWarehouse.TransferFees`.

| Setting | Default | Allowed values / effect |
| --- | --- | --- |
| `Enabled` | `false` | `true` charges fees; `false` makes transfers free regardless of configured prices. |
| `Currency` | `"RUB"` | `"RUB"` = roubles, `"EUR"` = euros, `"USD"` = dollars. One currency for both directions. |
| `Deposit` | `100` | Whole number from 0 to 2147483647, charged per successful deposit. `0` means free. |
| `Withdrawal` | `100` | Whole number from 0 to 2147483647, charged per successful withdrawal. `0` means free. |

For example, use `Enabled: true`, `Currency: "EUR"`, `Deposit: 0`, `Withdrawal: 50` for free deposits and a 50-euro withdrawal fee. These names and values must be placed in the JSON object shown below, with property names in double quotes.


Add this object inside `CommunityWarehouse` in the active server `user/mods/Heborian/config.json` (or `SPT_Runtime/user/mods/Heborian/config.json` in the combined layout):

```json
"TransferFees": {
  "Enabled": false,
  "Currency": "RUB",
  "Deposit": 100,
  "Withdrawal": 100
}
```

Restart the server after changes. Missing settings use these defaults, so existing servers remain free. Currency must be `RUB`, `EUR` or `USD`. Prices are whole numbers from 0 to 2147483647; 0 makes that direction free. Invalid settings stop the warehouse module instead of silently applying a different price.

One successful cross-inventory transfer incurs one flat fee, regardless of quantity or container contents. Partial stacks and cross-inventory merges are charged once. Internal arrangement, rotation, splitting and merging remain free. Rejected operations do not charge. Fees remove money from the game; there is no communal treasury.

Payment uses existing currency in the personal stash, including nested wallets and cases, and can combine multiple stacks. Equipment, locked money or money inside a locked container are not used. The transferred item and all its contents are excluded. For a merge into a personal currency stack, that receiving stack is also excluded; keep payment in another stack. Newly withdrawn money cannot pay for its own withdrawal. Payment does not depend on the AllowedDeposits currency category or Found in Raid setting.

The header continuously displays the server's deposit/withdrawal prices, with status messages beside the header controls. Each recorded paid transfer includes its actual fee and currency in history. Older/free events have no fee value. Recording still follows History.Enabled.

Fees and item changes use one durable transfer journal with before/after images. Recovery completes that same payment; it does not recalculate using current prices. Duplicate/stale requests do not charge again. If a storage error interrupts a journaled transfer, follow the server recovery message rather than retrying it.

Version 1.12.3 displays `YOUR STASH` with the actual personal stash width and height (for example, `YOUR STASH  10 x 64`). A separate light header cell beside it displays the currency symbol (₽, € or $) before the total personal-stash balance, including nested containers. The symbol follows the configured fee currency; no currency code is appended to the balance. It refreshes from the server after transfers and during regular refreshes, even when fees are disabled. Equipment and shared storage are excluded. The total includes locked money; payment eligibility can be lower when funds are locked or belong to the transferred item/receiving stack.

When transfer fees are enabled, the community stash header is followed by a separate light cell showing both prices with the configured currency symbol (for example, `Deposit: ₽ 100 | Withdrawal: ₽ 100`), then the HISTORY button. With fees disabled, the price cell is hidden and HISTORY moves next to the stash title.

## Complete default configuration

For reference when adding missing options. Do not replace a customized configuration unless you intend to reset its settings. The server generates these defaults when `config.json` is absent.

```json
{
  "MainMenuBanner": {
    "Enabled": true,
    "Title": "Heborian"
  },
  "CommunityWarehouse": {
    "Enabled": true,
    "FoundInRaidOnly": false,
    "AllowedDeposits": {
      "Weapons": false,
      "Gear": false,
      "Tools": true,
      "Food": true,
      "Medicine": true,
      "Ammo": true,
      "Barter": true,
      "WeaponMods": false,
      "Keys": false,
      "Money": false,
      "Containers": false,
      "Other": false
    },
    "Rows": 32,
    "History": {
      "Enabled": true,
      "Public": true,
      "RetentionDays": 30
    },
    "TransferFees": {
      "Enabled": false,
      "Currency": "RUB",
      "Deposit": 100,
      "Withdrawal": 100
    }
  }
}
```
