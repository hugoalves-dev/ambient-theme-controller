# Ambient Theme Controller — Design arquitetural consolidado (Open Core)

**Data:** 2026-10-22  
**Fase:** Brainstorm encerrado · design consolidado v1.0 para revisão · implementação do produto não iniciada; publicação de documentação aprovada  
**Escopo do MVP:** Android como sensor externo; Windows 11 como consumidor da leitura e controlador do tema.  
**Princípios:** MVP Free local-first, sem conta/backend/internet obrigatórios; Premium remoto futuro reutiliza RelayControl e requer internet; sem root/administrador no funcionamento local, respeitando políticas de rede.

> **Legenda:** [VALIDADO] PoC relatada ou documentação inspecionada; [DECIDIDO] direção do produto aprovada pelo usuário; [PROPOSTO] design técnico sugerido (a revisar na especificação); [PENDENTE] teste/validação antes de assumir garantia. **Fechar o brainstorm não é declarar PoCs pendentes como concluídas.**

## 1. Evidências da prova de conceito

### PoC-01 — WebSocket entre Android e Windows: [VALIDADO]

- Windows 11 + PowerShell 7.6.7, sem administrador; Android + Termux/Python, sem root.
- Windows conectado simultaneamente à Ethernet corporativa (`192.168.0.38/24`) e hotspot Wi-Fi `JCPC` (`192.168.137.1/24`; interface `Conexão Local* 11`).
- Android conectado ao hotspot: `192.168.137.175/24`; rota local `192.168.137.0/24`, `NextHop 0.0.0.0` (destino on-link).
- TCP `8765` e HTTP local com resposta `ANDROID_CONECTADO`: sucesso.
- WebSocket `8766`: Android **servidor**, Windows **cliente** via `ClientWebSocket` nativo do .NET; Windows inicia a conexão.
- Cinco amostras simuladas: **5, 25, 50, 100 e 200 lux**, recebidas em JSON; Windows respondeu ACK pelo mesmo canal.
- Comunicação bidirecional inteiramente local. Python/Termux eram apenas instrumentos de teste, não requisitos do produto.

**Precisão da conclusão:** o transporte funciona nessa topologia. A PoC **não** validou sensor físico, câmera, foreground service/background, descoberta, pareamento seguro, reconexão, automação ou aplicação do tema. Também não demonstrou suporte genérico a todos os modelos Android/hotspot.

### PoC-02 — Sub-redes distintas sem rota acessível: [VALIDADO]

- Windows `192.168.0.38/24`; Android `192.168.2.137`; gateway Windows `192.168.0.254`.
- Windows com perfil `DomainAuthenticated` e firewall de domínio ativo com bloqueio de entrada por padrão.
- Tentativas nos dois sentidos terminaram em timeout; o traceroute do Windows seguiu pelo gateway e alcançou roteadores públicos; servidor TCP do Windows estava escutando.
- **Conclusão:** infraestrutura física compartilhada não implica conectividade IP. QR, mDNS e WebSocket não superam ausência de rota ou política de firewall.
- **Limite de inferência:** timeout não determina sozinho qual firewall, ACL, NAT ou rota causou a falha. O diagnóstico do produto deve preservar essa incerteza.

## 2. Decisões e hipóteses arquiteturais

| ID | Estado | Decisão ou proposta |
|---|---|---|
| ADR-001 | [DECIDIDO] | Produto A: Android mede; Windows decide e aplica tema. |
| ADR-002 | [VALIDADO]/[PROPOSTO] | Priorizar WebSocket local; **Android servidor**, **Windows cliente iniciador**. Transportado com sucesso na PoC. |
| ADR-003 | [DECIDIDO] | Quando Android/leituras ficarem indisponíveis, desativar automação, preservar o tema atual e notificar. |
| ADR-004 | [PROPOSTO] | Android Flutter na UI, Kotlin/Android SDK para sensor, descoberta e foreground service; Windows .NET + Avalonia, `ClientWebSocket` nativo. |
| ADR-005 | [PROPOSTO] | Descoberta de servidor Android via Android NSD/mDNS-DNS-SD; QR para **pareamento**, não para transportar dados nem garantir descoberta. IP/host/porta manual como fallback. |
| ADR-006 | [PROPOSTO] | Segurança via WSS, autenticação e identidade persistente, sem exceção TLS genérica. Primeira verificação de identidade explicitamente confirmada pelo usuário. |
| ADR-007 | [DECIDIDO] | Após queda, **reconectar e retomar automaticamente** após autenticação, recuperação e estabilização de leituras válidas. **Pausa manual prevalece** e jamais é revertida por reconexão. |
| ADR-008 | [PROPOSTO] | Câmera como fonte opcional posterior; BLE e USB tethering como investigações futuras. |
| ADR-009 | [PROPOSTO] | Somente uma fonte Android ativa por Windows no MVP; configurações/identidades locais. |
| ADR-010 | [DECIDIDO] | Qualquer **mudança externa detectável** do tema Windows suspende a automação **até retomada explícita**. Eventos produzidos pelo próprio app devem ser correlacionados para não causar autopausa; autoria externa não é identificável com certeza. |
| ADR-011 | [DECIDIDO] | No MVP, **controlar juntos** o tema de aplicativos (`AppsUseLightTheme`) e o tema do sistema (`SystemUsesLightTheme`) no Windows 11. Não oferecer controle separado na interface do MVP. |
| ADR-012 | [DECIDIDO] | MVP com **presets de luminosidade e thresholds personalizáveis**, configuração local no Windows, valores sugeridos sem pretensão de limiares médicos. |
| ADR-013 | [DECIDIDO] | Distribuição **Open Core**: Free 100% open source, GitHub/F-Droid; Premium inclui core Flutter público + módulo proprietário isolado e distribui APK/AAB apenas pela Google Play. |
| ADR-014 | [DECIDIDO, COM CONDIÇÕES] | Um `applicationId` Android para as edições (`dev.hugodotnet.ambienttheme`, exemplo); buscar **mesmo certificado de app signing** nas lojas, distinguindo-o da upload key Google. Migração in-place é objetivo, não garantia. |
| ADR-015 | [DECIDIDO, COM CONDIÇÕES] | Atualizações com política coordenada de `versionCode`/versões, matriz de testes cross-store e exportação/importação como fallback; evitar downgrade de dados e sobrescrita involuntária do Premium por Free. |
| ADR-016 | [DECIDIDO] | Preferir **aplicativo pago uma única vez** na Google Play para a edição Premium; nenhum sistema próprio de checkout/licenciamento para o MVP; lançamento da listagem Play **já como paga**. |
| ADR-017 | [DECIDIDO] | Premium final de roadmap: conectividade remota por **RelayControl**, reutilizando identidade, pareamento, sessões e signaling; não confundir signaling com transmissão efetiva de WebSocket atrás de NAT/firewall. |
| ADR-018 | [PROPOSTO] | Verificação de direito Premium para acesso ao serviço remoto por integração oficial do Google Play (a avaliar, como Play Integrity no servidor); sem conta humana, com controle de abuso, cotas e revogação. |
| ADR-019 | [DECIDIDO] | Nunca incluir código proprietário ou dependências fechadas nos builds públicos/F-Droid; pipelines CI/CD e artefatos separados, sem vazamento de segredos. |
| ADR-020 | [DECIDIDO] | Premium não bloqueia nem degrada recursos locais gratuitos; custo/infra/privacidade da rota remota ficam isolados da experiência local. |

## 3. Matriz de conectividade e prioridade

| Cenário | Técnica | Evidência | Prioridade |
|---|---|---|---|
| Android e Windows no mesmo Wi-Fi | mDNS/DNS-SD + WebSocket | Não testado como fluxo completo | MVP |
| Windows Ethernet + hotspot Wi-Fi, Android no hotspot | IP local + WebSocket | **VALIDADO** sem root/admin | MVP, destaque no onboarding |
| Android hotspot, Windows cliente Wi-Fi | WebSocket sobre IP | Não testado | Alternativa, PoC posterior |
| Sub-redes distintas com rota e firewall permissivos | IP/hostname + WebSocket | Condicional, não testado com sucesso | Suportar quando existir rota |
| IP/porta manual | Conexão direta após entrada de endpoint | Não testado no produto | MVP fallback |
| USB tethering | Rede IP via USB | Não testado | Pesquisa posterior |
| Bluetooth LE | Transporte alternativo sem IP | Não testado | Futuro |

**Observação:** a opção hotspot nativo do Windows depende de adaptador, driver, configuração e políticas locais. A documentação Microsoft apresenta o hotspot como compartilhamento de conexão; a PoC confirmou o tráfego local na configuração usada, não a criação de hotspot em qualquer PC totalmente offline.

## 4. Componentes e limites de responsabilidade

### Android (Flutter + Kotlin)
- `LightSource`: sensor físico `TYPE_LIGHT`; emitir status `ACTIVE`, `UNAVAILABLE`, `UNCERTAIN`, `ERROR` e informação sobre idade da última leitura efetivamente produzida pelo sensor.
- `SensorSessionService`: ciclo de vida de sessão, foreground service quando justificável; notificações e início explícito pelo usuário; registrar/desregistrar listeners apropriadamente.
- `WebSocketHost`: servidor de transporte autenticado, sem lógica de decisões de tema; enviar amostras e status, responder a comandos autorizados.
- `DiscoveryAdvertiser`: anunciar apenas id público, tipo de serviço, porta, versão de protocolo; **nunca** divulgar token de pareamento em TXT mDNS.
- `PairingManager`: aprovação local do computador e armazenamento protegido de credenciais.
- `NetworkObserver`: monitorar interfaces/endereços e reanunciar endpoint após mudança de rede.
- `CameraLightSource` (posterior): estimativa local de luminosidade, sem envio/salvamento de fotos; sujeita a restrições próprias de câmera/background.

