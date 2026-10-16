# Heborian Community Stash 1.12.7 — SPT 4.1.6

A shared stash for everyone on your server, designed for small groups and cooperative play.

## Download
[Download the ready-to-install ZIP](https://raw.githubusercontent.com/orbissan/Heborian-Community-Stash/main/downloads/Heborian-1.12.7-SPT-4.1.6.zip)

## What's new since 1.10.1
- Transfer history with player names, item previews, quantities, condition, search and filters.
- Optional server-controlled deposit and withdrawal fees in roubles, euros or dollars. Fees are disabled by default and charged per successful transfer, regardless of stack size.
- Optional Found in Raid-only deposits. Existing items remain withdrawable when deposit rules change.
- Improved stash headers, currency balance display, native inventory sounds and menu background.
- Faster large-inventory handling and fixes for transferring filled containers.
- Stability fixes for concurrent inventory responses and protection for locked personal items.
- Configuration documentation included beside the server mod.

## Installation and update
1. Close the game and server. Back up player profiles and shared stash data together.
2. Download `Heborian-1.12.7-SPT-4.1.6.zip` above.
3. Copy its `SPT_Runtime/user/mods/Heborian` folder into the server's `user/mods` folder.
4. Copy `BepInEx/plugins/Heborian` into every player's game installation, including the `assets` folder.
5. Keep existing `config.json` and `data`. Start the server and game, then open the **HEBORIAN** tab.

Requires **SPT 4.1.6**. Update the server and all clients to the same mod version.

## Configuration
Edit `user/mods/Heborian/config.json` while the server is stopped, then restart it. See the included `CONFIGURATION.md` or [configuration guide](../docs/CONFIGURATION.md) for all settings.

**Early release:** bugs may occur. Automated build, verification and isolated server integration tests passed, including concurrent transfers, fees and container transfers. This does not guarantee compatibility with every mod combination or multiplayer framework.

SHA-256:
`1aa090166bd1afae1d8c46e346bcabfb789ab42e9892eb6e25010f031d776c9e`
