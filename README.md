# Ambient Theme Controller

Android as an **external ambient-light sensor** for Windows 11: measure room brightness in lux, recommend light/dark themes, and optionally switch **Windows system and application themes together**.

> **Project status:** the Windows side of the Free MVP is implemented and covered by automated tests ([windows/](windows/README.md)). The Android app is not finished, and nothing has been validated on physical phones yet. Not ready for end users.

## What has been validated

- **Transport (early prototype on real devices, since removed from the repository):** a local bidirectional WebSocket between Android (Termux/Python server) and Windows 11 (PowerShell/.NET client) over a Windows-created Wi-Fi hotspot, with simulated 5/25/50/100/200 lux samples, without root/admin.
- **Windows implementation (automated tests on Windows 11):** versioned protocol and validation, local settings and DPAPI-protected credentials, recommendation engine, automation state machine, joint apps+system theme writes with external-change detection, and a certificate-pinned WSS client exercised against a simulator of the Android side.

**Still unvalidated:** physical light-sensor acquisition, Android background behavior, pairing and discovery on real phones, and the visual result of switching the real Windows theme. See the open issues.

## Planned architecture

- **Free local-first:** Flutter + Android native light sensor as WebSocket server; .NET/Avalonia Windows client initiates communication, processes sensor status, thresholds and recommendations, and applies the theme.
- **Discovery:** mDNS/DNS-SD, QR pairing invitation, manual endpoint fallback; Windows/Android hotspot guidance. Neither QR nor WebSocket bypasses blocked networking policies.
- **Automation safety:** suspension and notification when measurements become unreliable; reconnection/recovery only when readings stabilize; manual pause and external theme changes override automatic recovery.
- **Premium (future):** optional remote connectivity via RelayControl, with actual authenticated message relay in addition to signaling when direct connections are blocked. Planned **last**, not a dependency of Free.

## Documentation

- [Windows implementation](windows/README.md)
- [Local protocol v1](docs/protocol/protocol-v1.md)
- [Architecture / design](docs/superpowers/specs/2026-10-22-ambient-theme-controller-design.md)
- [Architectural decisions (ADRs)](docs/adr/2026-10-22-ambient-theme-controller-adrs.md)
- [Brainstorm reference](docs/brainstorm/2026-10-22-ambient-theme-controller.md)
- [Licensing boundaries](LICENSING.md)

## Licensing and distribution

**Open Core:** The public Free core and future public Windows app are licensed under **Apache-2.0** ([LICENSE](LICENSE)). Any future proprietary Premium-only modules will use separate terms ([LICENSE-PREMIUM.md](LICENSE-PREMIUM.md)) and remain in private repositories. The Open Source core is **still Apache-2.0 when reused inside Premium**.

Free is intended to be buildable from GitHub and distributed through F-Droid, subject to F-Droid inclusion/build/signature checks. The future Premium edition is intended for paid, one-time purchase through Google Play. The same Android application ID, cross-store signature, and lossless in-place updates are **goals to be verified**, not current claims.

No distributable Android or Windows binaries are available from this repository yet.