### Windows (.NET + Avalonia)
- `EndpointResolver`: unir anúncios mDNS, endpoints conhecidos e endereço digitado, sem tratar IP como identidade.
- `DeviceConnection`: `ClientWebSocket`, TLS/auth, tempos limite, retry e watchdog.
- `MeasurementValidator`: verificar formato, origem autorizada, versão, timestamp, sequência, unidades, status e validade da medição.
- `ThemeRecommendationEngine`: filtragem, limiares calibráveis, histerese, período de estabilidade e cooldown.
- `WindowsThemeAdapter`: leitura/aplicação do tema e observação de alterações manuais; erros não devem ser ocultados.
- `AutomationCoordinator`: estados e autorização final para trocar tema, independente da UI.
- `DesktopShell`: system tray, onboarding, pareamento, diagnósticos e notificações não repetitivas.

**Isolamento:** mensagem WebSocket não pode escrever no Registro nem mudar o tema diretamente. Só dados validados entram no motor, e apenas o coordenador autorizado pode executar uma alteração.

## 5. Descoberta, QR e seleção de endpoints

### Fluxo preferencial [PROPOSTO]
1. Usuário inicia uma sessão de medição no Android; servidor WebSocket é aberto na(s) interface(s) local(is) permitida(s).
2. Android anuncia serviço DNS-SD, por exemplo `_ambienttheme._tcp`, com identificador público, versão, porta e endereços resolvidos pelo SO.
3. Windows descobre candidatos, agrupa por **deviceId/identidade autenticada**, não por IP.
4. Windows inicia o pareamento e apresenta QR com sua própria identidade pública e convite temporário. Android escaneia e autoriza o Windows.
5. **Atenção: o QR exibido no Windows não ensina automaticamente ao Windows qual é o IP Android.** Endpoint vem de mDNS, de endereço previamente guardado ou da entrada manual exibida no Android.
6. Windows testa endpoints candidatos em sequência/pequenas tentativas paralelas limitadas, valida TLS e autenticação, seleciona o dispositivo autorizado e estabelece sessão.
7. Salvar identidade permanente, preferências e endpoints vistos como **caches mutáveis**. Nova rede exige resolução novamente.

### Segurança do primeiro pareamento [PROPOSTO]
- Produzir convite aleatório forte com expiração curta (ex.: 2 minutos), válido para 1 associação e não publicado em rede.
- WSS com identidade criptográfica do Android. Na primeira conexão, Windows mostra um **código/fingerprint verificável** que o Android exibe localmente para conferir; após confirmação, fixar identidade/certificado ou chave pública (pinning). Não aceitar qualquer certificado indiscriminadamente.
- Android exige aprovação explícita do Windows; armazenar credencial única por computador, permitir **revogação**, renovação e repareamento.
- Não enviar segredo por `ws://` de teste; proteger credenciais no armazenamento do SO; não registrar chaves/tokens em logs.
- O design exato do protocolo de pareamento deve favorecer biblioteca/protocolo padronizado e revisão de ameaça, não criptografia inventada. O QR facilita vínculo, mas por si só não autentica o endpoint remoto.
- Publicar o serviço de forma discreta e restringir comandos do cliente ao mínimo; recusar mensagens excessivas, endpoints desconhecidos e sessões concorrentes não autorizadas.

### Regras de seleção automática [PROPOSTO]
- Prioridade: endpoint autenticado previamente ativo → descoberta mDNS recente → outros endpoints anteriormente associados → endereço manual fornecido.
- Tentar rotas realmente presentes (interfaces e gateways), não inferir sucesso pela igualdade do prefixo IPv4.
- Timeout curto por candidato e retry com jitter; lembrar último endpoint **apenas como dica**.
- Se houver hotspot Windows, preferir endereço Android na rede conectada ao hotspot quando houver anúncio/endereço válido, sem fixar `192.168.137.x` no código.
- Falha de DNS/mDNS = degradar para entrada manual; falta de rota/firewall = explicar que o app não pode ultrapassar políticas.

## 6. Intenção, estados, reconexão e falhas — ADR-007 [DECIDIDO]

**Decisão confirmada em 22/10/2026:** O usuário selecionou a **política B**: reconexão e retomada automáticas depois de estabilizar o sensor; respeitar pausas manuais. Ao detectar degradação, **suspender imediatamente alterações do tema**; quando a falha for confirmada, notificar uma vez. Nunca mudar o tema sem leitura válida. A recuperação segura reabilita a automação apenas se o usuário ainda quiser o modo automático.

### Duas dimensões, sem uma enumeração combinatória [PROPOSTO]

1. **Intenção persistente do usuário (`DesiredMode`)**: `AUTO`, `RECOMMEND_ONLY`, `MANUAL_PAUSE`. Guardar essa preferência independentemente de qualquer queda de rede; modo automático selecionado **não é apagado** por indisponibilidade. Uma mudança explícita do usuário ou uma alteração externa detectável de tema gera `MANUAL_PAUSE`, com causa `USER_PAUSED` ou `EXTERNAL_THEME_CHANGE`, e prevalece sobre eventos de rede/sensor.
2. **Estado operacional volátil (`RuntimeState`)**: `UNPAIRED`, `DISCOVERING`, `CONNECTING`, `AUTHENTICATING`, `WAITING_FOR_SENSOR`, `ACTIVE`, `DEGRADED`, `DISCONNECTED`, `RECONNECTING`, `STABILIZING`, `READY`. O controle efetivo só age quando `DesiredMode=AUTO` **e** `RuntimeState=ACTIVE/READY`, com leitura válida e demais gates autorizando a mudança.

Isso distingue **“automação suspensa automaticamente”** de **“pausada por escolha do usuário”**. Pausa manual persiste mesmo após reiniciar aplicações e mudar de rede, até o usuário sair da pausa. O cliente pode continuar reconectando silenciosamente durante a pausa, desde que o usuário não encerre a sessão de monitoramento.

### Transições [PROPOSTO]

- `ACTIVE` → `DEGRADED` quando a conexão ou qualidade da leitura fica suspeita: **congelar alterações imediatamente**, mas aguardar janela curta para evitar notificações por microfalhas.
- `DEGRADED` → `DISCONNECTED` por falha confirmada: deixar controle efetivo suspenso, **preservar tema atual** e notificar **uma única vez por episódio**.
- `DISCONNECTED` → `RECONNECTING`: Windows re-resolve candidatos e inicia `ClientWebSocket` de saída; backoff exponencial limitado com jitter, sem varrer redes nem abrir porta de entrada no Windows.
- `RECONNECTING` → `AUTHENTICATING` → `WAITING_FOR_SENSOR`/`STABILIZING`: reconexão só conta após identidade e credenciais verificadas. Não aceitar mensagens de sessão anterior, nem leitura meramente retransmitida como leitura nova.
- `STABILIZING` → `READY`: aguardar sensor saudável + pelo menos uma **nova leitura física** na sessão recuperada + consistência em janela proposta de 20–30 s. Para sensores *on-change*, não exigir N eventos periódicos (podem não ocorrer se a luminosidade permanecer igual). A implementação concreta do critério de validade necessita PoC com aparelho bloqueado.
- `READY` → `ACTIVE`: somente se `DesiredMode=AUTO` e sem bloqueio/manual override; respeitar cooldown e histerese já existentes, não disparar mudança no instante do handshake.
- `READY` com `DesiredMode=RECOMMEND_ONLY`: atualizar recomendação; **não alterar tema**.
- `READY` com `DesiredMode=MANUAL_PAUSE`: continuar pausado; **não alterar tema** e não ativar sozinho, mesmo depois de reiniciar o app.
- Revogação, falha TLS/identidade ou erro explícito do sensor **não devem** produzir loop agressivo de reconexão nem ativar automação. Revogação exige novo pareamento.

### Watchdogs sugeridos para experimento, não SLAs [PROPOSTO]

| Mecanismo | Valor inicial | Interpretação |
|---|---|---|
| WebSocket PING/PONG | `KeepAliveInterval` 10 s / `KeepAliveTimeout` 10 s no Windows | Detecta sessão de transporte inerte; confirmar compatibilidade do servidor Android |
| Mensagens `SENSOR_STATUS` | 5–15 s enquanto sessão ativa | Comprova que o serviço responde, **não** que sensor produziu amostra nova |
| Suspeita de falha | 15–20 s | Suspender mudanças imediatamente |
| Falha confirmada | até ~45 s ou falha explícita | Notificar uma vez, efetivamente desativado |
| Reconexão | 1, 2, 4, 8, 15, até 30 s, com jitter | Tentar mesma identidade por endpoints atualizados |
| Estabilização | 20–30 s com sinais reais de validade | Retomar automaticamente apenas quando seguro |

**Fundamental:** WebSocket conectado ≠ sensor disponível. `TYPE_LIGHT` pode ser *on-change*. Tratar idade do **evento físico** e validade da sessão separadamente; nunca fazer `measuredAt=agora` ao retransmitir amostra velha. Não afirmar falha do sensor somente por não haver mudança de lux; quando não for possível provar leitura atual, manter estado incerto e bloqueio de automação até ser possível.

### Modelo lógico de habilitação [PROPOSTO]

`MayApplyTheme` exige, simultaneamente: `DesiredMode=AUTO`; pareamento autorizado; WSS autenticado; sessão atual; leitura confiável e adequada à fonte; estabilidade aprovada; mudança passou por histerese/cooldown; ausência de pausa/override manual; adaptador Windows operacional.

Não permitir que o transporte, a UI ou um ACK genérico ative o tema diretamente. Após uma falha o processo retoma sozinho a **intenção automática**, nunca ignora uma pausa manual que ocorreu enquanto tentava reconectar.

### Notificações [PROPOSTO]

- **Falha confirmada:** “Sensor Android indisponível. Automação suspensa; tema atual mantido.”
- **Retomada:** “Sensor recuperado. Controle automático retomado.” (respeitar preferência de notificações, sem spam).
- **Pausa manual:** status “Pausado por você”, mesmo se o sensor estiver saudável.
- **Mudança externa:** “Tema alterado fora do Ambient Theme Controller. Automação pausada até você retomá-la.” Exibir uma vez, sem insistência.
- Notificação única por episódio; eventos transitórios podem atualizar indicador na bandeja sem toast.

