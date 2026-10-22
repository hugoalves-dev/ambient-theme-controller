# Licensing — Ambient Theme Controller (Open Core)

The project has **two different licensing scopes**. It is **not** a claim that all source code is available under two interchangeable licenses.

| Component | License | Source/distribution |
|---|---|---|
| Public Flutter/Android Free core and open-source Windows app, when published here | **Apache License 2.0** (`LICENSE`) | Public GitHub and eligible F-Droid builds |
| Premium-only proprietary implementation | **Ambient Theme Controller Premium Proprietary Software License** (`LICENSE-PREMIUM.md`) | Private source repository; future Google Play Premium binary |
| Third-party libraries/assets | Their respective upstream licenses | Subject to dependency-specific notices/attributions |

## Key boundaries

- Copyright for the public Apache-2.0 components remains with their authors. Shipping them inside a paid app **does not turn them proprietary**.
- Proprietary Premium code **must not be committed** to the public repository or included as a build requirement for the Free flavor, including CI.
- The Premium license applies only to the separately developed proprietary additions; where the Premium binary includes Apache-2.0 source, Apache-2.0 remains applicable to that source.
- No proprietary license or Play Services runtime dependency may make the Free F-Droid build non-FLOSS.
- The F-Droid developer-signed/Reproducible Builds workflow, cross-store certificate continuity, and Free-to-Premium in-place migration are **goals pending validation**, not distribution guarantees.
- Licensing terms are distinct from Google's purchase entitlement and server-side authorization to access the future RelayControl relay. A signed binary alone is not proof of payment.

## Reference documents

- [Apache-2.0 (public core)](LICENSE)
- [Premium proprietary terms (future)](LICENSE-PREMIUM.md)
- [Consolidated design](docs/superpowers/specs/2026-10-22-ambient-theme-controller-design.md)
- [Architecture Decision Records](docs/adr/2026-10-22-ambient-theme-controller-adrs.md)

**Legal review:** The Premium terms are an initial draft and must be reviewed before commercial publication. They do not amend or restrict the Apache-2.0 license.
