# Ambient Theme Controller — protocolo local v1

Contrato entre o Android (servidor WSS) e o Windows (cliente iniciador). Implementação de referência:
`windows/src/AmbientTheme.Core/Protocol` (codec e validador), `windows/src/AmbientTheme.Core/Connection` (cliente)
e `windows/tools/SensorSimulator` (papel do Android, somente para testes).

> **Estado:** o lado Windows está implementado e testado contra o simulador. O fluxo de pareamento ainda
> precisa de revisão de ameaças e de validação em aparelho real (issue #4) antes de qualquer release.

## Transporte

- WebSocket sobre TLS: `wss://<host>:<porta>/v1`. Não existe modo `ws://`.
- O Android apresenta um certificado autoassinado. A **identidade** do Android é o SHA-256 do
  `SubjectPublicKeyInfo` desse certificado (o *pin*), fixado no pareamento. Endereço IP, nome mDNS e TXT
  DNS-SD são apenas dicas.
- Fora do pareamento, o Windows recusa qualquer certificado cujo pin seja diferente do fixado, antes de enviar
  qualquer dado.
- Mensagens são JSON UTF-8 em frames de texto, com no máximo **4096 bytes**. Mensagem maior, malformada, de
  versão diferente ou de tipo desconhecido encerra a sessão.
- Keep-alive WebSocket (PING/PONG) de 10 s / 10 s no cliente.

## Envelope

Todo objeto tem `type`, `protocolVersion` (= `1`) e `messageId`. Campos desconhecidos são ignorados.

| `type` | Sentido | Campos |
|---|---|---|
| `HELLO` | Android → Windows | `deviceId`, `sessionId`, `appVersion`, `capabilities[]` |
| `AUTH` | Windows → Android | `clientId`, `credential` |
| `AUTH_RESULT` | Android → Windows | `authorized`, `reason?` |
| `LIGHT_READING` | Android → Windows | `sessionId`, `sequence`, `measuredAt`, `lux`, `source`, `quality`, `sensorEventAgeMs` |
| `SENSOR_STATUS` | Android → Windows | `sessionId`, `state`, `lastEventAgeMs?`, `reason?` |
| `ACK` | Windows → Android | `correlationId` |
| `ERROR` | ambos | `correlationId?`, `code`, `message` |
| `PAIR_REQUEST` | Windows → Android | `clientId`, `clientName`, `inviteToken` |
| `PAIR_APPROVE` | Android → Windows | `deviceId`, `credential` |
| `PAIR_REVOKE` | Windows → Android | `clientId` |

Enumerações: `source` ∈ `LIGHT_SENSOR`, `CAMERA_ESTIMATE`, `SIMULATED`; `quality` ∈ `GOOD`, `UNCERTAIN`,
`INVALID`; `state` ∈ `ACTIVE`, `UNAVAILABLE`, `UNCERTAIN`, `ERROR`.

Não existe mensagem que peça troca de tema. O Windows nunca aceita comando de tema vindo da rede.

## Sessão

1. Windows conecta e valida o pin do certificado.
2. Android envia `HELLO` com um `sessionId` novo. Se `deviceId` não for o pareado, o Windows encerra.
3. Windows envia `AUTH`. Android responde `AUTH_RESULT`. `authorized=false` (credencial revogada ou errada)
   exige novo pareamento; o Windows **para** de tentar reconectar.
4. Android envia `LIGHT_READING` a cada evento físico do sensor e `SENSOR_STATUS` periodicamente (5 s
   recomendado) e sempre que a saúde do sensor mudar.
5. Windows responde `ACK` a cada leitura aceita e `ERROR` (`READING_REJECTED`) a cada leitura recusada.

### Regras de `LIGHT_READING`

- `sequence` cresce estritamente dentro da sessão. Repetida ou menor: recusada.
- `sessionId` deve ser o do `HELLO` atual. Leitura de sessão anterior: recusada.
- `lux` finito, `0 ≤ lux ≤ 200000`.
- `sensorEventAgeMs` é a idade do **evento físico**, medida no Android com relógio monotônico
  (`SensorEvent.timestamp`). Nunca é zerada ao reenviar. Acima de 10 000 ms: recusada como antiga.
- Só `source = LIGHT_SENSOR` com `quality = GOOD` alimenta a automação. `SIMULATED` é recusada, salvo em
  build/execução de desenvolvimento com opção explícita. `CAMERA_ESTIMATE` não é suportada no MVP.

### `SENSOR_STATUS` não é leitura

`SENSOR_STATUS` prova que o serviço responde e informa a saúde do sensor. **Nunca** conta como amostra nova.
Após (re)conexão, o Windows só volta a estabilizar depois de receber ao menos uma `LIGHT_READING` válida da
sessão atual. `state ≠ ACTIVE` suspende a automação imediatamente.

## Pareamento

1. Windows gera um convite: `clientId`, nome, token aleatório de 32 bytes, validade de 2 minutos, uso único.
   Mostra como QR: `ambienttheme://pair?v=1&c=<clientId>&n=<nome>&t=<token>&e=<expira-unix>`.
   O QR **não** contém endereço; o endpoint vem do mDNS ou é digitado.
2. Android lê o QR e guarda o convite pendente. Isso é a aprovação explícita daquele computador.
3. Windows conecta ao endpoint, recebe `HELLO` e mostra o código do certificado (primeiros 64 bits do pin,
   `XXXX-XXXX-XXXX-XXXX`). O usuário confere com o código exibido no Android.
4. Só depois da confirmação o Windows envia `PAIR_REQUEST` com o token. Se o usuário recusar, nada é enviado.
5. Android confere `clientId`, token e validade, invalida o convite e responde `PAIR_APPROVE` com uma
   credencial exclusiva daquele computador. Caso contrário responde `ERROR` (`PAIRING_REJECTED`).
6. Windows guarda a credencial com DPAPI, fixa o pin e o `deviceId`.

Revogar no Android apaga a credencial daquele `clientId`. Nenhum token, credencial ou chave aparece em mDNS,
logs ou exportação de configurações.

## Pontos em aberto

- Código de conferência de 64 bits e certificado autoassinado: aceitar como está ou adotar um protocolo
  padronizado de pareamento (spec §5) — depende da revisão de ameaças.
- Cadência real de `SENSOR_STATUS` e semântica de `UNCERTAIN` com tela apagada — dependem da PoC #2.
- Negociação de cadência e `PING`/`PONG` de aplicação — não implementados; o keep-alive do WebSocket é usado.