## 7. Motor de decisão (Windows) — ADR-012 [DECIDIDO]

- O Windows é o único responsável por thresholds, avaliação, cooldown, recomendação e escrita no tema. Android só fornece medição/qualidade.
- MVP inclui **presets + personalização**: valores editáveis `darkBelowLux` e `lightAboveLux`; mudança manual dos valores gera perfil `PERSONALIZED`. Configuração por Windows/usuário, não embutida no pareamento/IP.
- Premissa a validar: `darkBelowLux < lightAboveLux`, ambos finitos, não negativos e compatíveis com o alcance do sensor. Impedir inversões e alterações inválidas sem gerar correções silenciosas.
- Presets numéricos abaixo são [PROPOSTOS] para avaliação de UX, **não** valores clínicos ou universalmente corretos.
- Aplicar histerese baseada no tema observado: abaixo de `darkBelowLux`, candidato escuro; acima de `lightAboveLux`, candidato claro; na faixa intermediária, não pedir mudança. Usar comparações inclusivas/exclusivas consistentes e documentadas para valores exatamente nos limites.
- Exigir estabilidade sustentada de um candidato e cooldown depois de alterações automáticas. Não bloquear pausa explícita; sempre distinguir recomendação de autorização.
- Começar com avaliação determinística e sem ML. Filtros, se necessários, devem usar janela temporal limitada, sem tratar amostras antigas como novas.
- Manter as condições já decididas: sem leitura confiável, desabilitar alteração automática e notificar uma vez por episódio; reconexão pode reabilitar após estabilização se não houver pausa manual; mudanças externas detectáveis do tema produzem pausa até retomada explícita.

## 8. Alteração do tema no Windows

- Primeiro consultar o tema atual; quando modo desejado for igual, **não escrever**.
- Microsoft documenta `HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize` com `AppsUseLightTheme` e `SystemUsesLightTheme` (DWORD 0/1).
- Escrita em HKCU tende a dispensar administrador, mas atualização visual imediata, notificações do sistema e compatibilidade de apps **precisam de validação específica**; valores documentados não representam uma API pública de alternância completamente garantida para todas as versões.
- **ADR-011:** ambos os valores de aplicativos e sistema são controlados juntos no MVP; isso não significa que o Windows ofereça escrita atômica dos dois valores ou que aplicativos de terceiros obedeçam imediatamente.
- Testar manual override, app que ignora mudança, Explorer, rollback de falhas e competição com outros controladores.
- **ADR-010:** monitorar o tema observado e correlacionar alterações internas para evitar autopausas; detalhamento na seção 17. **ADR-011** e as falhas parciais de atualização conjunta são detalhadas na seção 18.

## 9. Android em background: riscos reais

- `TYPE_LIGHT` via `SensorManager` e identificação do reporting mode / wake-up são os primeiros experimentos reais.
- Android impõe restrições de sensores em apps comuns de background; testar foreground service apropriado (`connectedDevice`, observando pré-requisitos oficiais) **sem presumir continuidade do sensor**.
- Verificar tela acesa, minimizado, bloqueado, economia de bateria, Doze, processo morto, troca de Wi-Fi/dados, suspensão e retorno.
- Evitar `WorkManager` para suposta medição contínua de segundos: jobs periódicos não têm cadência apropriada.
- A câmera tem restrições especiais de permissão/foreground e maior impacto energético. Tratar como opção opt-in em sessão explícita, não fallback silencioso.
- Android 17 / API 37: considerar `ACCESS_LOCAL_NETWORK` ou fluxos oficiais mediados por descoberta NSD; avaliar permissões por `targetSdkVersion`.

## 10. Onboarding e diagnósticos

### Passos de onboarding [PROPOSTO]
1. Windows: “Conectar meu celular” → procura Android; explica ausência de dados armazenados na nuvem.
2. Android: “Usar como sensor” → verifica sensor e inicia serviço com indicação persistente.
3. Windows encontra o Android por mDNS. Em seguida apresenta pareamento; Android lê QR do **Windows** e autoriza. Sem mDNS: mostrar IP e porta no Android para digitar no Windows.
4. Em redes restritas: tutorial para Windows hotspot com Ethernet + Wi-Fi, destacando possibilidade de bloqueio por política. Alternativa Android hotspot somente após PoC.
5. Mostrar status separado de **rede, identidade, WebSocket, sensor e automação**.
6. Selecionar “Somente recomendar” ou “Automático” depois da leitura inicial estável, com explicação dos limites.

### Linguagem de erro
- `ECONNREFUSED`: destino acessível em algum grau, mas não houve listener aceitando naquela porta (ou rejeição ativa por política); não afirmar culpa exata.
- `TIMEOUT`: conexão não concluída; pode ser rota, firewall, isolamento, endereço incorreto ou serviço indisponível.
- `UNREACHABLE`: sem caminho para destino segundo o sistema, ainda sujeito a variações de rede.
- `AUTH_FAILED`/`TLS_IDENTITY_MISMATCH`: conexão alcançada mas identidade/autenticação recusada; orientar repareamento seguro.
- `SENSOR_UNAVAILABLE`: rede conectada, sensor ausente ou incapaz de medir; não misturar com falha de transporte.
- `STALE_MEASUREMENT`: conexão viva, mas qualidade/atualidade insuficiente; suspender automação.
- Não solicitar mudanças em firewall de domínio nem contornar políticas de empresa.

## 11. Contrato de mensagens em discussão

Mensagens lógicas a versionar (design conceitual, **não código pronto**):

- `HELLO`: versão, `deviceId`, `sessionId`, capacidades, versão do app.
- `LIGHT_READING`: `sessionId`, `sequence`, `measuredAt`, `lux`, `source` (`LIGHT_SENSOR` ou `CAMERA_ESTIMATE`), `quality`, idade do evento físico.
- `SENSOR_STATUS`: `ACTIVE` / `UNAVAILABLE` / `UNCERTAIN` / `ERROR`, erro opcional.
- `PING`/`PONG` ou heartbeat de aplicação com relógio monotônico por sessão; não confundir com evento fresco do sensor.
- `ACK` e erros com `correlationId` e `protocolVersion`.
- `PAIR_REQUEST`, `PAIR_APPROVE`, `PAIR_REVOKE` em canal autenticado/protocolo de bootstrap específico, ainda sob avaliação de segurança.

As mensagens devem ser pequenas, limitadas em tamanho e processamento. O Windows não aceita comandos arbitrários de alteração do tema vindos do Android.

## 12. Critérios de aceitação do MVP e próximas provas

1. **MVP rede:** duas máquinas pareadas em Wi-Fi comum **e** cenário Windows Ethernet + hotspot; sem internet durante transferência; endereço manual funcional.
2. **MVP autenticação:** Windows não autorizado não recebe dados; invalidar token de convite e revogar computador; troca de IP não troca identidade.
3. **MVP sensor:** amostras reais produzidas pelo sensor físico em pelo menos dois aparelhos, com diferenças claras entre sensor ativo e dado antigo.
4. **MVP background:** documentar precisamente em quais estados a leitura continua; quando não continuar, automação efetiva suspensa, notificação adequada e retomada automática apenas após validade comprovada.
5. **MVP lógica:** sem troca em faixa intermediária, cooldown respeitado, sem flapping, manual override preservado.
6. **MVP tema:** atualizar **juntos** `AppsUseLightTheme` e `SystemUsesLightTheme`, verificar valores aplicados no Windows 11 com e sem aplicações abertas, mudanças manuais em qualquer uma das duas preferências e falhas de escrita parciais.
7. **MVP resiliência:** testar mudanças de IP, desconexão curta/longa, saída e entrada de hotspot, reinicialização do app e credencial revogada. **Caso essencial:** pausa manual feita durante a reconexão NÃO é revertida na recuperação; estado AUTO interrompido por rede/sensor volta sozinho após estabilizar.
8. **MVP UX:** usuário não técnico consegue conectar via mDNS ou hotspot, e entende quando não há conectividade permitida.

### Próximas PoCs, por ordem de risco
1. `TYPE_LIGHT` real em foreground/minimizado/bloqueado/Doze, incluindo ausência de eventos em sensor on-change.
2. Servidor WSS Android com foreground service, prova de autenticidade sem acesso root/admin, reconexão.
3. Android NSD anunciar + descoberta pelo Windows: rede normal e hotspot Windows.
4. Alternância de tema Windows 11: impacto visual, eventos do Registro, diferenciação de escrita própria e externa, persistência de pausa.
5. Hotspot criado pelo Android; USB tethering; BLE só se houver necessidade comprovada.

## 13. Histórico de decisões do MVP

- **22/10/2026 — PoC [VALIDADO]:** Android/Termux servidor WebSocket e Windows/.NET cliente, na rede de hotspot criada pelo Windows. Transporte bidirecional confirmado, *não* sensor/tema/segurança.
- **22/10/2026 — ADR-003 [DECIDIDO]:** falha confirmada suspende automação efetiva, preserva tema e notifica.
- **22/10/2026 — ADR-007 [DECIDIDO]:** **B**. Reconexão e retomada automáticas após leituras estáveis e autenticadas; **nunca** sobrescrever pausa manual. No estado suspenso, intenção `AUTO` permanece persistida e o controle efetivo fica bloqueado.
- **22/10/2026 — ADR-010 [DECIDIDO]:** **A**. Alteração externa detectável no tema Windows suspende a automação de forma persistente. Só uma ação explícita do usuário permite retomar. Alterações feitas pelo próprio app não devem disparar autopausa.
- **22/10/2026 — ADR-011 [DECIDIDO]:** **A**. No MVP, controlar tema de aplicativos e do sistema juntos. Os dois valores são uma única intenção de produto, mas não uma operação atômica de Registro.
- **Próxima escolha em aberto:** configuração inicial de limiares: presets simples com personalização opcional, calibração guiada ou configuração estritamente manual.

