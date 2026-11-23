import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'logo.dart';
import 'session_state.dart';
import 'theme.dart';

void main() => runApp(const AmbientThemeApp());

class AmbientThemeApp extends StatelessWidget {
  const AmbientThemeApp({super.key, this.states});

  /// Injected in tests; the app listens to the native session otherwise.
  final Stream<SessionState>? states;

  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'Ambient Theme',
    debugShowCheckedModeBanner: false,
    theme: buildTheme(Brightness.light),
    darkTheme: buildTheme(Brightness.dark),
    home: SessionPage(states: states),
  );
}

class SessionPage extends StatefulWidget {
  const SessionPage({super.key, this.states});

  final Stream<SessionState>? states;

  @override
  State<SessionPage> createState() => _SessionPageState();
}

class _SessionPageState extends State<SessionPage> {
  static const _control = MethodChannel('ambienttheme/control');
  static const _state = EventChannel('ambienttheme/state');

  late final Stream<SessionState> _states = widget.states ??
      _state.receiveBroadcastStream().map((event) => SessionState.fromMap(event as Map<dynamic, dynamic>));

  final _inviteText = TextEditingController();

  @override
  void dispose() {
    _inviteText.dispose();
    super.dispose();
  }

  Future<void> _paste() async {
    final result = await _control.invokeMethod<String>('armInvite', _inviteText.text.trim()) ?? 'NO_SESSION';
    if (!mounted) return;
    if (result == 'ARMED') _inviteText.clear();
    _say(inviteMessage(result));
  }

  void _say(String message) => ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));

  Future<void> _scan() async {
    final result = await _control.invokeMethod<String>('scanInvite') ?? 'CANCELLED';
    if (!mounted) return;
    if (result != 'CANCELLED') _say(inviteMessage(result));
  }

  Future<void> _copy(String address) async {
    await Clipboard.setData(ClipboardData(text: address));
    if (mounted) _say('Endereço copiado: $address');
  }

  Future<void> _revoke(PairedComputer computer) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Revogar computador'),
        content: Text('${computer.name} deixará de receber leituras e precisará parear de novo.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancelar')),
          FilledButton(
            style: FilledButton.styleFrom(minimumSize: Size.zero),
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Revogar'),
          ),
        ],
      ),
    );
    if (confirmed == true) await _control.invokeMethod<void>('revoke', computer.id);
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    body: SafeArea(
      child: StreamBuilder<SessionState>(
        stream: _states,
        initialData: const SessionState(running: false),
        builder: (context, snapshot) {
          final state = snapshot.data ?? const SessionState(running: false);
          return ListView(
            padding: const EdgeInsets.fromLTRB(20, 16, 20, 32),
            children: [
              _Header(live: state.running),
              const SizedBox(height: 20),
              _Hero(
                state: state,
                onToggle: () => _control.invokeMethod<void>(state.running ? 'stop' : 'start'),
              ),
              const SizedBox(height: 14),
              if (state.running) ..._session(context, state),
            ],
          );
        },
      ),
    ),
  );

  List<Widget> _session(BuildContext context, SessionState state) {
    final text = Theme.of(context).textTheme;
    return [
      _Section(Icons.lan_outlined, 'Endereço para digitar no computador', [
        if (state.addresses.isEmpty)
          const Text('Sem rede local. Conecte ao Wi-Fi ou ao hotspot do computador.')
        else
          for (final address in state.addresses)
            Row(
              children: [
                Expanded(
                  child: SelectableText(
                    address,
                    style: text.titleLarge?.copyWith(fontFamily: 'monospace', fontWeight: FontWeight.w600),
                  ),
                ),
                IconButton(
                  tooltip: 'Copiar endereço',
                  icon: const Icon(Icons.copy_rounded),
                  onPressed: () => _copy(address),
                ),
              ],
            ),
        if (state.otherAddresses.isNotEmpty)
          _Detail('Outras interfaces (operadora, VPN): ${state.otherAddresses.join(', ')}. '
              'Em geral não alcançam o computador; use o endereço acima.'),
        const _Detail('Se a conexão não for concluída, a rede pode bloquear o tráfego entre os aparelhos. '
            'O aplicativo não contorna firewall nem políticas de rede.'),
      ]),
      _Section(Icons.qr_code_2_rounded, 'Parear', [
        const Text('No computador, clique em "Parear celular" e escaneie o código mostrado.'),
        const SizedBox(height: 12),
        FilledButton.tonalIcon(
          style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(48)),
          icon: const Icon(Icons.qr_code_scanner),
          label: const Text('Autorizar computador'),
          onPressed: _scan,
        ),
        ExpansionTile(
          tilePadding: EdgeInsets.zero,
          title: const Text('Sem câmera? Cole o texto do convite'),
          children: [
            TextField(
              key: const Key('inviteText'),
              controller: _inviteText,
              autocorrect: false,
              enableSuggestions: false,
              keyboardType: TextInputType.url,
              minLines: 3,
              maxLines: 5,
              decoration: const InputDecoration(hintText: 'ambienttheme://pair?...'),
            ),
            Align(
              alignment: Alignment.centerRight,
              child: TextButton(onPressed: _paste, child: const Text('Usar convite')),
            ),
          ],
        ),
        if (state.pendingInvite != null) _Detail('Convite de "${state.pendingInvite}" aguardando o computador.'),
        const SizedBox(height: 8),
        const Text('Código deste celular (compare com o que o computador mostrar):'),
        const SizedBox(height: 8),
        _CodeBox(state.displayCode ?? '—'),
      ]),
      _Section(Icons.sensors, 'Sensor', [
        Text(state.sensorText),
        if (state.sensorDescription != null) _Detail(state.sensorDescription!),
        _Detail('Eventos físicos recebidos: ${state.eventCount} · com a tela apagada: ${state.screenOffEvents}'),
        if (state.lastEventAgeMs != null) _Detail('Último evento há ${(state.lastEventAgeMs! / 1000).toStringAsFixed(0)} s'),
      ]),
      _Section(Icons.computer_rounded, 'Computadores autorizados', [
        Text(state.connectionText),
        _Detail(state.nsdName == null ? 'Descoberta automática indisponível nesta rede.' : 'Anunciado na rede como ${state.nsdName}.'),
        if (state.clients.isNotEmpty) const SizedBox(height: 4),
        for (final computer in state.clients)
          ListTile(
            contentPadding: EdgeInsets.zero,
            leading: Icon(
              Icons.circle,
              size: 12,
              color: toneColor(computer.id == state.connectedClientId ? Tone.ok : Tone.waiting),
            ),
            minLeadingWidth: 12,
            title: Text(computer.name),
            subtitle: Text(computer.id == state.connectedClientId ? 'Conectado agora' : 'Autorizado'),
            trailing: TextButton(onPressed: () => _revoke(computer), child: const Text('Revogar')),
          ),
      ]),
    ];
  }
}

