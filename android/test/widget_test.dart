import 'package:ambienttheme/session_state.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('stopped session parses with safe defaults', () {
    final state = SessionState.fromMap({'running': false});

    expect(state.running, isFalse);
    expect(state.addresses, isEmpty);
    expect(state.clients, isEmpty);
    expect(state.sensorText, '—');
  });

  test('running session exposes addresses, code, sensor and computers', () {
    final state = SessionState.fromMap({
      'running': true,
      'addresses': ['192.168.137.175:8766'],
      'displayCode': '0123-4567-89AB-CDEF',
      'sensorState': 'ACTIVE',
      'lux': 42.4,
      'eventCount': 7,
      'connectedClientId': 'pc-1',
      'clients': [
        {'id': 'pc-1', 'name': 'Desktop'},
      ],
    });

    expect(state.addresses.single, '192.168.137.175:8766');
    expect(state.displayCode, '0123-4567-89AB-CDEF');
    expect(state.sensorText, 'Ativo: 42 lx');
    expect(state.connectionText, 'Conectado: Desktop');
  });

  test('a connected computer never hides an unhealthy sensor', () {
    final state = SessionState.fromMap({
      'running': true,
      'sensorState': 'UNCERTAIN',
      'sensorReason': 'SCREEN_OFF',
      'connectedClientId': 'pc-1',
    });

    expect(state.sensorText, contains('Incerto'));
  });

  test('missing sensor is stated explicitly', () {
    expect(SessionState.fromMap({'running': true, 'sensorState': 'UNAVAILABLE'}).sensorText,
        contains('não tem sensor de luz'));
  });

  test('active sensor without events yet does not invent a value', () {
    expect(SessionState.fromMap({'running': true, 'sensorState': 'ACTIVE'}).sensorText,
        contains('aguardando o primeiro evento'));
  });

  test('connection text distinguishes no computer from waiting', () {
    expect(SessionState.fromMap({'running': true}).connectionText, 'Nenhum computador autorizado.');
    expect(
      SessionState.fromMap({
        'running': true,
        'clients': [
          {'id': 'pc-1', 'name': 'Desktop'},
        ],
      }).connectionText,
      'Aguardando o computador conectar.',
    );
  });
}
