# Ambient Theme Controller — Windows

Windows side of the Free MVP: it connects to the Android sensor over the local network, decides, and (only in
automatic mode) switches the Windows 11 app and system themes together. No account, cloud, administrator
rights or proprietary dependency. Licensed under Apache-2.0.

## Layout

| Path | Responsibility |
|---|---|
| `src/AmbientTheme.Core` | Pure, deterministic logic: protocol v1, measurement validation, settings, recommendation engine, automation coordinator, theme adapter logic, WSS client, pairing, reconnection. |
| `src/AmbientTheme.Windows` | Win32 edges only: registry values, `RegNotifyChangeKeyValue` watcher, contrast-theme probe, DPAPI secret store. |
| `src/AmbientTheme.App` | Avalonia shell: tray icon, status, modes, thresholds, pairing by QR + manual address, notifications. |
| `tools/SensorSimulator` | Plays the Android role of the protocol for tests and development. Its readings are tagged `SIMULATED`. |
| `tools/ThemePoc` | Reads, watches and (only on explicit command) changes the real Windows theme through the product's adapter. |
| `tests/` | xUnit tests. `Windows.Tests` uses the real registry APIs on a scratch key, never the real theme. |

Only the `AutomationCoordinator` may ask for a theme write. Network messages, the UI and ACKs cannot.

## Build, test, run

Requires the .NET 10 SDK on Windows 11.

```
dotnet test windows/AmbientTheme.slnx
dotnet run --project windows/src/AmbientTheme.App
```

Settings live in `%LOCALAPPDATA%\AmbientThemeController` (`settings.json`, DPAPI-protected `secrets/`).

### Trying it without a phone

```
dotnet run --project windows/tools/SensorSimulator
```

Type the simulator address (`127.0.0.1:8766`) in the app, click **Parear celular…**, paste the invite text shown
under the QR into the simulator as `invite <text>`, continue, and compare the fingerprint codes. Then send
`lux 5`, `status UNCERTAIN`, `drop`, `revoke <clientId>` and so on.

Simulated readings are refused by default. To let them drive the engine for development, start the app with
`ATC_ALLOW_SIMULATED=1`; the window shows a warning banner. **In automatic mode this changes your real theme.**

### Evidence for the theme PoC (issue #3)

```
dotnet run --project windows/tools/ThemePoc -- read
dotnet run --project windows/tools/ThemePoc -- watch
dotnet run --project windows/tools/ThemePoc -- roundtrip
```

`read` and `watch` never write. `roundtrip` switches the theme, waits 8 s and restores the original.

## Defaults (uncalibrated hypotheses, spec §6 and §19)

| Setting | Value |
|---|---|
| Presets (dark below / light above) | 60/150, 30/80 (default), 15/50 lx |
| Dwell before recommending or applying | 30 s |
| Cooldown between automatic changes | 10 min |
| Silence before freezing changes | 15 s |
| Confirmed failure (one notification) | 45 s |
| Stabilization after a new physical reading | 20 s |
| Reconnection backoff | 1, 2, 4, 8, 15, 30 s, ±20 % |

## Known limits

- Nothing here has been validated against a physical phone; the Android app is not finished.
- Discovery by mDNS is only an interface (`IDiscovery`); today the address is typed manually.
- The pairing flow (self-signed certificate, 64-bit comparison code) still needs a threat review.
- Visual refresh after a theme change relies on a `WM_SETTINGCHANGE` broadcast; Windows does not guarantee that
  every surface or third-party app repaints. With a contrast theme active the app never writes.
- "Forget phone" only removes the local credential; revoking this computer is done on the phone.
- No installer, signing or auto-start yet.
