package dev.hugodotnet.ambienttheme

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.ServiceInfo
import android.net.ConnectivityManager
import android.net.LinkProperties
import android.net.Network
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.os.PowerManager
import dev.hugodotnet.ambienttheme.core.Addresses
import dev.hugodotnet.ambienttheme.core.Invite
import dev.hugodotnet.ambienttheme.core.PairingRegistry
import dev.hugodotnet.ambienttheme.core.SensorHealth
import java.net.Inet4Address
import java.net.NetworkInterface

/**
 * The explicit, user-started sensor session: a foreground service with a persistent notification that owns
 * the light sensor listener, the WSS host and the NSD announcement. Stopping it stops all three.
 */
class SensorSessionService : Service() {
    private val main = Handler(Looper.getMainLooper())
    private lateinit var identity: DeviceIdentity
    private lateinit var registry: PairingRegistry
    private lateinit var sensor: LightSensorSource
    private lateinit var nsd: NsdAdvertiser
    private var server: SensorServer? = null
    private var screenOffEvents = 0L

    private val heartbeat = object : Runnable {
        override fun run() {
            publishStatus()
            main.postDelayed(this, HEARTBEAT_MS)
        }
    }

    private val screenReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) = publishStatus()
    }

    private val networkCallback = object : ConnectivityManager.NetworkCallback() {
        override fun onLinkPropertiesChanged(network: Network, properties: LinkProperties) = onNetworkChanged()
        override fun onLost(network: Network) = onNetworkChanged()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        instance = this
        identity = DeviceIdentity(this)
        registry = PairingRegistry(PrefsClientStore(this), { System.currentTimeMillis() / 1000 })
        sensor = LightSensorSource(this, ::onSensorEvent)
        nsd = NsdAdvertiser(this, identity.deviceId)
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            stopSelf()
            return START_NOT_STICKY
        }

        startForegroundCompat()
        if (server == null) {
            server = SensorServer(PORT, identity.sslContext(), identity.deviceId, BuildConfig.VERSION_NAME, registry, ::notifyChanged, ::onSessionAuthenticated)
                .also { it.start() }
            sensor.start()
            nsd.start(PORT)
            registerReceiver(screenReceiver, IntentFilter().apply {
                addAction(Intent.ACTION_SCREEN_ON)
                addAction(Intent.ACTION_SCREEN_OFF)
            })
            getSystemService(ConnectivityManager::class.java).registerDefaultNetworkCallback(networkCallback)
            main.post(heartbeat)
        }
        notifyChanged()
        return START_NOT_STICKY
    }

    override fun onDestroy() {
        main.removeCallbacks(heartbeat)
        runCatching { unregisterReceiver(screenReceiver) }
        runCatching { getSystemService(ConnectivityManager::class.java).unregisterNetworkCallback(networkCallback) }
        nsd.stop()
        sensor.stop()
        runCatching { server?.stop(1000) }
        server = null
        instance = null
        notifyChanged()
        super.onDestroy()
    }

    /** @return ARMED, NOT_AN_INVITE, or EXPIRED:<seconds ago> (a large value points at a wrong phone or PC clock). */
    fun armInvite(text: String): String {
        val invite = Invite.parse(text) ?: return "NOT_AN_INVITE"
        val secondsLeft = invite.expiresAtEpochSec - System.currentTimeMillis() / 1000
        val armed = registry.arm(invite)
        android.util.Log.i("AmbientTheme", "invite armed=$armed secondsLeft=$secondsLeft")
        notifyChanged()
        return if (armed) "ARMED" else "EXPIRED:${-secondsLeft}"
    }

    fun revoke(clientId: String) {
        registry.revoke(clientId)
        server?.disconnect(clientId)
        notifyChanged()
    }

    fun snapshot(): Map<String, Any?> {
        val (state, reason) = health()
        return mapOf(
            "running" to (server != null),
            "port" to PORT,
            "addresses" to localAddresses().filter { Addresses.isPrivateLan(it.substringBefore(":")) },
            "otherAddresses" to localAddresses().filterNot { Addresses.isPrivateLan(it.substringBefore(":")) },
            "deviceId" to identity.deviceId,
            "displayCode" to identity.displayCode,
            "sensorState" to state.name,
            "sensorReason" to reason,
            "sensorDescription" to sensor.description,
            "lux" to sensor.lastLux,
            "eventCount" to sensor.eventCount,
            "screenOffEvents" to screenOffEvents,
            "lastEventAgeMs" to sensor.lastEventAgeMs,
            "nsdName" to nsd.registeredName,
            "connectedClientId" to server?.connectedClientId,
            "pendingInvite" to registry.pendingInvite?.clientName,
            "clients" to registry.clients().map { mapOf("id" to it.clientId, "name" to it.name) },
        )
    }

    private fun health() = SensorHealth.evaluate(sensor.present, sensor.registered, isScreenInteractive())

    private fun isScreenInteractive() = getSystemService(PowerManager::class.java).isInteractive

    private fun onSensorEvent(lux: Double, ageMs: Long) {
        // With the screen off the event is counted as evidence for the PoC but not forwarded: the declared
        // state is UNCERTAIN until real devices show that the sensor keeps working reliably in that state.
        if (isScreenInteractive()) server?.sendReading(lux, ageMs) else screenOffEvents++
        notifyChanged()
    }

    /**
     * TYPE_LIGHT is on-change: a still room produces no events, so a newly authenticated computer would wait
     * forever. Re-registering the listener makes the platform deliver a fresh initial event for this session.
     */
    private fun onSessionAuthenticated() = main.post {
        sensor.stop()
        sensor.start()
    }

    private fun publishStatus() {
        val (state, reason) = health()
        server?.sendStatus(state, sensor.lastEventAgeMs, reason)
        notifyChanged()
    }

    private fun onNetworkChanged() {
        if (server != null) nsd.start(PORT)
        notifyChanged()
    }

    private fun localAddresses(): List<String> = runCatching {
        NetworkInterface.getNetworkInterfaces().toList()
            .filter { it.isUp && !it.isLoopback }
            .flatMap { it.inetAddresses.toList() }
            .filterIsInstance<Inet4Address>()
            .map { "${it.hostAddress}:$PORT" }
    }.getOrDefault(emptyList())

    private fun startForegroundCompat() {
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(
            NotificationChannel(CHANNEL, "Sessão de sensor", NotificationManager.IMPORTANCE_LOW))
        val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE)
        val stop = PendingIntent.getService(this, 1,
            Intent(this, SensorSessionService::class.java).setAction(ACTION_STOP), PendingIntent.FLAG_IMMUTABLE)
        val notification = Notification.Builder(this, CHANNEL)
            .setContentTitle("Sensor de luz ativo")
            .setContentText("Enviando luminosidade para o computador pareado pela rede local.")
            .setSmallIcon(android.R.drawable.ic_menu_view)
            .setContentIntent(open)
            .setOngoing(true)
            .addAction(Notification.Action.Builder(null, "Parar", stop).build())
            .build()

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
    }

    companion object {
        const val PORT = 8766
        const val ACTION_STOP = "dev.hugodotnet.ambienttheme.STOP"
        private const val CHANNEL = "sensor-session"
        private const val NOTIFICATION_ID = 1
        private const val HEARTBEAT_MS = 5_000L

        @Volatile var instance: SensorSessionService? = null
            private set

        /** Set by the UI to be told when the snapshot may have changed. Called on arbitrary threads. */
        @Volatile var listener: (() -> Unit)? = null

        private fun notifyChanged() {
            listener?.invoke()
        }
    }
}