## 14. Contratos e segurança: consolidação de design [PROPOSTO]

### Fronteiras de confiança

- Descoberta mDNS, endereço IP e TXT DNS-SD são **dicas não confiáveis**. Identidade real é a chave/credencial associada ao `deviceId` e conferida no pareamento.
- Android recebe conexão apenas de Windows autenticado. Se usar WSS com certificado local/autoassinado, exigir conferência inicial fora de banda (ex.: fingerprint/código exibido nos dois dispositivos) e **pinning posterior**. Jamais configurar aceitação universal de certificados.
- QR mostrado pelo Windows representa convite temporário e sua identidade, enquanto o endpoint Android vem da descoberta ou inserção manual. Android precisa **aceitar aquele computador** explicitamente. O fluxo completo de bootstrap TLS/pareamento ainda deve ser projetado e testado, não está validado pela PoC `ws://`.
- Tokens de convite expiram, são de uso único; credenciais permanentes devem ser **por computador**, revogáveis. Proteção em repouso: **Android Keystore** para chaves Android, **Windows DPAPI (`ProtectedData`)** para segredos do usuário Windows.
- Comandos recebidos do Windows no Android ficam limitados ao protocolo necessário (status, autorização, cadência negociada); Android não executa comandos arbitrários. No Windows, leituras recebidas nunca contêm comandos de mudança de Registro.
- Limitar tamanho/taxa de mensagens, conexões não autorizadas e sessões paralelas; não logar token, imagens de câmera, certificado privado ou leitura de ambiente associada a identidade em telemetria remota.

### Identificadores/tempos sugeridos

- `deviceId`: estável e não igual a endereço IP; `sessionId`: novo por conexão/instância de medição; `sequence`: cresce dentro da sessão; `measuredAt` + `sensorEventAgeMs`: idade da amostra física, **não** do reenvio; `sourceType`: `LIGHT_SENSOR` ou `CAMERA_ESTIMATE`; `quality`: apropriado/indeterminado/inválido.
- Preferir **relógio monotônico local** para timeouts e medição de idade na origem, sem supor que relógios Android e Windows estão perfeitamente sincronizados; evitar reinterpretação de timestamp remoto como evidência única de frescor.
- Pacote `SENSOR_STATUS` periódico deve carregar saúde, última idade real do evento e motivo para `UNCERTAIN`, não reetiquetar amostra velha como atual.

### Reconexão/seleção de interface

- Priorizar endereço local do Android que esteja realmente alcançável pela interface Windows selecionada (hotspot e Ethernet podem coexistir). Usar identidade e endpoints candidatos, **não** rede `192.168.137.0/24` fixa.
- Windows resolve mDNS novamente após troca de rede/timeout e faz tentativas curtas e limitadas; Android acompanha mudanças de conectividade com `ConnectivityManager.NetworkCallback` e atualiza anúncio/servidor conforme disponibilidade. Resultado do callback não substitui teste real de conexão. 
- Sem mDNS: informar IP/hostname e porta do **Android** no Windows. Diagnóstico transparente para `timeout`, `refused`, TLS e sensor ausente; não recomendar burlar firewall corporativo.
- A `connectedDevice` foreground service exige seu tipo, permissão e **pré-requisitos documentados pelo Android**; não assumir que basta declarar o tipo para manter sensor ou processo ativos. PoC de ciclo de vida antes de escolher implementação do servidor (Dart isolate persistente vs Kotlin service nativo).

## 15. Critérios extras da política B [PROPOSTO]

| Experimento | Resultado exigido para aceitação |
|---|---|
| Queda de Wi-Fi por 5–10 s | Nunca alterar tema com dado suspeito; não necessariamente notificar falha confirmada |
| Queda longa, depois reconexão | Suspender e notificar uma vez; retomar sozinho após autenticação, amostra válida e estabilização |
| Usuário pausa enquanto reconectando | Continuar `MANUAL_PAUSE`; **nenhuma** retomada automática nem no restart |
| Revogação do Windows no Android | Conexão falha autorizadamente, não faz downgrade de segurança nem reativa tema |
| Apenas heartbeats, sem leitura nova ou qualidade confiável | Não retomar até que validade do sensor seja comprovada |
| Mudança de IP Android / hotspot | Re-resolver endpoint e autenticar **mesmo dispositivo**, sem tratar endereço como identidade |
| Falha ao aplicar tema Windows | Reportar no UI/diagnóstico; não afirmar sucesso, sem loop de escrita |
| Alteração externa do tema enquanto AUTO ativo | Interromper imediatamente novas mudanças automáticas; persistir pausa; retomar somente por ação explícita |
| Eco do evento de uma alteração aplicada pelo próprio app | Não disparar pausa, desde que correlação da operação confirme resultado esperado |
| Alteração externa durante reconexão e estabilização | Registrar pausa persistente; reconexão não pode limpá-la |
| Tema modificado com app fechado; app reinicia | Se divergência detectável com estado conhecido anteriormente, exigir retomada; ausência de evidência não autoriza alegar intervenção humana |
| Outro aplicativo concorrente troca tema | Tratar como alteração externa, sem identificar o autor; evitar disputar controle |

## 16. Estado de maturidade e limites

- **Transporte WebSocket `ws://` Android→Windows via hotspot Windows**: [VALIDADO] na PoC relatada pelo usuário.
- **Reconexão automática e retomada após estabilização**: [DECIDIDO] como requisito, ainda [PENDENTE] implementar/testar.
- **Pausa persistente por alteração externa de tema**: [DECIDIDO] como requisito, mas detecção/correlação no Windows 11 [PENDENTE] prova de conceito.
- **Controle conjunto Apps + Sistema**: [DECIDIDO] como requisito (ADR-011), [PENDENTE] validar aplicação visual, falhas parciais e notificações do Windows.
- **Segurança WSS, mDNS, pareamento por QR, foreground/background, sensor real, alteração do tema, hotspot Android, USB e BLE**: [PENDENTE] validações independentes.
- **Não existe especificação final, plano de implementação nem código de produto aprovados.**

## 17. Observação de alterações do tema e precedência da decisão do usuário — ADR-010 [DECIDIDO]

**Decisão de 22/10/2026:** a opção **A** foi escolhida. Alterar tema pelas Configurações do Windows (ou outra ferramenta externa) faz o Ambient Theme Controller entrar em **pausa manual persistente**, até que o usuário selecione explicitamente “Retomar automático”. Não há timeout para remover essa pausa. Durante a pausa, o serviço pode continuar monitorando/reconectando, mas não pode aplicar temas automaticamente.

### Modelo de precedência [PROPOSTO]

- `DesiredMode` persiste como `AUTO`, `RECOMMEND_ONLY` ou `MANUAL_PAUSE` e guarda `PauseReason` (`USER_PAUSED`, `EXTERNAL_THEME_CHANGE`, quando aplicável); estado operacional `RuntimeState` permanece independente.
- Suspensão por falha de transporte/sensor **não** altera `DesiredMode=AUTO`; recuperação com dados estáveis retoma automaticamente **somente** se o usuário não entrou em pausa.
- Alteração externa de tema enquanto `AUTO`, `DEGRADED`, `RECONNECTING` ou `STABILIZING` eleva a intenção persistente para `MANUAL_PAUSE`. Em todas as condições, preferências explícitas prevalecem sobre o motor.
- Um comando explícito “Retomar automático” permite `MANUAL_PAUSE → AUTO`, mas ainda aguarda pareamento, sensor estável, histerese e cooldown; não dispara troca imediata por estado remoto.
- A pausa persiste quando app ou Windows reinicia. Quando houver estado confiável persistido, comparar tema observado na inicialização ao último snapshot para identificar divergências ocorridas enquanto o app estava fechado; **não inferir autoria humana**, apenas mudança externa/indeterminada.

### Monitoramento do Windows [PROPOSTO, NÃO VALIDADO]

1. Observar `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`, especialmente `AppsUseLightTheme` e `SystemUsesLightTheme`, por `RegNotifyChangeKeyValue` com `REG_NOTIFY_CHANGE_LAST_SET` (Win32 oficial). Após cada evento, **rearmar** a notificação; exigir fechamento/descarte correto dos handles.
2. Ler valores efetivos novamente e aplicar coalescência/debounce curto a atualizações quase simultâneas; mudanças apps/sistema podem ocorrer em passos separados, logo não comparar snapshots transitórios sem cuidado.
3. Opcionalmente ouvir `SystemEvents.UserPreferenceChanged` (exige message pump e descarte de handlers) e conciliar o snapshot após desbloqueio/retomada do sistema ou inicialização; evento genérico **não identifica o autor nem garante correspondência 1:1 com tema**.
4. `ThemeMutationJournal` registra **antes** de cada escrita iniciada pelo aplicativo: `operationId`, valores anteriores, valores-alvo e janela operacional. Escritas devem ser serializadas; após aplicar, consultar o estado efetivo, confirmar resultado e encerrar a operação.
5. O `ExternalThemeObserver` correlaciona notificações com mutações próprias, sem ignorar eventos exclusivamente por timestamp. Se novo snapshot divergir do resultado esperado e não puder ser explicado com segurança pela operação em andamento, pausar e impedir novas escritas.
6. Não é possível descobrir com garantia **quem** modificou a chave apenas com notificações. Outro app pode escrever exatamente o mesmo valor esperado durante uma operação. Uma seleção do mesmo tema (sem mudança de valor) pode não ser observável. Publicar essa limitação no design e demonstrar a robustez em testes.
7. Persistir `MANUAL_PAUSE`, causa, último tema observado e status da operação com atualização ordenada. Se houver falha/crash entre escrita e confirmação, não supor automaticamente que a mudança foi originada pelo usuário: reconciliar e preferir suspensão quando ambíguo.
8. Mesmo quando alteração de valores é confirmada, a **atualização visual** do Windows/Explorer/aplicativos precisa de teste específico; a Microsoft documenta os valores do Registro, não uma API completa e garantida para alternar imediatamente todas as superfícies.

