# Ambient Theme Controller — Architecture Decision Records (consolidadas)

**Data:** 2026-10-22  
**Status:** decisões de produto fechadas; propostas técnicas pendentes de validação.  
**Fonte de verdade detalhada:** [`design arquitetural`](../superpowers/specs/2026-10-22-ambient-theme-controller-design.md).  
**Legenda:** Aprovada = decisão do usuário; técnica proposta = ainda necessita validação; PoC validada = evidência delimitada.

> **Aviso:** a documentação é publicada na `main`; o código de produto ainda não foi implementado. O fechamento do brainstorm não aprova automaticamente a execução.

## ADR-001 — Android como sensor externo e Windows como controlador
**Status:** Aprovada.  
**Decisão:** sensor Android informa lux; Windows decide/observa e altera seu tema; sem obrigar o Android a trocar tema global.  
**Consequência:** lógica de tema no desktop, sensor nativo no Android e protocolo portátil.

## ADR-002 — WebSocket local, Android servidor e Windows cliente
**Status:** Aprovada; transporte em hotspot validado.  
**Decisão:** Windows inicia ClientWebSocket, Android hospeda servidor; sem internet, sem root/admin, quando rede permite.  
**Evidência:** PoC Android Termux/Python e Windows PowerShell/.NET em hotspot Windows (`192.168.137.0/24`), dados JSON lux e ACK; não valida FGS/sensor real/TLS.  
**Consequência:** descoberta e segurança desenhadas nessa direção, sem prometer roteamento inexistente.

## ADR-003 — Falhas: suspender automação e notificar
**Status:** Aprovada.  
**Decisão:** Android indisponível ou medição incerta impede novas alterações; preserva último tema e notifica falha confirmada.  
**Consequência:** distinguir heartbeat da saúde do sensor, não usar amostra antiga como nova.

## ADR-004 — Stack e separação de componentes
**Status:** Proposta técnica.  
**Decisão:** Flutter+Kotlin/SensorManager Android; .NET+Avalonia Windows; separar fonte, descoberta, pareamento, conexão, decisão e tema.  
**Consequência:** trocar transporte sem bifurcar motor de decisão.

## ADR-005 — Descoberta progressiva e onboarding
**Status:** Proposta técnica.  
**Decisão:** mDNS/DNS-SD, QR de convite sem pressupor que QR descobre IP Android, endereço manual, orientações de hotspot.  
**Consequência:** VLAN/firewall/ausência de rota não são contornados; respeitar política corporativa.

## ADR-006 — Segurança local
**Status:** Proposta técnica.  
**Decisão:** WSS e pinning/identidade validada, autorização local explícita, token temporário de pareamento e revogação; secrets no SO; não aceitar TLS indiscriminadamente.  
**Consequência:** protocolo de pareamento exige PoC/ameaças, não crypto caseira.

## ADR-007 — Reconexão com retomada automática condicionada
**Status:** Aprovada.  
**Decisão:** depois de reconectar, verificar identidade, sensor e estabilidade; voltar ao automático **somente** sem pausa manual.  
**Consequência:** intenção do usuário e estado operacional devem ser independentes.

## ADR-008 — Câmera, USB e BLE
**Status:** Escopo futuro.  
**Decisão:** câmera como fonte configurável/fallback experimental; USB tethering e BLE como transportes potenciais ainda não validados.  
**Consequência:** não bloquear MVP por funcionalidades especulativas.

## ADR-009 — Um Android ativo por Windows no MVP
**Status:** Proposta técnica.  
**Decisão:** uma fonte ativa, múltiplos endpoints apenas como candidatos da mesma identidade.  
**Consequência:** evita arbitragem de sensors múltiplos.

## ADR-010 — Mudança externa de tema pausa até retomada explícita
**Status:** Aprovada.  
**Decisão:** mudança externa detectável nos temas Windows pausa automação. Alterações próprias precisam ser correlacionadas sem autopausa.  
**Consequência:** observação do Registro + verificação; autoria perfeita não é garantida.

## ADR-011 — Alterar juntos apps e sistema Windows
**Status:** Aprovada.  
**Decisão:** `AppsUseLightTheme` e `SystemUsesLightTheme` seguem mesma recomendação; falha parcial suspende.  
**Consequência:** não prometer atualização instantânea de todos os programas ou ignorar tema de contraste.

## ADR-012 — Presets e thresholds personalizáveis
**Status:** Aprovada para UX; limites concretos propostos.  
**Decisão:** presets ajustáveis e histerese, dwell/cooldown sem ML. Referências 30/80 lux e 30s/10min exigem teste.  
**Consequência:** persistir personalização e preservar tema na zona intermediária.

## ADR-013 — Open Core com Free open source e Premium privado
**Status:** Aprovada.  
**Decisão:** GitHub/F-Droid distribuem Free integralmente compilável; Google Play distribui Premium que reutiliza core Flutter público e adiciona implementação proprietária.  
**Consequência:** builds separados e dependências privadas ausentes do Free.