class _Header extends StatelessWidget {
  const _Header({required this.live});

  final bool live;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Row(
      children: [
        SunMoonLogo(size: 44, animate: live, moonColor: theme.scaffoldBackgroundColor),
        const SizedBox(width: 14),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('Ambient Theme', style: theme.textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w700)),
              Text('Sensor de luz para o seu computador', style: theme.textTheme.bodySmall),
            ],
          ),
        ),
      ],
    );
  }
}

/// Live reading, status chips and the start/stop action.
class _Hero extends StatelessWidget {
  const _Hero({required this.state, required this.onToggle});

  final SessionState state;
  final VoidCallback onToggle;

  static String _sensorLabel(Tone tone) => switch (tone) {
    Tone.ok => 'Sensor ativo',
    Tone.waiting => 'Aguardando sensor',
    Tone.paused => 'Leitura incerta',
    Tone.problem => 'Sensor indisponível',
  };

  static String _connectionLabel(Tone tone) => switch (tone) {
    Tone.ok => 'Conectado',
    Tone.waiting => 'Aguardando computador',
    _ => 'Sem computador',
  };

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final dark = theme.brightness == Brightness.dark;
    return Container(
      padding: const EdgeInsets.all(22),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(24),
        border: Border.all(color: theme.colorScheme.outlineVariant),
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: dark
              ? const [Color(0xFF2B2618), Brand.nightCard]
              : const [Color(0xFFFFF1D2), Colors.white],
          stops: const [0, 0.6],
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (state.running) ...[
            Text('LUZ DO AMBIENTE', style: theme.textTheme.labelMedium?.copyWith(letterSpacing: 1.2)),
            AnimatedSwitcher(
              duration: const Duration(milliseconds: 250),
              child: Text(
                state.luxHeadline,
                key: ValueKey(state.luxHeadline),
                style: theme.textTheme.displayMedium?.copyWith(fontWeight: FontWeight.w800, letterSpacing: -1),
              ),
            ),
            const SizedBox(height: 10),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                _Chip(_sensorLabel(state.sensorTone), state.sensorTone),
                _Chip(_connectionLabel(state.connectionTone), state.connectionTone),
              ],
            ),
            const SizedBox(height: 18),
            OutlinedButton.icon(
              style: OutlinedButton.styleFrom(minimumSize: const Size.fromHeight(48)),
              icon: const Icon(Icons.stop_rounded),
              label: const Text('Parar sessão de sensor'),
              onPressed: onToggle,
            ),
          ] else ...[
            Text(
              'Este celular mede a luz do ambiente e envia para o seu computador pela rede local. '
              'Nada vai para a nuvem e não há conta.',
              style: theme.textTheme.bodyLarge,
            ),
            const SizedBox(height: 20),
            FilledButton.icon(
              icon: const Icon(Icons.sensors),
              label: const Text('Usar como sensor'),
              onPressed: onToggle,
            ),
          ],
        ],
      ),
    );
  }
}

class _Chip extends StatelessWidget {
  const _Chip(this.label, this.tone);

  final String label;
  final Tone tone;

  @override
  Widget build(BuildContext context) {
    final color = toneColor(tone);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
      decoration: BoxDecoration(color: color.withValues(alpha: 0.15), borderRadius: BorderRadius.circular(999)),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.circle, size: 8, color: color),
          const SizedBox(width: 6),
          Text(label, style: TextStyle(color: color, fontWeight: FontWeight.w600, fontSize: 12)),
        ],
      ),
    );
  }
}

class _CodeBox extends StatelessWidget {
  const _CodeBox(this.code);

  final String code;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.symmetric(vertical: 14),
      decoration: BoxDecoration(
        color: theme.scaffoldBackgroundColor,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: theme.colorScheme.outlineVariant),
      ),
      child: SelectableText(
        code,
        textAlign: TextAlign.center,
        style: theme.textTheme.titleLarge?.copyWith(fontFamily: 'monospace', fontWeight: FontWeight.w700, letterSpacing: 1),
      ),
    );
  }
}

class _Section extends StatelessWidget {
  const _Section(this.icon, this.title, this.children);

  final IconData icon;
  final String title;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(18),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(icon, size: 20, color: Brand.amber),
                const SizedBox(width: 10),
                Expanded(child: Text(title, style: theme.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w600))),
              ],
            ),
            const SizedBox(height: 10),
            ...children,
          ],
        ),
      ),
    );
  }
}

class _Detail extends StatelessWidget {
  const _Detail(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(top: 6),
    child: Text(text, style: Theme.of(context).textTheme.bodySmall),
  );
}