### Regras de produto e UI [PROPOSTO]

- Menu de bandeja: “Automação ativa”, “Sensor indisponível: aguardando recuperação” e “Pausado: tema modificado externamente” são estados distintos.
- Se detectar mudança externa: preservar tema escolhido, interromper aplicação automática e exibir aviso discreto uma única vez, com ação **Retomar automático**.
- Se outro controlador de tema (PowerToys Light Switch, Auto Dark Mode, etc.) alterar os mesmos valores: tratar como mudança externa e suspender para evitar disputa, sem afirmar que foi o próprio usuário.
- No MVP, **ambas** as preferências (`AppsUseLightTheme` e `SystemUsesLightTheme`) fazem parte do escopo. Uma mudança externa detectável em qualquer uma delas provoca pausa persistente. Alterações em outras propriedades, como cor de destaque e transparência, não são razão automática para pausa.
- Sem dependência de backend ou privilégios elevados. Evitar polling constante quando o sistema oferece notificações; realizar reconciliação de estado em eventos de ciclo de vida.

### Testes da ADR-010 [PENDENTE]

- Troca automática feita pelo próprio app: sem autopausa e sem loop.
- Usuário muda para claro/escuro nas Configurações do Windows: pausa persistente e nenhuma troca automática posterior.
- Usuário muda apps e sistema quase simultaneamente: observer não classifica mudança interna parcial como externa.
- Alteração durante reconnect/estabilização: a recuperação respeita `MANUAL_PAUSE`.
- App fechado/reiniciado: pausa é restaurada, e divergência externa anterior é conciliada de forma conservadora.
- Outro app de tema concorrente: sistema não entra em guerra de escrita.
- Troca do mesmo valor, modificações de temas de alto contraste, erro de Registro e mudança de política: documentar comportamento e limites de detecção.

### Fontes oficiais adicionais

- Microsoft, valores de personalização no Registro: https://learn.microsoft.com/en-us/windows/apps/develop/settings/settings-common
- Microsoft, `RegNotifyChangeKeyValue`: https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regnotifychangekeyvalue
- Microsoft, `SystemEvents.UserPreferenceChanged`: https://learn.microsoft.com/en-us/dotnet/api/microsoft.win32.systemevents.userpreferencechanged
- Microsoft, mensagem `WM_SETTINGCHANGE`: https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-settingchange

## 18. Controle conjunto do tema de aplicativos e do sistema — ADR-011 [DECIDIDO]

**Decisão de 22/10/2026:** escolhida a alternativa **A**. Uma única intenção de tema do motor (`LIGHT` ou `DARK`) define simultaneamente `AppsUseLightTheme` e `SystemUsesLightTheme` no Windows 11. O MVP **não** terá seletores separados para apps e shell do Windows. O escopo não inclui forçar o tema de aplicativos que ignoram as preferências do sistema, modificar temas personalizados, papel de parede ou paleta de acessibilidade.

### Mapeamento e limites da plataforma

- Chave: `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`.
- Ambos DWORDs são `0` para escuro e `1` para claro. A documentação oficial Microsoft informa os valores, porém **não promete uma API pública transacional de alternância de toda a UI do Windows**. Atualização visual, Explorer e apps já abertos exigem PoC específica.
- Escritas em HKCU podem funcionar sem administração, mas permissões/políticas do usuário devem ser respeitadas. Não tentar desbloquear políticas de domínio ou reiniciar o Explorer para forçar atualização.
- Aplicativos de terceiros podem optar por um tema próprio. Quando os dois valores foram confirmados, isso significa **preferência do sistema aplicada**, não garantia de 100% de superfícies atualizadas.

### `WindowsThemeAdapter` — fronteira de escrita [PROPOSTO]

1. Sob lock/single writer, ler snapshot original dos dois valores e estado de tema de contraste.
2. Bloquear escrita caso `DesiredMode != AUTO`, `RuntimeState` não esteja estável, exista mudança externa pendente, tema de contraste esteja ativo, ou a aplicação seja restrita por política.
3. Se ambos os valores já representam a intenção, realizar operação **no-op** (não escrever nem gerar notificação inútil).
4. Persistir `ThemeMutationJournal` com `operationId`, snapshot anterior, destino comum (0/0 ou 1/1), estado `PENDING` e contexto da sessão. O observador deve conhecer operações internas **antes** da primeira escrita.
5. Escrever apenas os valores necessários, em sequência. A mudança de dois DWORDs **não é atômica**; o observador tolera o snapshot intermediário misto associado à operação em curso, mas não ignora divergências externas.
6. Reconsultar os **dois** valores. Confirmar `APPLIED` somente quando ambos coincidirem com o destino; atualizar UI com o resultado real. A estratégia de difusão das alterações às janelas e a reação visual do shell requerem PoC, sem supor que qualquer broadcast garante repaint universal.
7. Se apenas um valor mudar ou houver erro, registrar `PARTIAL_FAILURE`, impedir novas aplicações automáticas e notificar sem spam. Não efetuar rollback cego: reverter apenas os valores sob controle comprovado da operação, se a estratégia de compensação tiver sido validada e não houver evidência de intervenção concorrente; caso contrário, preservar o estado observado e pedir ação explícita.
8. Em crash entre escrita e confirmação, reconciliar journal e estado observado na próxima inicialização, preferindo suspensão em caso de ambiguidade.

### Observação e preferência do usuário [PROPOSTO]

- Modificação externa **detectável** em qualquer um dos dois valores gera `MANUAL_PAUSE` persistente, mesmo se o resultado ficar misto (ex.: apps escuro/sistema claro).
- Escritas feitas pelo Ambient Theme Controller devem ser correlacionadas por operação e snapshot, **não apenas por atraso em milissegundos**. Não existe prova perfeita de autoria a partir da notificação do Registro.
- Ao reativar manualmente depois de um estado misto, o usuário explicitamente devolve controle para o aplicativo: o próximo ciclo válido pode normalizar ambos os valores para um mesmo tema, após validação do sensor e das regras de cooldown/histerese.
- Se houver **tema de contraste ativo**, suspender aplicação automática sem substituir as configurações de acessibilidade. A detecção precisa de teste da API Win32/Windows no Windows 11 usado pelo produto. Troca do tema de contraste deve ser interpretada como intervenção externa.

### Testes mínimos da ADR-011 [PENDENTE]

- Windows inicialmente com claro/claro, escuro/escuro e **misto**; confirmar que mudança só ocorre com controle AUTO autorizado.
- Mudança interna para claro e escuro: ler ambos os DWORDs e confirmar ausência de autopausa.
- Alteração manual só de apps, só do sistema e de ambos durante monitoramento ativo ou reconnect: `MANUAL_PAUSE` até retomada explícita.
- Simular falha após primeira escrita, processo interrompido e concorrência com outro alterador: não entrar em loop nem reverter decisão externa sem evidência.
- Avaliar Explorer, taskbar, menu Iniciar, Configurações e três aplicativos comuns que seguem/não seguem preferências do Windows; documentar superfícies não atualizadas.
- Ativar tema de contraste, restrições de domínio e ausência/corrupção das chaves; não forçar acessibilidade, não solicitar administração como solução padrão.

**Decisão resolvida pela ADR-012:** presets + thresholds editáveis no MVP; valores e algoritmo permanecem sujeitos a experimentos.

## 19. Calibração e regras de estabilidade — ADR-012 [DECIDIDO/PENDENTE]

### Escopo aprovado em 22/10/2026

**Decisão aprovada:** usar **presets e thresholds editáveis** no MVP. Não exigir calibração guiada, ML, aprendizado de preferências, perfis por cômodo ou nuvem. Valores concretos e tempo de permanência ainda são propostas para avaliar em testes de campo.

### Presets iniciais de referência [PROPOSTO — NÃO VALIDADO]

| Perfil no Windows | `darkBelowLux` | `lightAboveLux` | Intenção |
|---|---:|---:|---|
| Escuro mais cedo | 60 lx | 150 lx | Favorece escuro em iluminação moderada |
| Equilibrado (padrão) | 30 lx | 80 lx | Ponto inicial neutro, sem pretensão médica |
| Claro por mais tempo | 15 lx | 50 lx | Evita escuro enquanto ainda houver alguma iluminação |
| Personalizado | Valor editado | Valor editado | Nunca sobrescrever ajuste do usuário por atualizar preset |

- A escolha inicial precisa respeitar o tema já observado do Windows e a intenção do usuário. **Entre limiares, preservar o tema**. Não forçar alternância só por iniciar sessão, abrir o aplicativo ou escolher preset se a luminosidade não decidir.
- UI: escolher preset, mostrar ambos os limiares (lux), apresentar prévia de recomendações baseada em leitura **válida**, botão de restaurar preset, acessibilidade e explicação simples (“escuro abaixo de…; claro acima de…”).
- Se o usuário altera um limiar, marcar `PERSONALIZED`, guardando ambos. Evitar reset implícito após upgrade ou reconexão. Guardar localmente, de preferência com versão de esquema e leitura tolerante de versões antigas.
- A medição tem limitações: depende de posição do Android (bolso, mesa, em frente a lâmpada), do fabricante, da faixa/resolução e da confiabilidade em tela apagada. Oferecer orientação para posicionar o aparelho próximo ao ambiente visual do monitor, sem prometer precisão de luxímetro.

### Algoritmo v1 [PROPOSTO]

