# Building and testing

## Requirements

- Windows and your own installed SPT **4.1.6** with BepInEx 5.
- PowerShell 7.6 on .NET 10 for the build fallback and verification.
- Optionally, the .NET 10 SDK for the normal SDK build.
- Windows PowerShell 5.1 for the client verification subprocess.

The installation root passed to `-SptRoot` must contain `SPT_Runtime`, `BepInEx`, and `EscapeFromTarkov_Data/Managed`. No game or SPT DLLs are distributed in this repository.

## Build installation packages

From the repository root:

```powershell
pwsh -File ./build.ps1 -SptRoot "D:\Games\SPT"
```

Without the SDK, use PowerShell 7.6's bundled compiler:

```powershell
pwsh -File ./build.ps1 -SptRoot "D:\Games\SPT" -UseBundledCompiler
```

The server targets .NET 10. The client targets netstandard2.1 and references Unity/Mono assemblies from the game, not the server runtime.

Outputs:

- `artifacts/<version>/`: staged client and server installation files.
- `dist/Heborian-<version>-SPT-4.1.6.zip`: installation package.
- `dist/Heborian-<version>-source.zip`: source package.

Building does not install the generated DLLs into the running game/server. Copy the staged mod folders after stopping both. Version numbers are defined in `src/Shared/BuildInfo.cs`.

## Verify

Build first, then run:

```powershell
pwsh -File ./verify.ps1 -SptRoot "D:\Games\SPT"
```

This checks configuration, storage operations, client helpers, native API bindings, assembly metadata and package contents. It writes test output under `test-results/`.

History verification includes commit/archive failures, durable outbox replay, pagination, retention and public response redaction. `tools/Test-HistoryRuntime.ps1` exercises the public/private/disabled history modes on the isolated fixture server. Like the other runtime scripts, it is tied to local fixture paths and must not run against real player profiles.

Storage checks include simulated write failures, recovery and bounded operation history. `Test-AuditRuntime.ps1` covers bonus stash rows; `Test-AuditConcurrency.ps1` sends concurrent full and partial withdrawals to the isolated fixture server. See [audit results and limitations](AUDIT-1.10.2.md).

`Test-PerformanceRuntime.ps1` measures snapshots/deposits for 100–3,000 synthetic stash items. `Test-SnapshotParity.ps1` compares complete API snapshots against the locally retained 1.10.2 build, including nested items, folded equipment and category permissions. Both use the same isolated fixture restrictions. Concurrency tests keep one outstanding inventory mutation per profile, matching the game's queue.

The additional HTTP/recovery scripts in `tools/Test-*Runtime.ps1`, `Test-*Recovery.ps1` and `Test-StashSnapshot.ps1` are development harnesses tied to a prepared local fixture server, fixed paths and test profiles. They are not standalone tests and the fixtures are not distributed. Adapt them only in an isolated test installation; do not point them at a live server or real profiles.

Automated checks do not replace in-game testing. Check both clients against the same test server, including simultaneous transfers, splitting large currency stacks, cancelling dialogs, scrolling and reconnecting.

## Source layout

- `src/Heborian.Server`: configuration, API routes and persistent warehouse operations.
- `src/Heborian.Client`: BepInEx plugin, menu and inventory UI.
- `src/Shared`: shared constants and version.
- `assets`: the mod's menu icon and cell texture.
- `tests`, `tools`: verification code and build helpers.
- `config/server`: default configuration example.

`docs/MODULES.txt` and `VALIDATION.txt` contain historical development notes, partly in Czech. Use the English README and configuration guide for current installation and settings.
