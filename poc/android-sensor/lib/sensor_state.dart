class SensorState {
  const SensorState({required this.status, this.lux, this.timestampNanos, this.accuracy, this.count});
  final String status;
  final double? lux;
  final int? timestampNanos;
  final int? accuracy;
  final int? count;

  factory SensorState.fromEvent(Map<dynamic, dynamic> event) => SensorState(
    status: event['type'] as String,
    lux: (event['lux'] as num?)?.toDouble(),
    timestampNanos: event['timestampNanos'] as int?,
    accuracy: event['accuracy'] as int?,
    count: event['count'] as int?,
  );
}
