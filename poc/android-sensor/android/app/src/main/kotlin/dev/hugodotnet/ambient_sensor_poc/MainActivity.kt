package dev.hugodotnet.ambient_sensor_poc

import android.content.Context
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.EventChannel

class MainActivity : FlutterActivity(), SensorEventListener, EventChannel.StreamHandler {
    private lateinit var manager: SensorManager
    private var sensor: Sensor? = null
    private var sink: EventChannel.EventSink? = null
    private var resumed = false
    private var count = 0
    private var accuracy = SensorManager.SENSOR_STATUS_UNRELIABLE

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        manager = getSystemService(Context.SENSOR_SERVICE) as SensorManager
        sensor = manager.getDefaultSensor(Sensor.TYPE_LIGHT)
        EventChannel(flutterEngine.dartExecutor.binaryMessenger, "dev.hugodotnet.ambient_sensor_poc/light")
            .setStreamHandler(this)
    }
    override fun onListen(arguments: Any?, events: EventChannel.EventSink) {
        sink = events
        if (sensor == null) events.success(mapOf("type" to "unavailable"))
        else if (resumed) register() else events.success(mapOf("type" to "paused"))
    }
    override fun onCancel(arguments: Any?) { manager.unregisterListener(this); sink = null }
    override fun onResume() { super.onResume(); resumed = true; if (sink != null && sensor != null) register() }
    override fun onPause() { resumed = false; manager.unregisterListener(this); sink?.success(mapOf("type" to "paused")); super.onPause() }
    private fun register() {
        manager.unregisterListener(this)
        val registered = manager.registerListener(this, sensor, SensorManager.SENSOR_DELAY_NORMAL)
        sink?.success(mapOf("type" to if (registered) "waiting" else "error"))
    }
    override fun onSensorChanged(event: SensorEvent) {
        if (event.sensor.type != Sensor.TYPE_LIGHT || event.values.isEmpty()) return
        count++
        sink?.success(mapOf("type" to "reading", "lux" to event.values[0].toDouble(),
            "timestampNanos" to event.timestamp, "accuracy" to accuracy, "count" to count))
    }
    override fun onAccuracyChanged(sensor: Sensor?, newAccuracy: Int) { accuracy = newAccuracy }
}
