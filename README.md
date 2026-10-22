# Ambient Theme Controller

Android as an **external ambient-light sensor** for Windows 11: measure room brightness in lux, recommend light/dark themes, and optionally switch **Windows system and application themes together**.

> **Project status:** architecture and PoC documented; the application itself has **not been implemented** yet.

## What has been validated

A local bidirectional WebSocket proof of concept was completed with Android (Termux/Python server) and Windows 11 (PowerShell/.NET client). In a Windows-created Wi-Fi hotspot configuration, the Windows client received simulated 5/25/50/100/200 lux measurements and returned acknowledgments without root/admin privileges. **Physical light-sensor acquisition, background reliability, secure pairing and theme switching remain unvalidated.**

## Planned architecture

- **Free local-first:** Flutter + Android native light sensor as WebSocket server; .NET/Avalonia Windows client initiates communication, processes sensor status, thresholds and recommendations, and applies the theme.
- **Discovery:** mDNS/DNS-SD, QR pairing invitation, manual endpoint fallback; Windows/Android hotspot guidance. Neither QR nor WebSocket bypasses blocked networking policies.
- **Automation safety:** suspension and notification when measurements become unreliable; reconnection/recovery only when readings stabilize; manual pause and external theme changes override automatic recovery.
- **Premium (future):** optional remote connectivity via RelayControl, with actual authenticated message relay in addition to signaling when direct connections are blocked. Planned **last**, not a dependency of Free.

## Documentation

- [Architecture / design](docs/superpowers/specs/2026-10-22-ambient-theme-controller-design.md)
- [Architectural decisions (ADRs)](docs/adr/2026-10-22-ambient-theme-controller-adrs.md)
- [Brainstorm reference](docs/brainstorm/2026-10-22-ambient-theme-controller.md)
- [Licensing boundaries](LICENSING.md)

## Licensing and distribution

**Open Core:** The public Free core and future public Windows app are licensed under **Apache-2.0** ([LICENSE](LICENSE)). Any future proprietary Premium-only modules will use separate terms ([LICENSE-PREMIUM.md](LICENSE-PREMIUM.md)) and remain in private repositories. The Open Source core is **still Apache-2.0 when reused inside Premium**.

Free is intended to be buildable from GitHub and distributed through F-Droid, subject to F-Droid inclusion/build/signature checks. The future Premium edition is intended for paid, one-time purchase through Google Play. The same Android application ID, cross-store signature, and lossless in-place updates are **goals to be verified**, not current claims.

No distributable Android or Windows binaries are available from this repository yet.