1. **Validar origem e sessão**: `deviceId` autenticado, versão suportada, `sensorStatus`, unidade `lx`, lux finito/não negativo, ID/seq de amostra física e idade de medição. Nunca confundir `heartbeat`/status reemitido com nova amostra física.
2. **Verificar confiança da leitura**: evento gerado na sessão atual (ou inicialmente adquirido na ativação do sensor, quando comprovado), sem relógios cruzados Windows/Android para calcular idade. Android reporta idade estimada da amostra a partir de relógio monotônico; Windows usa relógio monotônico **local** para dwell/cooldown. Testar reinício Android, retomada de serviço, sensor *on-change* e tela apagada.
3. **Atenuar ruído sem falsificar eventos**: para MVP, usar inicialmente o **último valor fisicamente observado** mais permanência temporal do candidato. Mediana/média móvel somente se testes mostrarem oscilação; se adotadas, limitar janela por **tempo**, não por “últimos 5 eventos” que podem estar separados por horas.
4. **Avaliar candidato com histerese**: se `lux < darkBelowLux`, `DARK`; se `lux > lightAboveLux`, `LIGHT`; caso contrário `HOLD`. `HOLD` limpa candidato pendente e não gera escrita.
5. **Exigir permanência**: proposta inicial de ~30 s **de condição confiável** antes de recomendar/aplicar. Sensor *on-change* não precisa emitir N eventos idênticos; tempo transcorrido por si só **não prova** que o sensor permaneceu operando com tela bloqueada. O gate depende do estado de confiabilidade da fonte.
6. **Cooldown**: proposta inicial de ~10 min entre **trocas automáticas efetivamente aplicadas**. Não usar cooldown para impedir retomada manual, atualização de preset ou notificações; ao retomar, recalcular recomendação e seguir política aprovada.
7. **Verificação final**: `DesiredMode=AUTO`, `RuntimeState` saudável/estabilizado, fonte confiável, sem pausa manual ou alteração externa pendente, sem tema de contraste/política impeditiva e tema destino diferente do observado. Só o `AutomationCoordinator` autoriza o `WindowsThemeAdapter` a escrever ambos os DWORDs.

### Semântica de ausência de eventos [PROPOSTO — PRECISA DE PoC]

- O sensor Android `TYPE_LIGHT` é **on-change** e normalmente **non-wake-up**: ausência de novos eventos pode significar ambiente estável, tela apagada/suspensão ou indisponibilidade. Não definir “falha de sensor” apenas por 30 s sem evento.
- `SENSOR_STATUS` informa atividade do serviço, registro do listener, modo da tela e limitações conhecidas, **sem** representar nova leitura. Guardar a idade real do último `SensorEvent.timestamp`, que usa base monotônica do Android; nunca alterá-la só porque uma mensagem WebSocket foi enviada.
- Após reconectar, exigir ao menos um evento físico novo da ativação/sessão, se disponibilizado, antes de liberar mudança. Para `on-change`, a especificação AOSP prevê um evento inicial quando o sensor é ativado, mas **validar isso nos aparelhos reais**, inclusive após bloqueio e reabertura do serviço.
- Se a tela apagar e a plataforma não assegurar acompanhamento do sensor, classificar `UNCERTAIN` e **suspender mudanças automáticas** até uma nova fonte comprovadamente confiável. Continuar preservando o tema e manter reconexão de rede separada de saúde do sensor. Não fingir que amostra histórica é atualização atual.

### Testes de aceitação do motor [PENDENTE]

- Amostras `5, 25, 50, 100, 200 lx` como conjunto **simulado** da PoC de transporte: verificar decisões determinísticas e valores limítrofes; não tratá-las como calibração ambiental.
- Alternância rápida ao redor de 30/80; `HOLD` mantém o tema; cooldown bloqueia alternância curta.
- Preset alterado para personalizado, persistência após reinício e restauração explícita do preset original.
- Jitter de WebSocket, pacotes em atraso, sequência repetida, idade incoerente, `NaN`, negativos e lux além do alcance declarado.
- Sensor sem eventos num cômodo parado vs. sensor suspenso com tela apagada; documentar quais sinais permitem diferenciar (não inventar garantias).
- Android cai/reconecta enquanto há candidato pendente; recomeçar janela de estabilidade sem usar amostras de sessão anterior.
- Alteração manual externa durante cooldown/reconexão: entra `MANUAL_PAUSE`, nunca retoma automaticamente.
- Alteração de perfil nunca força tema de contraste nem supera falha de escrita parcial no Windows.

### Referências para esta decisão

- Android, leitura `TYPE_LIGHT` em lx: https://developer.android.com/develop/sensors-and-location/sensors/sensors_environment
- Android AOSP, `TYPE_LIGHT` como on-change/non-wake-up: https://source.android.com/docs/core/interaction/sensors/sensor-types
- Android AOSP, primeiro evento de sensores on-change após ativação: https://source.android.com/docs/core/interaction/sensors/report-modes
- Android, timestamp do evento: https://developer.android.com/reference/android/hardware/SensorEvent
- Android, restrições de sensores em background: https://developer.android.com/develop/sensors-and-location/sensors/sensors_overview
- NN/g, tema claro/escuro e preferências de acessibilidade: https://www.nngroup.com/articles/dark-mode/

## 20. Referências oficiais consultadas

- Microsoft: `ClientWebSocket` e PING/PONG keep-alive (.NET 9+): https://learn.microsoft.com/dotnet/fundamentals/networking/websockets
- Microsoft: valores de tema Windows: https://learn.microsoft.com/en-us/windows/apps/develop/settings/settings-common
- Microsoft: hotspot móvel Windows: https://support.microsoft.com/en-us/windows/experience/connectivity-networking/use-your-windows-device-as-a-mobile-hotspot
- Android: visão geral de sensores/background: https://developer.android.com/develop/sensors-and-location/sensors/sensors_overview
- Android: tipos de foreground service: https://developer.android.com/develop/background-work/services/fgs/service-types
- Android: restrições para iniciar foreground service: https://developer.android.com/develop/background-work/services/fgs/restrictions-bg-start
- Android: `NsdManager`: https://developer.android.com/reference/android/net/nsd/NsdManager
- Android: `TYPE_LIGHT` e restrições de sensor em background: https://developer.android.com/develop/sensors-and-location/sensors/sensors_overview
- Android: Android Keystore: https://developer.android.com/privacy-and-security/keystore
- Microsoft: DPAPI / `ProtectedData`: https://learn.microsoft.com/dotnet/api/system.security.cryptography.protecteddata
- Android: alterações de rede: https://developer.android.com/develop/connectivity/network-ops/reading-network-state
- Android: acesso à rede local no Android 17: https://developer.android.com/privacy-and-security/local-network-permission




- Microsoft: valores `AppsUseLightTheme` e `SystemUsesLightTheme`: https://learn.microsoft.com/en-us/windows/apps/develop/settings/settings-common
- Microsoft: dark/light mode e compatibilidade Win32: https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-windows-themes
- Microsoft: temas de contraste/acessibilidade: https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes
- Microsoft: `WM_SETTINGCHANGE` (não é garantia de repaint universal): https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-settingchange

---

**Estado:** conteúdo anterior preservado como histórico técnico. A consolidação Open Core e o encerramento formal do brainstorm estão nas seções 21–29. Ainda não houve implementação, publicação ou commit.


## 21. Encerramento do brainstorm: visão e limites de produto

**Decisão de produto em 22/10/2026: brainstorm encerrado para revisão do design.** O usuário aprovou a direção Free + Premium e deixou a conectividade remota por RelayControl **para uma das últimas implementações**. O fechamento do brainstorm **não** valida testes Android/Windows ainda pendentes nem aprova execução de código.

**Proposta de valor:** transformar o celular Android num sensor externo de luminosidade para recomendar e, quando autorizado, alternar **juntos** o tema dos aplicativos e do sistema Windows 11 conforme luz do ambiente, com controle local, sem conta nem internet. O recurso Premium futuro remove a exigência de alcance IP local por um caminho remoto autenticado quando existir internet e política de rede permissiva.

**Critérios de sucesso do produto:**

1. Free totalmente útil sem Premium, login, assinatura, nuvem, root ou administrador; build reproduzível/auditável pelo público (sujeito a validação de reprodutibilidade no F-Droid).
2. Pareamento e medição confiáveis na topologia PoC Windows hotspot → Android e nos demais meios quando houver rota.
3. O Windows nunca sobrepõe mudança manual de tema; desconexão suspende e notifica; reconexão recupera somente após estabilidade e sem pausa manual.
4. Nenhuma imagem de câmera trafega pela rede; câmera é fonte opt-in posterior, com limitações claras de background.
5. Compra Premium é única na Google Play, sem sistema próprio de pagamentos; mecanismo de autorização remota não implica checkout próprio.
6. O uso de RelayControl é evolução isolada: falha/indisponibilidade do cloud não interfere no funcionamento local Free.

### Divisão de capacidades e custeio

| Aspecto | Free (GitHub/F-Droid) | Premium (Google Play, futuro) |
|---|---|---|
| Código Android | Flutter + Kotlin 100% públicos; sem módulo proprietário | Mesmo core público mais módulo privado |
| Motor de lux + calibração | Incluído | Incluído |
| Sensor Android / câmera configurável | Sensor no MVP; câmera opcional posterior | Mesma política; **não** bloquear sensor físico atrás de paywall |
| WebSocket local e hotspots | Incluído, sem conta/nuvem | Incluído, nunca exige RelayControl para uso local |
| Cliente desktop Windows | Open source, controle de tema e motor completos | Mesmo cliente desktop, podendo receber canal remoto autenticado |
| Conectividade distante / sem rota LAN | Não faz parte do Free | RelayControl / canal remoto, fase final de roadmap |
| Dados e privacidade | Local-first | Transmissão via backend com consentimento explícito e política de retenção mínima |
| Receita | Sem cobrança | Compra única da aplicação na Play; custo de operação de relay deve ser medido antes da liberação |

**Não metas:** transformar o Android em controlador global de tema sem permissões especiais; garantir lux com tela apagada sem PoC; impedir toda redistribuição não autorizada de APK premium; vencer isolamento corporativo; construir outro backend/cloud antes de haver demanda.

