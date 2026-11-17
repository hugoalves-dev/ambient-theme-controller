package dev.hugodotnet.ambienttheme

import android.content.Context
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.os.SystemClock
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Log
import dev.hugodotnet.ambienttheme.core.AuthorizedClient
import dev.hugodotnet.ambienttheme.core.ClientStore
import org.json.JSONArray
import org.json.JSONObject
import java.math.BigInteger
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.MessageDigest
import java.security.spec.ECGenParameterSpec
import java.util.Date
import java.util.UUID
import javax.net.ssl.KeyManagerFactory
import javax.net.ssl.SSLContext
import javax.security.auth.x500.X500Principal

private const val TAG = "AmbientTheme"

/**
 * Persistent identity of this phone: a public device id and a TLS key that never leaves the Android Keystore.
 * Windows pins the SHA-256 of the certificate's SubjectPublicKeyInfo at pairing.
 */
class DeviceIdentity(context: Context) {
    private val keyStore = KeyStore.getInstance(KEYSTORE).apply { load(null) }

    val deviceId: String = context.getSharedPreferences("identity", Context.MODE_PRIVATE).let { prefs ->
        prefs.getString("deviceId", null) ?: UUID.randomUUID().toString().replace("-", "").also {
            prefs.edit().putString("deviceId", it).apply()
        }
    }

    init {
        if (!keyStore.containsAlias(ALIAS)) generateKey()
    }

    val pin: String = MessageDigest.getInstance("SHA-256")
        .digest(keyStore.getCertificate(ALIAS).publicKey.encoded)
        .joinToString("") { "%02x".format(it) }

    /** Same short code Windows shows at pairing: first 64 bits of the pin, in groups of four. */
    val displayCode: String = pin.take(16).uppercase().chunked(4).joinToString("-")

    fun sslContext(): SSLContext {
        val keys = KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm()).apply { init(keyStore, null) }
        return SSLContext.getInstance("TLS").apply { init(keys.keyManagers, null, null) }
    }

    private fun generateKey() {
        val tenYears = 10L * 365 * 24 * 60 * 60 * 1000
        val spec = KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_SIGN)
            .setAlgorithmParameterSpec(ECGenParameterSpec("secp256r1"))
            .setDigests(KeyProperties.DIGEST_NONE, KeyProperties.DIGEST_SHA256,
                KeyProperties.DIGEST_SHA384, KeyProperties.DIGEST_SHA512)
            .setCertificateSubject(X500Principal("CN=ambient-theme-android"))
            .setCertificateSerialNumber(BigInteger.ONE)
            .setCertificateNotBefore(Date(System.currentTimeMillis() - 24L * 60 * 60 * 1000))
            .setCertificateNotAfter(Date(System.currentTimeMillis() + tenYears))
            .build()
        KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_EC, KEYSTORE).apply { initialize(spec) }.generateKeyPair()
    }

    private companion object {
        const val KEYSTORE = "AndroidKeyStore"
        const val ALIAS = "ambient-theme-tls"
    }
}

/** Authorized computers in app-private preferences. Only credential hashes are stored. */
class PrefsClientStore(context: Context) : ClientStore {
    private val prefs = context.getSharedPreferences("pairing", Context.MODE_PRIVATE)

    override fun load(): List<AuthorizedClient> {
        val array = JSONArray(prefs.getString("clients", "[]"))
        return List(array.length()) { index ->
            array.getJSONObject(index).let { AuthorizedClient(it.getString("id"), it.getString("name"), it.getString("hash")) }
        }
    }

    override fun save(clients: List<AuthorizedClient>) {
        val array = JSONArray(clients.map { JSONObject().put("id", it.clientId).put("name", it.name).put("hash", it.credentialHash) })
        prefs.edit().putString("clients", array.toString()).apply()
    }
}

/**
 * Physical TYPE_LIGHT source. Every callback is a real sensor event with the sensor's own timestamp;
 * nothing here ever fabricates or re-stamps a sample.
 */
class LightSensorSource(
    context: Context,
    private val onEvent: (lux: Double, ageMs: Long) -> Unit,
) : SensorEventListener {
    private val manager = context.getSystemService(Context.SENSOR_SERVICE) as SensorManager
    private val sensor: Sensor? = manager.getDefaultSensor(Sensor.TYPE_LIGHT)

    val present get() = sensor != null
    val description get() = sensor?.let { "${it.name} (${it.vendor}), máx ${it.maximumRange} lx, wake-up=${it.isWakeUpSensor}" }

    @Volatile var registered = false
        private set
    @Volatile var eventCount = 0L
        private set
    @Volatile var lastLux: Double? = null
        private set
    @Volatile private var lastEventNanos = 0L

    /** Age of the last physical event, from the same monotonic clock the sensor stamps events with. */
    val lastEventAgeMs: Long?
        get() = if (eventCount == 0L) null else ageMs(lastEventNanos)

    fun start(): Boolean {
        registered = sensor != null && manager.registerListener(this, sensor, SensorManager.SENSOR_DELAY_NORMAL)
        return registered
    }

    fun stop() {
        manager.unregisterListener(this)
        registered = false
    }

    override fun onSensorChanged(event: SensorEvent) {
        if (event.sensor.type != Sensor.TYPE_LIGHT || event.values.isEmpty()) return
        eventCount++
        lastEventNanos = event.timestamp
        lastLux = event.values[0].toDouble()
        onEvent(event.values[0].toDouble(), ageMs(event.timestamp))
    }

    override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) = Unit

    private fun ageMs(eventNanos: Long) = ((SystemClock.elapsedRealtimeNanos() - eventNanos) / 1_000_000).coerceAtLeast(0)
}

/** Announces only the public id, protocol version and port. Never a token or credential. */
class NsdAdvertiser(context: Context, private val deviceId: String) {
    private val manager = context.getSystemService(Context.NSD_SERVICE) as NsdManager
    private var listener: NsdManager.RegistrationListener? = null

    @Volatile var registeredName: String? = null
        private set

    fun start(port: Int) {
        stop()
        val info = NsdServiceInfo().apply {
            serviceName = "AmbientTheme-${deviceId.take(6)}"
            serviceType = SERVICE_TYPE
            setPort(port)
            setAttribute("id", deviceId)
            setAttribute("v", "1")
        }
        listener = object : NsdManager.RegistrationListener {
            override fun onServiceRegistered(registered: NsdServiceInfo) { registeredName = registered.serviceName }
            override fun onRegistrationFailed(info: NsdServiceInfo, code: Int) {
                registeredName = null
                Log.w(TAG, "NSD registration failed: $code")
            }
            override fun onServiceUnregistered(info: NsdServiceInfo) { registeredName = null }
            override fun onUnregistrationFailed(info: NsdServiceInfo, code: Int) = Unit
        }.also { manager.registerService(info, NsdManager.PROTOCOL_DNS_SD, it) }
    }

    fun stop() {
        listener?.let { runCatching { manager.unregisterService(it) } }
        listener = null
        registeredName = null
    }

    private companion object {
        const val SERVICE_TYPE = "_ambienttheme._tcp"
    }
}
