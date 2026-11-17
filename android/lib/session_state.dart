/// Snapshot of the native sensor session, as published by `SensorSessionService.snapshot()`.
class SessionState {
  const SessionState({
    required this.running,
    this.addresses = const [],
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

  String get connectionText {
    if (connectedClientId == null) {
      return clients.isEmpty ? 'Nenhum computador autorizado.' : 'Aguardando o computador conectar.';
    }
    final name = clients.where((client) => client.id == connectedClientId).map((client) => client.name).firstOrNull;
    return 'Conectado: ${name ?? 'computador autorizado'}';
  }
}

class PairedComputer {
  const PairedComputer(this.id, this.name);

  final String id;
  final String name;
}