## 22. Arquitetura Open Core e isolamento de código — ADR-013/019/020

### Diretrizes consolidadas

- **Core Flutter público:** telas comuns, domínio de medição, abstração de transporte, descoberta local, presets, diagnóstico, configuração, exportação/importação e integrações nativas de sensores.
- **Windows público:** `AutomationCoordinator`, `WindowsThemeAdapter`, motor de decisão, cliente WebSocket e interface Avalonia. O transporte remoto futuro é **um adaptador opcional** que não modifica regras de preferência/manual pause.
- **Premium privado (Android):** orquestração de sessão remota, integração de direito de uso e configuração de caminho RelayControl. Não copiar arquivos privados para o repositório Free, nem condicionar seu build a dependências privadas.
- **Builds separados:** um pipeline público `free` a partir de commit/tag público, com testes; outro privado `premium` consumindo versão/tag imutável do core e assinando/publicando AAB para a Play. O pipeline privado deve impedir vazamento de fontes, artefatos e secrets em logs/releases públicos.
- **Dependências:** F-Droid precisa compilar o Free integralmente com ferramentas e bibliotecas aceitas pelo projeto F-Droid. Nem SDK Play Services/Billing obrigatório no Free, nem stub proprietário distribuído publicamente. O módulo premium não deve estar presente no código compilado Free, não apenas oculto por flags.
- **Licença open source:** escolher licença explícita e compatível com dependências e exigências do F-Droid; a licença da Free não deve ser tratada como licença do repositório Premium.
- **Compatibilidade:** definir interfaces públicas de extensão e um contrato de protocolo versionado; o Free não importa tipos de implementação Premium. Mudanças de schema dos settings devem preservar leitura por edições antigas.

**Estrutura conceitual (não scaffolding):** repositório Android público (`lib/core`, `android/native`, `lib/local`, `lib/settings`), repositório Premium privado com módulo/extensão e orquestração de release, repositório Windows público e módulo posterior no RelayControl. Nenhum detalhe de árvore é requisito de implementação nesta fase.

### Ameaças e mitigação simples

- Build Free recebe alguma referência/arquivo de Premium → job público de checkout limpo e análise de dependências/artefatos.
- APK Free assinado com chave incorreta → conferir fingerprint SHA-256 do certificado em **APK final** de cada canal.
- Vazamento de app-signing key pela CI pública → assinatura fora do build de terceiros; segredo em ambiente protegido, com acesso mínimo e procedimentos de rotação/backup. Não incluir chave privada no Git.
- APK Premium redistribuído → store paga impede distribuição oficial gratuita; **não é DRM**, logo o backend remoto não confia só em `buildFlavor=premium` ou na assinatura do APK.

## 23. Identidade Android e assinatura cross-store — ADR-014

**Intenção confirmada:** `applicationId` único (exemplo `dev.hugodotnet.ambienttheme`) nas edições Free e Premium; atualização *in-place* para manter arquivos internos, preferências e banco.

### Regras oficiais verificadas

- Android exige **mesmo package/applicationId e assinatura compatível**, junto a `versionCode` aceito, para atualização instalada sem desinstalação. `applicationId` sozinho não transfere dados.
- **Google Play App Signing:** chave de assinatura distribuída aos usuários (*app signing key*) é diferente da **upload key** usada para envio de AAB. Para compartilhar assinatura, gerar e preservar uma **app signing key própria** e fornecer essa chave à Google na adesão ao Play App Signing. A Play assina os APKs de distribuição com essa chave; APKs GitHub devem usar **a mesma chave de assinatura de distribuição**, não apenas a upload key.
- **F-Droid oficial:** normalmente assina com chaves F-Droid, incompatíveis com Play/GitHub. O caminho pretendido usa **Reproducible Builds**, com verificação do APK e publicação do APK assinado pelo desenvolvedor (`Binaries`/`Builds.binary`, `AllowedAPKSigningKeys` quando aplicável). É preciso aprovar/validar metadados e reprodutibilidade reais, inclusive para Flutter.
- O F-Droid também pode fornecer variantes assinadas por F-Droid em alguns cenários; para prometer continuidade, orientar **explicitamente** qual variante/certificado foi instalada. Não presumir que todo download chamado “F-Droid” usa a nossa chave.

### Migração Free → Premium desejada (não garantida)

1. Usuário instala Free via GitHub/F-Droid e configura aparelho, pareamento e presets.
2. Compra na Google Play a **mesma aplicação paga**, com mesmo `applicationId` e certificado distribuído, sem instalar uma segunda variante em paralelo.
3. Play oferece APK com `versionCode` **superior** ao instalado e entrega a versão Premium *in-place*, se a loja permitir o fluxo de compra/atualização daquele install source.
4. Core lê o mesmo banco/settings e migra schema sem apagar dados; credenciais do Android Keystore só são preservadas sem reinstalação, e podem exigir novo pareamento se a migração recair em export/import.
5. Validar com conta real de teste e com dispositivo físico: **a existência de um APK de outra origem já instalado pode afetar a UX de compra da Play**. Não declarar o passo 3 garantido sem reproduzi-lo.

**Plano B obrigatório:** exportar configurações não sensíveis num arquivo versionado (presets, thresholds, preferências e informações de dispositivos autorizadas), comprar/instalar Premium com novo install quando necessário e importar após reinstalar. **Nunca exportar tokens, chaves privadas ou segredos do Keystore em texto claro**. Restaurar o pareamento de maneira segura; explicar que algumas credenciais não são migráveis.

### Gates antes da primeira publicação

- Assinatura SHA-256 idêntica em APKs GitHub, F-Droid developer-signed e Play-installed.
- Instalar Free → atualizar Premium → confirmar versionCode e dados persistidos → instalar Free futura (testar risco de substituição involuntária).
- Fluxo de compra Play com Free pré-instalada; sem compra, com compra, após reembolso, Play ausente e app sem conta conectada.
- Validar exigências atuais Play/F-Droid sobre AAB, produtos pagos, builds reproduzíveis, SDKs e políticas.

## 24. Versionamento multiloja e migração de dados — ADR-015

**Regra:** versionamento coordenado, monotônico por release, sem collisions. `versionName` é rótulo; `versionCode` é número Android que controla possibilidade de atualização e bloqueio de downgrade.

**Proposta inicial para testes (não decisão sobre valor exato):** uma linha de release partilhada `R`, com `free = R×10` e `premium = R×10+5` para build do mesmo release; tags de base/derivações imutáveis. Assim Play Premium `105` pode substituir Free `100` no release `R=10`, sem que um Free `100` sobreponha Premium `105`.

**Limite impossível de eliminar só com fórmula:** Free do release seguinte `110` tem versionCode maior que Premium anterior `105`. Como o appId e certificado são iguais, outro gerenciador de pacotes pode oferecê-lo e substituir a edição Premium por Free. Nenhuma fórmula simples crescente oferece proteção eterna a todas as versões antigas; exigir gestão de canal/UX de updates e testes com F-Droid e Play. Se a preservação de Premium diante de qualquer atualização cruzada for absoluta, precisaríamos reconsiderar a identidade de pacote/arquitetura, contrariando a meta atual.

- CI rejeita `versionCode` reaproveitado pela Play, `versionCode` incompatível e artefatos fora da matriz Free/Premium.
- Não confiar em `versionName`, flavor ou data de build para selecionar entitlement Premium; não sobrescrever estado de dados com defaults ao alternar edição.
- Schemas devem ser versionados; import/export deve suportar migração declarada, sem rollback silencioso.
- APK F-Droid não pode conter feature Premium mesmo se instalar sobre Premium. A interface deve informar claramente qual edição está ativa, e atualização de outra loja pode remover funcionalidades Premium daquela instalação.

## 25. Cobrança única e verificação de acesso remoto — ADR-016/018

**Escolha de produto:** aplicativo **pago uma única vez na Google Play**, sem assinaturas, sem checkout customizado e sem servidor próprio de pagamentos. **A listagem Play nasce paga**: após tornar um app disponível gratuitamente na Play, não é permitido convertê-lo para pago com o mesmo package name. A versão Free é distribuída por **GitHub/F-Droid**, não como listagem gratuita Play sob o mesmo id.

**Riscos que impedem promessas de licenciamento automático:**

- Adquirir APK Premium e modificá-lo ou redistribuí-lo é possível. Assinatura idêntica a Free não comprova **compra**.
- Google Play fornece sinais de licença como `appLicensingVerdict=LICENSED` em Play Integrity, mas `UNLICENSED` ou `UNEVALUATED` precisam de UX e tratamento apropriados (por exemplo, dispositivo sem Play, instalação fora da Play, app desconhecido).
- Para liberar recurso que consome o RelayControl, verificar autorização **no servidor**, ligando uma prova fresca e validada de direito de uso à identidade da sessão/dispositivo. Não aceitar alegação arbitrária enviada pelo cliente (`isPremium:true`). Esta é uma **integração de entitlement**, não um backend próprio de checkout; a implementação exata/quotas estão **pendentes de design e política**.
- Um app pago uma vez pode consumir custos de VPS, bandwidth, observabilidade e abuso indefinidamente. Antes de habilitar o remoto, medir custo real e definir limite justo de conexões/sessões, taxa e duração, deixando preço e sustentabilidade explícitos. Não supor que pagamento único financia relay ilimitado.
- Não introduzir Google Play Integrity/Billing obrigatório na edição Free nem no transporte local. Sem internet, o comportamento local continua intacto em todas as edições.

**Bloqueio antes do lançamento Premium remoto:** validação oficial Play entitlement em aparelhos reais, políticas de armazenamento/privacidade e reembolso, defesa contra replay, limites/rate limits, falha da Play e protocolo seguro entre Android/Windows/RelayControl. Não assumir que Play Integrity é licença perfeita em todos os aparelhos.

## 26. RelayControl como Premium remoto **somente no fim do roadmap** — ADR-017

