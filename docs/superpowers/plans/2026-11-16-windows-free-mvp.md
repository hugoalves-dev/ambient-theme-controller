# Windows Free MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implementar o lado Windows do MVP Free (protocolo, persistência, motor, automação, tema, conexão, pareamento e shell Avalonia), com testes automatizados, mais um simulador do servidor Android para validar o contrato sem aparelho.

**Architecture:** `AmbientTheme.Core` (net10.0, puro e determinístico) contém protocolo, validação, motor, coordenador, lógica do adaptador de tema e conexão. `AmbientTheme.Windows` contém apenas as bordas Win32 (Registro, DPAPI, contraste). `AmbientTheme.App` é o shell Avalonia. Só o `AutomationCoordinator` autoriza escrita de tema.

**Tech Stack:** .NET 10, xUnit, Avalonia 12, QRCoder, Kestrel (somente em teste/simulador).

**Spec:** `docs/superpowers/specs/2026-10-22-ambient-theme-controller-design.md`

## Global Constraints

- Sem conta, nuvem, root ou administrador; nenhuma dependência proprietária (ADR-019).
- Android é servidor; Windows é cliente iniciador (ADR-002). Mensagem de rede nunca escreve no Registro (§4).
- `AppsUseLightTheme` e `SystemUsesLightTheme` sempre juntos, 0 = escuro, 1 = claro (ADR-011).
- Presets: 60/150, 30/80 (padrão), 15/50; dwell 30 s; cooldown 10 min (§19, hipóteses).
- Watchdogs: suspeita 15 s, falha confirmada 45 s, estabilização 20 s, backoff 1/2/4/8/15/30 s com jitter (§6).
- Heartbeat/`SENSOR_STATUS` nunca conta como leitura física; amostra simulada nunca aciona automação por padrão.
- Pausa manual e mudança externa de tema persistem e nunca são revertidas por reconexão (ADR-007/010).
- Segredos só via DPAPI; nunca em log, export ou mDNS.

## Limites deste plano (o que NÃO fica provado)

- Lado Android (Flutter/Kotlin): esta máquina não tem Flutter/Android SDK nem aparelho. Issues #2, #4, #5 e a metade Android da #8 permanecem abertas.
- Atualização visual real do Windows ao trocar o tema: os testes automatizados usam uma chave de Registro de teste; a evidência visual da #3 é colhida com `tools/ThemePoc` executado pelo usuário.
- mDNS no Windows: apenas a interface `IDiscovery`; implementação depende da PoC #5.

## Review Focus

1. Mudança externa de tema durante uma escrita própria em andamento: não deve ser absorvida como eco.
2. Arquivo de settings corrompido ou de schema mais novo: não sobrescrever silenciosamente.
3. Leitura reenviada com idade alta logo após reconexão: não libera estabilização.
4. Falha parcial de escrita (apps mudou, sistema não): sem loop de escrita nem rollback cego.
5. Credencial revogada: sem loop agressivo de reconexão e sem reativar automação.

---

## Tasks

### Task 1 — Protocolo e validação (issue #6)
`windows/src/AmbientTheme.Core/Protocol/` · testes em `windows/tests/AmbientTheme.Core.Tests/Protocol/`
- `MessageCodec.Encode(ProtocolMessage) : byte[]`, `MessageCodec.Decode(ReadOnlySpan<byte>) : DecodeResult` — limite 4096 bytes, `protocolVersion = 1`, erros `TOO_LARGE`, `MALFORMED`, `UNSUPPORTED_VERSION`, `UNKNOWN_TYPE`.
- Mensagens: `HELLO`, `AUTH`, `AUTH_RESULT`, `LIGHT_READING`, `SENSOR_STATUS`, `ACK`, `ERROR`, `PAIR_REQUEST`, `PAIR_APPROVE`, `PAIR_REVOKE`.
- `MeasurementValidator.Accept(Hello)` / `Validate(LightReading) : ValidationResult` — identidade pareada, sessão, sequência crescente, lux finito ≥ 0 e ≤ 200 000, idade do evento ≤ 10 s, fonte `SIMULATED` rejeitada salvo opção explícita.
- Documento do contrato: `docs/protocol/protocol-v1.md`.

### Task 2 — Persistência (issue #6)
`Core/Settings/` · `SettingsStore.Load() : SettingsLoadResult`, `Save(AppSettings)` (escrita atômica, `schemaVersion = 1`, leitura tolerante, backup de arquivo corrompido, recusa de schema mais novo), `SettingsExporter.Export/Import` sem segredos, `ISecretStore`.

### Task 3 — Motor (issue #7)
`Core/Engine/` · `Thresholds.TryCreate`, `ThresholdPresets`, `ThemeRecommendationEngine.Evaluate(lux, observed, now) : Recommendation`, `NotifyApplied(now)`, `Reset()`. Comparações estritas: `lux < darkBelow` → escuro; `lux > lightAbove` → claro; limites exatos → `Hold`.

### Task 4 — Adaptador de tema e observador (issues #3/#7)
`Core/Theme/` · `IThemeRegistry`, `IContrastProbe`, `IThemeJournal`, `ThemeAdapter.Apply(ThemeKind) : ApplyOutcome` (`NoOp`, `Applied`, `PartialFailure`, `Failed`, `Blocked`), `ExternalThemeObserver.OnRegistryChanged() : ObservationResult`, `Reconcile()` na inicialização.

### Task 5 — Coordenador (issue #7)
`Core/Automation/` · `AutomationCoordinator` com `DesiredMode`/`PauseReason` persistidos e `RuntimeState` volátil; eventos `OnConnecting`, `OnAuthenticated`, `OnReading`, `OnSensorStatus`, `OnTransportLost`, `OnFatalFailure`, `OnExternalThemeChange`, `UserPause`, `UserSetMode`, `Tick`.

### Task 6 — Bordas Win32 (issue #3)
`windows/src/AmbientTheme.Windows/` · `RegistryThemeRegistry`, `RegistryChangeWatcher` (`RegNotifyChangeKeyValue`), `HighContrastProbe`, `DpapiSecretStore`, `FileThemeJournal`, `ThemeChangeBroadcaster`; `windows/tools/ThemePoc`.

### Task 7 — Conexão e pareamento (issues #4/#5/#8, lado Windows)
`Core/Connection/` · `BackoffPolicy`, `EndpointResolver`, `PairingInvite`, `CertificatePin`, `DeviceConnection`, `ConnectionSupervisor`, `IDiscovery`; `windows/tools/SensorSimulator` (servidor WSS de referência, fonte `SIMULATED`).

### Task 8 — Shell Avalonia (issue #8, lado Windows)
`windows/src/AmbientTheme.App/` · bandeja, estado separado (rede, identidade, WebSocket, sensor, automação), modos, presets/limiares, pareamento por QR + endpoint manual, notificação única por episódio. Textos em `Core/Presentation/StatusPresenter`.

### Task 9 — CI e documentação (issue #9, parcial)
`.github/workflows/windows.yml`, `windows/README.md`, atualização do `README.md`.
