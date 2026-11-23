# UI redesign, animated splash and release pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Polished, consistent UI on Windows and Android with animated vector splash screens, an automatic dev/main
release pipeline, and no POC code.

**Architecture:** Windows keeps code-behind Avalonia windows; a shared `Styles/Theme.axaml` holds palette and card/chip
styles, and the logo is a reusable `LogoMark` control. Android keeps its single page, gains a `theme.dart` and the
native Android 12 splash. CI is one workflow with path filters and a branch-aware release job.

**Tech Stack:** .NET 10, Avalonia 12.1.6 (Fluent), xUnit; Flutter 3.47 / Material 3; GitHub Actions, `gh` CLI.

**Spec:** `docs/superpowers/specs/2026-11-22-ui-redesign-and-release-pipeline-design.md`

## Global Constraints

- Palette: amber `#F5A524`, amber soft `#FFD27A`, night `#141821`, surface `#1C212C`, card `#232A37`, ok `#3FB97F`,
  waiting `#5B9BF0`, paused `#F5A524`, problem `#E5534B`.
- No new NuGet or pub packages. `TreatWarningsAsErrors` stays on.
- User-facing text stays Portuguese and unchanged in meaning.

## Review Focus

- Window opened with `--tray`: no splash, window stays hidden.
- Lux unknown (`null`): gauge shows no marker and the hero shows "—", never `0 lx`.
- Thresholds where dark ≥ light can't be stored, but gauge maths must clamp a lux above the scale max (log scale to
  2000 lx).
- Android < 12 (minSdk 26): `windowSplashScreen*` attributes only in `values-v31`.
- Docs-only push to `main`: workflow does not run; mixed docs+code push does run.

---

### Task 1: StatusView tone and lux (Core, TDD)

**Files:** Modify `windows/src/AmbientTheme.Core/Presentation/StatusPresenter.cs`; Test
`windows/tests/AmbientTheme.Core.Tests/Presentation/StatusPresenterTests.cs`

**Interfaces — Produces:** `public enum StatusTone { Ok, Waiting, Paused, Problem }`;
`StatusView(..., double? Lux, StatusTone Tone)`.

- [ ] Test `Tone_reflects_automation_state`: Active → Ok; ManualPause → Paused; RecommendOnly → Paused; ApplyBlocked →
  Problem; Fatal → Problem; Reconnecting → Waiting. Test `Lux_is_exposed_only_in_session`: Active lux 42 → 42;
  Degraded lux 42 → null.
- [ ] Run `dotnet test windows/tests/AmbientTheme.Core.Tests --filter StatusPresenterTests` → compile FAIL.
- [ ] Implement, run again → PASS. Commit.

### Task 2: Windows theme, logo, splash and main window

**Files:** Create `windows/src/AmbientTheme.App/Styles/Theme.axaml`, `LogoMark.cs`, `SplashWindow.axaml(.cs)`;
Modify `App.axaml`, `App.axaml.cs`, `MainWindow.axaml(.cs)`, `Windows.cs`; Create `docs/brand/logo.svg`.

- [ ] Theme resources + classes `card`, `chip.ok|waiting|paused|problem`, `segment`, `h1`, `caption`.
- [ ] `LogoMark` (vector sun + rays + moon); `AppIcon` renders `LogoMark`'s geometry.
- [ ] `SplashWindow` with keyframe animations; `App` shows it unless `--tray`, then opens `MainWindow`.
- [ ] `MainWindow` hero + cards; `LuxGauge.Position(double lux) → double 0..1` log scale, unit-free.
- [ ] `dotnet build windows/AmbientTheme.slnx -c Release` → 0 warnings; run the app and screenshot. Commit.

### Task 3: Android theme, page and animated splash

**Files:** Create `android/lib/theme.dart`, `android/android/app/src/main/res/drawable/ic_splash_logo.xml`,
`avd_splash_logo.xml`, `values-v31/styles.xml`, `values-night-v31/styles.xml`; Modify `lib/main.dart`,
`res/drawable*/launch_background.xml`, `res/values*/styles.xml`.

- [ ] Widget test `renders hero and start button when stopped` in `test/widget_test.dart`.
- [ ] Implement page; `flutter test` and `flutter analyze` → clean. Commit.

### Task 4: Workflow and POC removal

**Files:** Modify `.github/workflows/release.yml`, `windows/AmbientTheme.slnx`, `windows/README.md`, `README.md`,
`docs/releases.md`; Delete `poc/`, `windows/tools/ThemePoc/`.

- [ ] Path filters, branch triggers, release job per spec; YAML parses.
- [ ] `dotnet test windows/AmbientTheme.slnx -c Release` → PASS. Commit.
