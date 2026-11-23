import 'package:ambienttheme/main.dart';
import 'package:ambienttheme/session_state.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('stopped session shows the intro and the start action', (tester) async {
    await tester.pumpWidget(AmbientThemeApp(states: Stream.value(const SessionState(running: false))));
    await tester.pump();

    expect(find.text('Ambient Theme'), findsOneWidget);
    expect(find.text('Usar como sensor'), findsOneWidget);
    expect(find.text('LUZ DO AMBIENTE'), findsNothing);
  });

  testWidgets('running session shows the live reading, chips and address', (tester) async {
    final state = SessionState.fromMap({
      'running': true,
      'addresses': ['192.168.1.236:8766'],
      'sensorState': 'ACTIVE',
      'lux': 42.4,
      'connectedClientId': 'pc-1',
      'clients': [
        {'id': 'pc-1', 'name': 'Desktop'},
      ],
    });
    await tester.pumpWidget(AmbientThemeApp(states: Stream.value(state)));
    await tester.pump();

    expect(find.text('42 lx'), findsOneWidget);
    expect(find.text('Sensor ativo'), findsOneWidget);
    expect(find.text('Conectado'), findsOneWidget);
    expect(find.text('192.168.1.236:8766'), findsOneWidget);
    expect(find.text('Parar sessão de sensor'), findsOneWidget);
    expect(find.byTooltip('Copiar endereço'), findsOneWidget);
  });

  testWidgets('unhealthy sensor is not hidden by a connected computer', (tester) async {
    final state = SessionState.fromMap({'running': true, 'sensorState': 'UNCERTAIN', 'lux': 42.0, 'connectedClientId': 'pc-1'});
    await tester.pumpWidget(AmbientThemeApp(states: Stream.value(state)));
    await tester.pump();

    expect(find.text('Leitura incerta'), findsOneWidget);
    expect(find.text('42 lx'), findsNothing);
    expect(find.byType(SelectableText), findsWidgets);
  });
}
