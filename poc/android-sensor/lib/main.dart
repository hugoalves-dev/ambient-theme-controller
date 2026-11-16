import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'sensor_state.dart';

void main() => runApp(const SensorApp());

class SensorApp extends StatelessWidget {
  const SensorApp({super.key});
  static const channel = EventChannel('dev.hugodotnet.ambient_sensor_poc/light');

  @override
  Widget build(BuildContext context) => MaterialApp(
    home: Scaffold(
      appBar: AppBar(title: const Text('Light sensor PoC')),
      body: StreamBuilder<dynamic>(
        stream: channel.receiveBroadcastStream(),
        builder: (context, snapshot) {
          if (snapshot.hasError) return Text('Sensor error: ${snapshot.error}');
          if (!snapshot.hasData) return const Text('Waiting for sensor status');
          final state = SensorState.fromEvent(snapshot.data as Map<dynamic, dynamic>);
          return Padding(
            padding: const EdgeInsets.all(16),
            child: Text('Status: ${state.status}\nLux: ${state.lux ?? "—"}\n'
              'Sensor timestamp (elapsed ns): ${state.timestampNanos ?? "—"}\n'
              'Accuracy: ${state.accuracy ?? "—"}\nEvents: ${state.count ?? "—"}\n\n'
              'Foreground-only experiment. Background and screen-off behavior are not validated.'),
          );
        },
      ),
    ),
  );
}
