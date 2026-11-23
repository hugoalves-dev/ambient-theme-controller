/// Snapshot of the native sensor session, as published by `SensorSessionService.snapshot()`.
class SessionState {
  const SessionState({
    required this.running,
    this.addresses = const [],
    this.otherAddresses = const [],
    this.displayCode,
    this.sensorState,
    this.sensorReason,
    this.sensorDescription,
    this.lux,
    this.eventCount = 0,
    this.screenOffEvents = 0,
    this.lastEventAgeMs,
    this.nsdName,
    this.connectedClientId,
    this.pendingInvite,
    this.clients = const [],
  });

  final bool running;
  final List<String> addresses;
  final List<String> otherAddresses;
  final String? displayCode;
  final String? sensorState;
  final String? sensorReason;
  final String? sensorDescription;
  final double? lux;
  final int eventCount;
  final int screenOffEvents;
  final int? lastEventAgeMs;
  final String? nsdName;
  final String? connectedClientId;
  final String? pendingInvite;
  final List<PairedComputer> clients;

  factory SessionState.fromMap(Map<dynamic, dynamic> map) => SessionState(
    running: map['running'] == true,
    addresses: (map['addresses'] as List?)?.cast<String>() ?? const [],
    otherAddresses: (map['otherAddresses'] as List?)?.cast<String>() ?? const [],
    displayCode: map['displayCode'] as String?,
    sensorState: map['sensorState'] as String?,
    sensorReason: map['sensorReason'] as String?,
    sensorDescription: map['sensorDescription'] as String?,
    lux: (map['lux'] as num?)?.toDouble(),
    eventCount: (map['eventCount'] as num?)?.toInt() ?? 0,
    screenOffEvents: (map['screenOffEvents'] as num?)?.toInt() ?? 0,
    lastEventAgeMs: (map['lastEventAgeMs'] as num?)?.toInt(),
    nsdName: map['nsdName'] as String?,
    connectedClientId: map['connectedClientId'] as String?,
    pendingInvite: map['pendingInvite'] as String?,
    clients: ((map['clients'] as List?) ?? const [])
        .map((client) => PairedComputer(client['id'] as String, client['name'] as String))
        .toList(),
  );

  /// Sensor health in plain words. A connected computer never implies a healthy sensor.
  String get sensorText => switch (sensorState) {
    'ACTIVE' => lux == null
        ? 'Ativo, aguardando o primeiro evento do sensor.'
        : 'Ativo: ${lux!.toStringAsFixed(0)} lx',
    'UNAVAILABLE' => 'Este aparelho não tem sensor de luz disponível.',
    'ERROR' => 'Não foi possível registrar o sensor de luz.',
    'UNCERTAIN' => 'Incerto com a tela apagada: a automação no computador fica suspensa.',
    _ => '—',
  };

  /// Colour family of the sensor chip. Driven by sensor health only.
  Tone get sensorTone => switch (sensorState) {
    'ACTIVE' => lux == null ? Tone.waiting : Tone.ok,
    'UNCERTAIN' => Tone.paused,
    'UNAVAILABLE' || 'ERROR' => Tone.problem,
    _ => Tone.waiting,
  };

  Tone get connectionTone => connectedClientId != null
      ? Tone.ok
      : clients.isEmpty
      ? Tone.paused
      : Tone.waiting;

  /// Big number on the hero card: only a trusted, active reading.
  String get luxHeadline => sensorState == 'ACTIVE' && lux != null ? '${lux!.toStringAsFixed(0)} lx' : '—';

  String get connectionText {
    if (connectedClientId == null) {
      return clients.isEmpty ? 'Nenhum computador autorizado.' : 'Aguardando o computador conectar.';
    }
    final name = clients.where((client) => client.id == connectedClientId).map((client) => client.name).firstOrNull;
    return 'Conectado: ${name ?? 'computador autorizado'}';
  }
}

/// Status colour family shared by the chips (same palette as the Windows app).
enum Tone { ok, waiting, paused, problem }

class PairedComputer {
  const PairedComputer(this.id, this.name);

  final String id;
  final String name;
}

/// Plain-language result of arming an invite, from the native result code.
String inviteMessage(String code) {
  if (code == 'ARMED') return 'Computador autorizado a parear. Continue no computador agora (vale por 2 minutos).';
  if (code == 'NO_SESSION') return 'Inicie a sessão de sensor antes de autorizar o computador.';
  if (code == 'NOT_AN_INVITE') return 'Esse código não é um convite do Ambient Theme.';
  if (code.startsWith('EXPIRED:')) {
    final seconds = int.tryParse(code.substring(8)) ?? 0;
    final age = seconds >= 120
        ? ' (expirou há ${(seconds / 60).round()} min — se o QR é novo, confira a data e a hora do celular e do computador)'
        : '';
    return 'O convite expirou$age. Gere outro no computador e escaneie em até 2 minutos.';
  }
  return 'Não foi possível usar o convite.';
}
