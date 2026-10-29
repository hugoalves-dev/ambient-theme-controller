import 'package:flutter_test/flutter_test.dart';
import 'package:ambient_sensor_poc/sensor_state.dart';

void main() {
  test('sensor events preserve physical timestamp and accuracy', () {
    final state = SensorState.fromEvent({'type': 'reading', 'lux': 25.0, 'timestampNanos': 123, 'accuracy': 3, 'count': 1});
    expect(state.lux, 25.0);
    expect(state.timestampNanos, 123);
    expect(state.accuracy, 3);
    expect(state.count, 1);
  });
  test('unavailable sensor does not invent a reading', () {
    final state = SensorState.fromEvent({'type': 'unavailable'});
    expect(state.status, 'unavailable');
    expect(state.lux, isNull);
  });
}
