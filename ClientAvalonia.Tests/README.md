# ClientAvalonia.Tests

Unit and integration tests for the Avalonia CnCNet client. All assertions are
pinned to **DXMainClient behavior** as the alignment baseline — every expected
value traces back to a DX source line, not to a guess.

## DX alignment baseline

Every test that asserts a DX-aligned contract is tagged with `[Trait("DXContract", "...")]`
and the constant lives in `Fixture/DxAliases.cs`. Update the table when DX moves.

| Contract ID | DX anchor | What it locks |
|-------------|-----------|---------------|
| `DX-GAME-FIELDS` | `DXGUI/Multiplayer/CnCNet/CnCNetLobby.cs:1519-1563` | GAME CTCP 13-field order: revision, gameVersion, maxPlayers, channel, displayName, flags, players, map, gameMode, tunnel(host:port), loadedGameId, skillLevel, mapHash |
| `DX-GAME-REJECT-COUNT` | same | field counts other than 13 (or 11 via legacy) are rejected |
| `DX-GAME-REVISION` | same `:1541` | revision must equal `ProgramConstants.CNCNET_PROTOCOL_REVISION` |
| `DX-GAME-FLAGS-DEFAULTS` | same `:1547-1551` | locked/passworded/closed/loaded/ladder defaults |
| `DX-GAME-NOTUNNEL-REJECT` | same `:1572-1589` | reject when `tunnels.Count == 0` |
| `DX-GAME-LEGACY-11` | same | R10 11-field fallback path |
| `DX-PASSWORD-SHA1-CHANNEL` | `DXGUI/Multiplayer/CnCNet/CnCNetLobby.cs:1052` | DX upstream: `SHA1(channelName)[..10]` |
| `MG-PASSWORD-SHA1-CHANNEL-ROOM` | MG `clientdx.exe` IL | MG actual: `SHA1(channelName + roomName)[..10]` ASCII |
| `DX-PORT-RANGE` | `Domain/Multiplayer/CnCNet/CnCNetTunnel.cs` bare `int.Parse` | DX accepts anything parseable; Avalonia adds 1–65535 (MG-Extension) |
| `DX-NAME-VALIDATOR` | `ClientCore/NameValidator.cs` | shared by DX and Avalonia — char set, length, first-char rules |
| `DX-IRC-CHANNEL-CASING` | DX `Channel.cs` JOIN preserves case, compare uses lower | mirrored by `CnCNetIrcChannelNames.Preserve/Normalize` |
| `DX-BOOTSTRAP-CWD` | `DXMainClient/PreStartup.cs:62-65` | DX uses exe path directly; Avalonia adds registry hint + CWD walk |
| `DX-REGISTRY-WRITE-GATE` | `DXMainClient/Startup.cs:432-436` | DX honors `WritePathToRegistry`; Avalonia early-bound repair bypasses it |

Where DX and MG diverge (only `DX-PASSWORD-SHA1-CHANNEL` vs `MG-PASSWORD-SHA1-CHANNEL-ROOM`),
tests are tagged `[Trait("Baseline","DX")]` or `[Trait("Baseline","MG-Binary")]` so the
divergence is explicit. `MG-Extension` traits mark features Avalonia adds beyond DX.

## Test categories

- **Easy** (`CnCNet/`, `IniUi/`, `Services/`): pure-logic classes, no IO. Run anywhere.
- **Medium** (`Core/`, `CnCNet/CnCNetTunnelListLoaderTests.cs`, password tests): need a
  TempGameRoot fixture or Windows registry access. Some are Windows-only (marked
  `[SkippableFact]` and skipped on non-Windows).
- **Integration** (`IniUi/LayoutEngineEndToEndTests.cs`, `Integration/ValidateModesTests.cs`):
  end-to-end INI → tree → layout via the real LayoutEngine, or subprocess invocation of
  `ClientAvalonia.exe --validate-*`. Tagged `[Trait("Category","Integration")]`.

## Running

```bash
# All unit tests (skip integration):
dotnet test ClientAvalonia.Tests/ClientAvalonia.Tests.csproj --filter "Category!=Integration"

# Integration tests only (requires ClientAvalonia.exe to be built first):
dotnet build ClientAvalonia/ClientAvalonia.csproj -c Debug
dotnet test ClientAvalonia.Tests/ClientAvalonia.Tests.csproj --filter "Category=Integration"

# Everything:
dotnet test ClientAvalonia.Tests/ClientAvalonia.Tests.csproj
```

> **Note:** when running outside CI, prefix the build/test commands with
> `-p:DisableGitVersionTask=true -p:GitVersion_MsBuildTask_Disabled=true` if the
> GitVersion submodule task fails in your environment.

## Test seams (production code changes)

Two test seams were added to `ClientAvalonia` (small, additive — no behavior change):

1. `InstallationRegistry.TryReadEarlyBoundInstallPath(string[] candidateKeys, bool)`
   and `TryRepairAllCandidates(string[] candidateKeys, string? knownGoodRoot)` —
   `internal` overloads that take the candidate key list as a parameter so tests
   can use unique random keys instead of touching the real `MomentOfGenesis` /
   `TiberianSun` / ... entries.
2. `ClientEnvironment.FindGameRoot(string startDirectory, string[]? registryCandidates)` —
   `internal` overload with the same purpose.

`ClientAvalonia.csproj` declares `<InternalsVisibleTo Include="ClientAvalonia.Tests" />`
so the seams are reachable.

## Blind spots (not covered — by design)

These areas are documented in the plan but **not tested** because they're either
non-deterministic or require a real IRC server:

- `MainWindow.axaml.cs` CnCNet event subscriptions (no VM mediation → unit-test-unreachable)
- `CnCNetSession.Instance` IRC timers / threads / ThreadPool (need real network)
- `ProgramConstants.IsInGame` cross-thread read/write (data race, not unit-testable)
- Real CnCNet IRC server interaction

For these, see `MainWindow.axaml.cs` and `CnCNetSession.cs` directly.
