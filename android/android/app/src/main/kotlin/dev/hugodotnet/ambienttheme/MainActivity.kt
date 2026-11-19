package dev.hugodotnet.ambienttheme

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Handler
import android.os.Looper
import com.google.zxing.integration.android.IntentIntegrator
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.EventChannel
import io.flutter.plugin.common.MethodChannel

/** Bridges the Flutter UI to the sensor session service. All logic lives in the service and in `core`. */
class MainActivity : FlutterActivity(), EventChannel.StreamHandler {
    private val main = Handler(Looper.getMainLooper())
    private var sink: EventChannel.EventSink? = null
    private var pendingScan: MethodChannel.Result? = null

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        val messenger = flutterEngine.dartExecutor.binaryMessenger
        EventChannel(messenger, "ambienttheme/state").setStreamHandler(this)
        MethodChannel(messenger, "ambienttheme/control").setMethodCallHandler { call, result ->
            when (call.method) {
                "start" -> {
                    requestNotificationPermission()
                    startForegroundService(Intent(this, SensorSessionService::class.java))
                    result.success(null)
                }
                "stop" -> {
                    stopService(Intent(this, SensorSessionService::class.java))
                    result.success(null)
                }
                "armInvite" -> result.success(SensorSessionService.instance?.armInvite(call.arguments as String) ?: "NO_SESSION")
                "scanInvite" -> {
                    pendingScan = result
                    IntentIntegrator(this)
                        .setDesiredBarcodeFormats(IntentIntegrator.QR_CODE)
                        .setPrompt("Aponte para o código mostrado no computador")
                        .setBeepEnabled(false)
                        .setOrientationLocked(false)
                        .initiateScan()
                }
                "revoke" -> {
                    SensorSessionService.instance?.revoke(call.arguments as String)
                    result.success(null)
                }
                else -> result.notImplemented()
            }
        }
    }

    override fun onListen(arguments: Any?, events: EventChannel.EventSink) {
        sink = events
        SensorSessionService.listener = { main.post(::publish) }
        publish()
    }

    override fun onCancel(arguments: Any?) {
        SensorSessionService.listener = null
        sink = null
    }

    @Deprecated("FlutterActivity is not a ComponentActivity, so the result still arrives here.")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        val scan = IntentIntegrator.parseActivityResult(requestCode, resultCode, data)
        val result = pendingScan
        if (scan == null || result == null) {
            super.onActivityResult(requestCode, resultCode, data)
            return
        }

        pendingScan = null
        result.success(
            if (scan.contents == null) "CANCELLED"
            else SensorSessionService.instance?.armInvite(scan.contents) ?: "NO_SESSION",
        )
    }

    private fun publish() {
        sink?.success(SensorSessionService.instance?.snapshot() ?: mapOf("running" to false))
    }

    private fun requestNotificationPermission() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) {
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 1)
        }
    }
}
