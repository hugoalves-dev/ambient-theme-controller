import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'session_state.dart';

void main() => runApp(const AmbientThemeApp());

class AmbientThemeApp extends StatelessWidget {
  const AmbientThemeApp({super.key});

  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'Ambient Theme',
    theme: ThemeData(colorSchemeSeed: Colors.amber, brightness: Brightness.light),
    darkTheme: ThemeData(colorSchemeSeed: Colors.amber, brightness: Brightness.dark),
    home: const SessionPage(),
  );
}

class SessionPage extends StatefulWidget {
  const SessionPage({super.key});

  @override
  State<SessionPage> createState() => _SessionPageState();
}

class _SessionPageState extends State<SessionPage> {
  static const _control = MethodChannel('ambienttheme/control');
  static const _state = EventChannel('ambienttheme/state');

  late final Stream<SessionState> _states = _state
      .receiveBroadcastStream()
      .map((event) => SessionState.fromMap(event as Map<dynamic, dynamic>));

  final _inviteText = TextEditingController();

  @override
  void dispose() {
    _inviteText.dispose();
    super.dispose();
  }

  Future<void> _paste() async {
    final armed = await _control.invokeMethod<bool>('armInvite', _inviteText.text.trim()) ?? false;
    if (!mounted) return;
    if (armed) _inviteText.clear();
    _say(armed
        ? 'Computador autorizado a parear. Continue no computador.'
        : 'Texto não reconhecido ou convite expirado.');
  }

  void _say(String message) => ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));

  Future<void> _scan() async {
    final armed = await _control.invokeMethod<bool>('scanInvite') ?? false;
    if (!mounted) return;
    _say(armed
        ? 'Computador autorizado a parear. Continue no computador.'
        : 'Código não reconhecido ou convite expirado.');
  }

  Future<void> _revoke(PairedComputer computer) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Revogar computador'),
        content: Text('${computer.name} deixará de receber leituras e precisará parear de novo.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancelar')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Revogar')),
        ],
      ),
    );
    if (confirmed == true) await _control.invokeMethod<void>('revoke', computer.id);
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Ambient Theme')),
    body: StreamBuilder<SessionState>(
      stream: _states,
      initialData: const SessionState(running: false),
      builder: (context, snapshot) {
        final state = snapshot.data!;
        return ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Text(
              'Este celular mede a luz do ambiente e envia para o seu computador pela rede local. '
              'Nada vai para a nuvem e não há conta.',
              style: Theme.of(context).textTheme.bodyMedium,
            ),
            const SizedBox(height: 16),
            FilledButton.icon(
              icon: Icon(state.running ? Icons.stop : Icons.sensors),
              label: Text(state.running ? 'Parar sessão de sensor' : 'Usar como sensor'),
              onPressed: () => _control.invokeMethod<void>(state.running ? 'stop' : 'start'),
            ),
            if (state.running) ..._session(context, state),
          ],
        );
      },
    ),
  );

  List<Widget> _session(BuildContext context, SessionState state) => [
    const SizedBox(height: 16),
    _Section('Sensor', [
      Text(state.sensorText),
      if (state.sensorDescription != null) _Detail(state.sensorDescription!),
      _Detail('Eventos físicos recebidos: ${state.eventCount} · com a tela apagada: ${state.screenOffEvents}'),
      if (state.lastEventAgeMs != null) _Detail('Último evento há ${(state.lastEventAgeMs! / 1000).toStringAsFixed(0)} s'),
    ]),
    _Section('Computador', [
      Text(state.connectionText),
      _Detail(state.nsdName == null ? 'Descoberta automática indisponível nesta rede.' : 'Anunciado na rede como ${state.nsdName}.'),
    ]),
    _Section('Endereço para digitar no computador', [
      if (state.addresses.isEmpty)
        const Text('Sem rede local. Conecte ao Wi-Fi ou ao hotspot do computador.')
      else
        for (final address in state.addresses) SelectableText(address, style: Theme.of(context).textTheme.titleLarge),
      const _Detail('Se a conexão não for concluída, a rede pode bloquear o tráfego entre os aparelhos. '
          'O aplicativo não contorna firewall nem políticas de rede.'),
    ]),
    _Section('Parear', [
      const Text('No computador, clique em "Parear celular" e escaneie o código mostrado.'),
      const SizedBox(height: 8),
      OutlinedButton.icon(icon: const Icon(Icons.qr_code_scanner), label: const Text('Autorizar computador'), onPressed: _scan),
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
      SelectableText(state.displayCode ?? '—', style: Theme.of(context).textTheme.headlineSmall),
    ]),
    _Section('Computadores autorizados', [
      if (state.clients.isEmpty) const Text('Nenhum.'),
      for (final computer in state.clients)
        ListTile(
          contentPadding: EdgeInsets.zero,
          title: Text(computer.name),
          subtitle: Text(computer.id == state.connectedClientId ? 'Conectado agora' : 'Autorizado'),
          trailing: TextButton(onPressed: () => _revoke(computer), child: const Text('Revogar')),
        ),
    ]),
  ];
}

class _Section extends StatelessWidget {
  const _Section(this.title, this.children);

  final String title;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) => Card(
    margin: const EdgeInsets.only(bottom: 12),
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [Text(title, style: Theme.of(context).textTheme.titleMedium), const SizedBox(height: 8), ...children],
      ),
    ),
  );
}

class _Detail extends StatelessWidget {
  const _Detail(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(top: 4),
    child: Text(text, style: Theme.of(context).textTheme.bodySmall),
  );
}
