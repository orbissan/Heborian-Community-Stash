# Heborian — Community Stash

**Your spare loot. Someone else's next raid.**

A shared inventory for **SPT 4.1.6**, designed for small communities, groups of friends, or just two players helping each other out.

Open the **HEBORIAN** tab to deposit supplies, withdraw what you need, and organize items with familiar inventory controls. The stash is saved on the server and shared by everyone using that server. There are no private group or clan inventories.

> **Early release:** bugs and unexpected behavior may occur. Back up your player profiles and shared stash together before updating. Feedback and bug reports are welcome.

## Features

- A persistent shared stash alongside your personal stash.
- Drag and drop, item rotation, stack splitting and merging.
- Familiar item icons, quantities, condition and Found in Raid indicators.
- Server settings for stash height and allowed deposit categories.

## Installation

Use a compiled installation package for **SPT 4.1.6**. GitHub's **Code → Download ZIP** contains source code, not an installable mod.

1. Close the game and server.
2. Copy the package's `SPT_Runtime/user/mods/Heborian` folder into your server's `user/mods` folder.
3. Copy `BepInEx/plugins/Heborian` into each player's game installation, keeping the included `assets` folder.
4. Start the server and game, then open **HEBORIAN** in the bottom menu.

Keep the server and all clients on the same mod version. Everyone must connect to the same server; use one running client per profile. This mod adds shared storage, not multiplayer networking. Compatibility with other SPT versions or multiplayer frameworks is not guaranteed.

## Configuration

Edit **`user/mods/Heborian/config.json` on the server**, beside `Heborian.dll`. With the combined folder layout, this is `SPT_Runtime/user/mods/Heborian/config.json`.

Under `CommunityWarehouse`:

- **`Rows`** sets the height (default **32**, allowed **1–1024**). Width is always **16** cells.
- **`AllowedDeposits`** enables or disables categories using `true` / `false`.

Disabled categories cannot be deposited, but items already stored can still be withdrawn. Reducing the height hides items beyond the new boundary without deleting them; increasing it reveals them again.

Stop the server before editing and restart it afterward. Edit **`config.json`**, not `config.example.json`.

See [all configuration options](docs/CONFIGURATION.md) and the [example configuration](config/server/config.example.json).

## Backups and support

With the server stopped, back up **`user/mods/Heborian/data` and `user/profiles` together**. Restoring only one side can duplicate or lose transferred items.

[Report a bug](https://github.com/orbissan/Heborian-Community-Stash/issues) with your mod/SPT versions, steps to reproduce, and relevant log excerpts. Remove personal information before sharing logs; do not upload full player profiles.

## Development

Current source version: **1.10.1**.

See [building and testing](docs/BUILDING.md) and [contributing](CONTRIBUTING.md). Game and SPT assemblies are not included; build against your own SPT installation.

## License

Released under the [MIT License](LICENSE). See [third-party notices](THIRD-PARTY-NOTICES.txt). Game assets and external dependencies are not relicensed by this project.
