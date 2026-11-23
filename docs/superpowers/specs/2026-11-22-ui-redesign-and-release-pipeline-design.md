# UI redesign, animated splash and release pipeline — design

Approved in conversation on 2026-11-22.

## Intent

Make the Windows (Avalonia) and Android (Flutter) apps look like one polished product, open both with an animated
vector splash, and turn the CI workflow into an automatic release pipeline. Remove the proof-of-concept code now that
the real apps exist.

## Goals

- Shared visual identity: dark-first, amber "sun" accent, a sun/moon logo drawn only as vectors.
- Windows: Avalonia is already in use (12.1.6, Fluent); restyle it, no new UI libraries.
  - Hero card: live lux in large type, a gauge with the dark/light thresholds, automation state as a coloured chip,
    mode as a segmented control (Automático / Somente recomendar / Pausado).
  - Cards for Conexão (network, identity, sensor, recommendation), Luminosidade, Celular, Preferências.
  - Secondary windows (pairing, confirmation, toast) use the same card style.
- Android: Material 3 with the same palette; hero card with lux and status chips, step-by-step pairing card.
- Animated splash, vector only:
  - Source logo `docs/brand/logo.svg`.
  - Windows: borderless splash window drawn with Avalonia shapes, rays spin, moon glides across the sun, title fades
    in; shown for ~1.8 s at normal start, skipped with `--tray`.
  - Android: Android 12+ `windowSplashScreenAnimatedIcon` with an `animated-vector` drawable; older versions show the
    static vector on the launch background.
- Workflow `.github/workflows/release.yml`:
  - Runs on pull requests and on pushes (commits and merges) to `dev` and `main`, plus manual dispatch.
  - Skipped when only documentation changes (`docs/**`, `**/*.md`, `LICENSE*`, `NOTICE`).
  - `dev` push → GitHub pre-release `v0.1.<run>-dev`; `main` push → release `v0.1.<run>` marked latest.
  - Pull requests only build and test.
- Remove `poc/` and `windows/tools/ThemePoc`, and every reference to them.

## Non-goals

- No change to protocol, automation logic, or user-facing Portuguese wording (only layout and decoration).
- No production Android signing (still debug-signed, stated in release notes).
- No creation or push of the `dev` branch by this change.

## Approaches

- Windows UI: (a) restyle with Avalonia styles/resources — picked, keeps dependencies and code-behind; (b) add a
  third-party theme (Material.Avalonia, Semi) — rejected, new dependency for a single window; (c) MVVM rewrite —
  rejected, unrelated refactor.
- Android splash: (a) native Android 12 splash API with animated vector — picked, no new package; (b)
  `flutter_native_splash` — rejected, new dependency and generated assets; (c) Flutter-drawn animation only —
  rejected, the native blank window would still show first.

## Builder's calls

- `StatusView` gains `double? Lux` and `StatusTone Tone` (Ok, Waiting, Paused, Problem) so the UI can colour the
  automation chip and draw the gauge without parsing text. Covered by Core tests.
- Release tags use the shared run number, so a `dev` and a `main` release never collide.