## ADR-014 — Um package ID + assinatura comum multiloja
**Status:** Aprovada como objetivo, sujeita a compatibilidade.  
**Decisão:** mesmo `applicationId` (ex.: `dev.hugodotnet.ambienttheme`) e **app signing key** de distribuição; Play App Signing com chave própria enviada à Google; GitHub com mesma chave; F-Droid developer-signed via Reproducible Builds. **Upload key da Play é outra chave.**  
**Consequência:** verificar fingerprints e compra/instalação de Play a partir da Free real; migração in-place **não garantida**.

## ADR-015 — VersionCode coordenado e export/import de contingência
**Status:** Aprovada como política; fórmula é proposta.  
**Decisão:** versões crescentes, sugestão `R×10` Free e `R×10+5` Premium, teste cruzado, export/import de settings como fallback.  
**Consequência:** versões Free futuras podem superar Premium antigas e substituí-las com a mesma assinatura; não prometer prevenção perfeita usando versionCode.

## ADR-016 — Compra única pela Play
**Status:** Aprovada, condicionada a testes de loja.  
**Decisão:** app Play pago **desde a primeira publicação**, sem checkout customizado/assinatura própria. Free publicada fora da Play.  
**Consequência:** após publicar gratuitamente uma app na Play, não é permitido convertê-la a paga no mesmo package; compra in-place a partir de outra origem exige validação.

## ADR-017 — RelayControl remoto somente nas últimas implementações
**Status:** Aprovada como escopo futuro.  
**Decisão:** reusar control plane RelayControl (`vitorhugo-dotnet/dotnet_RelayControl`) para autorização, pairing/sessões, signaling e **novo relay WSS leve** quando conexões diretas não forem possíveis.  
**Consequência:** signaling sozinho não atravessa NAT/firewall; ambos os clientes terão WSS de saída e o servidor deverá encaminhar lux/status autenticados. Sem promover relay antes de estabilizar Free.

## ADR-018 — Entitlement Premium no servidor remoto
**Status:** Proposta técnica, não implementada.  
**Decisão:** backend RelayControl verifica prova de direito obtida por mecanismo oficial Google Play (avaliar Play Integrity) antes de autorizar relay, com token fresco, quotas e revogação. **Não** confiar no flavor e não criar checkout customizado.  
**Consequência:** compra única não implica relay ilimitado; tratar aparelhos sem Play, reembolsos e `UNEVALUATED`.

## ADR-019 — Isolamento de código e pipelines
**Status:** Aprovada.  
**Decisão:** pipeline público não lê repo privado; pipeline privado obtém core público em tag imutável e inclui componente Premium; assinar/verificar cada canal; F-Droid sem dependências proprietárias.  
**Consequência:** auditoria de artefatos e chaves protegidas necessárias antes do release.

## ADR-020 — Free independente do Premium remoto
**Status:** Aprovada.  
**Decisão:** recursos locais, presets, sensor, diagnóstico e controle Windows não dependem de login/Play/RelayControl.  
**Consequência:** indisponibilidade e custo da nuvem afetam só a capacidade remota opcional.

## ADR-021 — Licenças separadas para componentes Free e Premium
**Status:** Aprovada como política Open Core, licença Free selecionada e termos Premium iniciais sujeitos a revisão jurídica.  
**Decisão:** licenciar o núcleo aberto e o código Free em **Apache-2.0**, com texto canônico em `LICENSE`. Manter a implementação proprietária Premium em repositório privado, sob condições de uso comercial descritas em `LICENSE-PREMIUM.md`. A distribuição Premium deve respeitar a Apache-2.0 aplicável ao core e todas as licenças de terceiros.  
**Consequência:** componentes separados em vez de "fechar" código já aberto; F-Droid pode compilar o Free sem código proprietário; revisar licença Premium e compatibilidade de dependências antes de vender; documentar matriz de escopo em `LICENSING.md`.

## Gates de validação preservados

1. Sensor Android real e leitura com tela bloqueada em dispositivos físicos.
2. Troca dos dois temas Windows e detecção de mudança externa sem autopausa.
3. Pareamento WSS/mDNS/QR manual com rotação e revogação.
4. Certificado comum Play/GitHub/F-Droid developer-signed; build reproduzível real.
5. Compra/atualização Play com APK Free previamente instalado, versionCodes cruzados e preservação de dados; fallback export/import.
6. Remoto RelayControl somente após entrega Free, com autorização Play, relay WSS real, limite de uso, custo e privacidade.

## Fontes prioritárias para as ADRs novas

- Assinatura Play e outras lojas: https://developer.android.com/guide/app-bundle/faq
- Play App Signing: https://developer.android.com/studio/publish/app-signing
- Versionamento Android: https://developer.android.com/studio/publish/versioning
- F-Droid developer-signed Reproducible Builds: https://fdroid.gitlab.io/jekyll-fdroid/en/docs/Reproducible_Builds/
- F-Droid FAQ de assinaturas: https://fdroid.gitlab.io/jekyll-fdroid/en/docs/FAQ_-_App_Developers/
- Google Play Free vs Paid: https://support.google.com/googleplay/android-developer/answer/6334373
- Play Integrity/licença: https://developer.android.com/google/play/integrity/verdicts
- RelayControl (README consultado): https://github.com/vitorhugo-dotnet/dotnet_RelayControl

**Próxima etapa do processo Superpowers:** revisão/aprovação deste design escrito; só então redigir plano de implementação. Documentação publicada; implementação de produto não iniciada.