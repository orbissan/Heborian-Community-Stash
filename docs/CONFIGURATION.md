# Configuration

## Server

The active file is `user/mods/Heborian/config.json`, next to `Heborian.dll`. In the combined installation layout, prepend `SPT_Runtime/`.

The server creates this file with defaults on first startup if it does not exist. `config.example.json` is only a reference. Existing configuration is preserved during updates; missing options use defaults.

Stop the server, edit valid JSON, save, and restart. Use the exact property names shown in the [example](../config/server/config.example.json). Unknown properties and invalid values can prevent the module from loading.

| Setting | Default | Purpose |
| --- | --- | --- |
| `MainMenuBanner.Enabled` | `true` | Show the Heborian main menu banner. |
| `MainMenuBanner.Title` | `"Heborian"` | Banner title; the mod version is appended automatically. |
| `CommunityWarehouse.Enabled` | `true` | Enable shared storage on this server. |
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

Rules apply to new deposits, including the contents and attachments of an item. For example, a loaded weapon may also need `WeaponMods` and `Ammo` enabled. Insured and quest items cannot be deposited, including those inside containers.

Changing a category to `false` does not delete existing shared items or prevent withdrawing them. Players still need space in their personal stash.

### Changing the height

`Rows: 2` creates an active area of 16 × 2 cells. Items extending below that boundary are hidden as whole items and cannot be accessed until the stash is tall enough again. Their positions and contents are preserved. Any visible cells occupied by a partially hidden item remain reserved.

## Client

The client file is `BepInEx/config/local.heborian.client.cfg`:

- `[General]` → `Enabled`: enable the plugin.
- `[Modules.HeborianMenu]` → `Enabled`: enable the HEBORIAN tab.
- `[Modules.CommunityWarehouse]` → `Enabled`: enable the warehouse interface.

The warehouse interface requires the menu module. Restart the game after changing client settings. Client settings do not override the server's deposit rules or stash dimensions.

## Stored data

Shared items are persisted in `user/mods/Heborian/data/warehouse.json`, with backup/recovery files alongside it. Back up the entire `data` directory together with `user/profiles` while the server is stopped. Do not delete storage files to change configuration.
