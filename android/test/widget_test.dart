import 'package:ambienttheme/session_state.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  inviteTests();
  toneTests();
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

void toneTests() {
  test('sensor tone follows sensor health, not the connection', () {
    expect(SessionState.fromMap({'running': true, 'sensorState': 'ACTIVE', 'lux': 12}).sensorTone, Tone.ok);
    expect(SessionState.fromMap({'running': true, 'sensorState': 'ACTIVE'}).sensorTone, Tone.waiting);
    expect(SessionState.fromMap({'running': true, 'sensorState': 'UNCERTAIN', 'connectedClientId': 'pc-1'}).sensorTone,
        Tone.paused);
    expect(SessionState.fromMap({'running': true, 'sensorState': 'UNAVAILABLE'}).sensorTone, Tone.problem);
    expect(SessionState.fromMap({'running': true, 'sensorState': 'ERROR'}).sensorTone, Tone.problem);
  });

  test('connection tone distinguishes connected, waiting and no computer', () {
    final computer = {'id': 'pc-1', 'name': 'Desktop'};
    expect(SessionState.fromMap({'running': true, 'connectedClientId': 'pc-1', 'clients': [computer]}).connectionTone,
        Tone.ok);
    expect(SessionState.fromMap({'running': true, 'clients': [computer]}).connectionTone, Tone.waiting);
    expect(SessionState.fromMap({'running': true}).connectionTone, Tone.paused);
  });

  test('lux headline never invents a value', () {
    expect(SessionState.fromMap({'running': true, 'sensorState': 'ACTIVE', 'lux': 42.4}).luxHeadline, '42 lx');
    expect(SessionState.fromMap({'running': true, 'sensorState': 'ACTIVE'}).luxHeadline, '—');
    expect(SessionState.fromMap({'running': true, 'sensorState': 'UNCERTAIN', 'lux': 42.4}).luxHeadline, '—');
  });
}

void inviteTests() {
  test('invite messages say what actually went wrong', () {
    expect(inviteMessage('ARMED'), contains('autorizado'));
    expect(inviteMessage('NO_SESSION'), contains('Inicie a sessão'));
    expect(inviteMessage('NOT_AN_INVITE'), contains('não é um convite'));
    expect(inviteMessage('EXPIRED:30'), isNot(contains('hora do celular')));
    expect(inviteMessage('EXPIRED:7200'), contains('data e a hora'));
  });

  test('carrier and VPN addresses are kept apart from LAN ones', () {
    final state = SessionState.fromMap({
      'running': true,
      'addresses': ['192.168.1.236:8766'],
      'otherAddresses': ['100.66.106.147:8766'],
    });
    expect(state.addresses, ['192.168.1.236:8766']);
    expect(state.otherAddresses, ['100.66.106.147:8766']);
  });
}