### Evidência de reuso (repositório consultado)

Repositório `vitorhugo-dotnet/dotnet_RelayControl`, README inspecionado em 22/10/2026: control plane de **SonicRelay** e **FrameRelay** em ASP.NET Core/.NET 10, com identidade de dispositivo sem conta humana, pareamento revogável, sessões, join codes, signaling WebSocket autenticado, Postgres/Redis e credenciais TURN. O README separa **signaling de mídia** e cita relay especializado de H.264/Opus para Discord; ele **não prova** existir um canal genérico de retransmissão de mensagens Ambient Theme Controller.

### Dois fluxos distintos

**Free (já priorizado e parcialmente validado):** Windows ClientWebSocket → Android WebSocket server por rede local roteável; JSON lux/ACK, sem RelayControl.

**Premium remoto futuro (arquitetura preliminar, não implementada):**

```text
Android Premium ── outbound WSS ──┐
                                  ├── RelayControl (sessão autenticada)
Windows público ── outbound WSS ──┘
                                   ↳ encaminha mensagens de luz/status
                                   ↳ não armazena histórico de lux por padrão
```

- **Signaling** negocia participantes, credenciais e sessão. Ele **não** torna acessível um WebSocket Android privado em outra rede quando ambos estão atrás de NAT/firewall.
- Se conexão direta IP for viável, continuar no modo local/direto. Para dispositivos distantes/restritivos, **cada lado cria WSS de saída ao RelayControl**, e um **relay autorizado de mensagens leves** encaminha lux/status (não fotos/vídeo). Isso exige componente específico de roteamento/conexões e política de limites; o signaling existente sozinho não satisfaz o caso de uso.
- Avaliação futura: reusar o signaling + infraestrutura atual e estender com **canal de dados leve**; evitar TURN/WebRTC para meia dúzia de números enquanto WSS relay resolve. O serviço de mídia Discord é especializado e não deve ser assumido como pronto para isso.
- Em rede corporativa, saída HTTPS/WSS também pode ser bloqueada; **não prometer contorno de ACL, proxy ou políticas empresariais**.
- Identidade continua sem conta humana; sessões e associações precisam ser autorizadas e revogáveis; o serviço só aceita a capacidade Premium quando validada.
- Preferência de privacidade: TLS em trânsito, mínimo de logs/retention, nenhum armazenamento de lux por padrão. **TLS até RelayControl não equivale a criptografia ponta a ponta**. Se E2EE for exigida posteriormente, especificar contrato, geração/distribuição de chaves e rotação, sem inventar cifra.
- O motor de decisão e a escrita do Registro **continuam no Windows**. Mensagens remotas não podem mandar executar alterações de tema por conta própria; preservam-se as ADRs 003, 007, 010, 011 e 012 sem bifurcar as regras.

### Posicionamento temporal

1. **MVP Free:** Android sensor real, Windows theme, hotspots, WebSocket, pareamento, watchdog, prescrições de calibração.
2. **Estabilização e lançamento Free:** onboarding, diagnóstico e segurança de LAN, F-Droid/GitHub, pipeline reproduzível/assinatura, exportação/importação.
3. **Play paga base Premium (se validado):** embalagem Open Core privada, compra única e testes de migração, sem exigir RelayControl para uso local.
4. **Últimas implementações:** integrar remoto com RelayControl, autorização/entitlement, quotas, segurança, observabilidade, custos e testes com redes distintas.

## 27. Entrega de rede e estados unificados

**Seleção de transporte (proposta):** `LocalDirect` preferido quando disponível; `RemoteRelay` somente se Premium autorizado, explicitamente habilitado e rede local indisponível ou dispositivo distante. Não mudar silenciosamente para um caminho cloud quando o usuário escolheu local-only. Ambos devem expor a mesma interface de eventos de leitura, qualidade e conexão para o Windows.

**Regras já decididas, válidas nos dois caminhos:**

- Estado de intenção `AUTO` ou `PAUSED_MANUALLY`/`RECOMMEND_ONLY`; estados técnicos de conexão independentes.
- Desconexão Android/fonte incerta: suspender mudanças, preservar tema, notificar quando falha for confirmada; retry de transporte sem alternância de tema.
- Após recuperar autenticação e **medição confiável estável**, retomar automaticamente **somente** se usuário não pausou manualmente.
- Mudança externa detectável nos valores `AppsUseLightTheme`/`SystemUsesLightTheme`: pausar até retomada explícita; impedir autopausa por operação própria.
- Tema do sistema e de aplicativos sempre juntos no MVP; respeitar acessibilidade/tema de contraste e limitações de apps de terceiros.
- Presets, thresholds editáveis, histerese, dwell e cooldown independentes da rede; valores 30/80, 30s, 10min permanecem hipóteses de testes, não garantias.
- `TYPE_LIGHT` on-change pode ficar sem evento novo quando o ambiente não muda; heartbeat não é medição. Quando confiabilidade for incerta, não aplicar nova mudança.

## 28. Matriz mínima de verificação antes do desenvolvimento e lançamento

| Gate | Critério | Status em 22/10/2026 |
|---|---|---|
| Transporte local hotspot Windows | Troca WebSocket bidirecional com lux simulado | **VALIDADO** na PoC de usuário |
| Redes distintas sem rota | Timeout; diagnóstico não deve acusar firewall sem prova | **VALIDADO** cenário negativo |
| Sensor físico Android | Leitura, faixa, timestamp, on-change, bloqueio de tela, fabricante | **PENDENTE** |
| Foreground service Android | Permissões atuais, consumo, leitura com app/tela desligados, reinício | **PENDENTE** |
| Troca de tema Windows | Dois valores, UI real, casos de falha parcial, alterações externas | **PENDENTE** |
| Descoberta e pareamento | mDNS, QR, TLS, pinning, revogação, troca de rede | **PENDENTE** |
| Alternativas de rede | Android hotspot, USB tethering e BLE | **PENDENTE/FUTURO** |
| F-Droid/GitHub/Play assinatura | SHA-256 idêntico nas três origens, reprodutibilidade | **PENDENTE, CRÍTICO** |
| Migração in-place | Comprar e instalar Play com Free pré-instalada sem perder dados | **PENDENTE, CRÍTICO** |
| Versionamento multiloja | Proteger a experiência contra substituição cross-store | **PENDENTE, CRÍTICO** |
| Remoto RelayControl | Novos fluxos genéricos WSS, auth, entitlement, quotas, política de rede | **FUTURO, NÃO IMPLEMENTADO** |
| Custo de Premium | Cota de banda, carga, sessão, abuso, preço único sustentável | **FUTURO, BLOQUEADOR DE LANÇAMENTO REMOTO** |

## 29. Status final e handoff

- **Brainstorm:** encerrado por solicitação do usuário em 22/10/2026; todas as escolhas de produto consideradas suficientes para escrever especificação/planejamento sem introduzir novas decisões de escopo.
- **Especificação:** este documento consolida decisões e oferece o desenho arquitetural **para revisão**. Não é evidência de implementação, compatibilidade universal ou aprovação técnica dos pontos [PROPOSTO].
- **Implementação:** **não iniciada** e **não autorizada**; após revisão e aprovação do design, produzir um plano de implementação separado antes de escrever código.
- **Repositório:** documentação inicial publicada na branch `main`; nenhum código de produto, release ou configuração de Play Console/F-Droid foi implementado.

### Fontes normativas e documentação consultadas para o fechamento

- Google Android, uso da mesma chave de assinatura em várias lojas: https://developer.android.com/guide/app-bundle/faq
- Google Android, assinatura do app / Play App Signing: https://developer.android.com/studio/publish/app-signing
- Android, `versionCode`/restrições de downgrade: https://developer.android.com/studio/publish/versioning
- Google Play Console, regras de free → paid: https://support.google.com/googleplay/android-developer/answer/6334373
- F-Droid, Reproducible Builds/developer-signed APK: https://fdroid.gitlab.io/jekyll-fdroid/en/docs/Reproducible_Builds/
- F-Droid, FAQ para desenvolvedores: https://fdroid.gitlab.io/jekyll-fdroid/en/docs/FAQ_-_App_Developers/
- Google Play Integrity (entitlement): https://developer.android.com/google/play/integrity/verdicts
- Google Play, remediação/licenciamento: https://developer.android.com/google/play/integrity/remediation
- RelayControl, repositório inspecionado: https://github.com/vitorhugo-dotnet/dotnet_RelayControl

## 29. Política de licenciamento de código — ADR-021 [DECIDIDO]

- O código do núcleo **Free** disponibilizado neste repositório é licenciado sob **Apache License 2.0 (SPDX `Apache-2.0`)**, conforme o arquivo raiz [LICENSE](../../../LICENSE). A concessão de direitos e eventuais obrigações da Apache-2.0 permanecem válidas mesmo quando esse código é utilizado na distribuição Premium.
- O **código adicional proprietário Premium**, mantido exclusivamente em repositório privado, possui licença comercial própria, descrita publicamente em [LICENSE-PREMIUM.md](../../../LICENSE-PREMIUM.md). O documento público não disponibiliza o código Premium.
- Não se trata de relicenciar automaticamente o core aberto. São **componentes com regimes jurídicos distintos** dentro de uma aplicação distribuída em duas edições.
- A distribuição Free do F-Droid deve conter somente componentes/licenças compatíveis com software livre; políticas de compilação e assinatura precisam ser verificadas antes do release.
- A licença Premium é inicialmente uma minuta contratual; revisar juridicamente e adaptar às exigências brasileiras e da Google Play antes do lançamento comercial.
- A distribuição do Premium no Google Play não concede direitos sobre o código proprietário além da licença comercial; a aquisição não substitui autenticação/autorização do serviço remoto RelayControl.
- Consulta rápida: [LICENSING.md](../../../LICENSING.md) e [ADR-021](../../adr/2026-10-22-ambient-theme-controller-adrs.md).
