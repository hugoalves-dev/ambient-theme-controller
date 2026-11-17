package dev.hugodotnet.ambienttheme

import android.util.Log
import dev.hugodotnet.ambienttheme.core.Codec
import dev.hugodotnet.ambienttheme.core.Message
import dev.hugodotnet.ambienttheme.core.PairingRegistry
import dev.hugodotnet.ambienttheme.core.SensorState
import dev.hugodotnet.ambienttheme.core.SessionHandler
import org.java_websocket.WebSocket
import org.java_websocket.handshake.ClientHandshake
import org.java_websocket.server.DefaultSSLWebSocketServerFactory
import org.java_websocket.server.WebSocketServer
import java.net.InetSocketAddress
import java.time.Instant
import java.util.concurrent.ConcurrentHashMap
import javax.net.ssl.SSLContext

/**
 * Local WSS host. It only transports: sensor samples and status go out to the one authenticated computer,
 * and the only things accepted from the network are authentication, pairing, ACKs and self-revocation.
 */
class SensorServer(
    port: Int,
    sslContext: SSLContext,
    private val deviceId: String,
    private val appVersion: String,
    private val registry: PairingRegistry,
    private val onChange: () -> Unit,
) : WebSocketServer(InetSocketAddress(port)) {
    private val handlers = ConcurrentHashMap<WebSocket, SessionHandler>()

    init {
        setWebSocketFactory(DefaultSSLWebSocketServerFactory(sslContext))
        isReuseAddr = true
        connectionLostTimeout = 20
    }

    /** Name-independent id of the computer currently receiving readings, if any. */
    val connectedClientId: String?
        get() = handlers.values.firstOrNull { it.authenticated }?.clientId

    fun sendReading(lux: Double, ageMs: Long) = forSession { it.reading(lux, ageMs, Instant.now().toString()) }

    fun sendStatus(state: SensorState, lastEventAgeMs: Long?, reason: String?) =
        forSession { it.status(state, lastEventAgeMs, reason) }

    /** Drops the session of a computer that was just revoked on this phone. */
    fun disconnect(clientId: String) =
        handlers.filterValues { it.clientId == clientId }.keys.forEach { it.close() }

    override fun onStart() = onChange()

    override fun onOpen(connection: WebSocket, handshake: ClientHandshake) {
        if (handshake.resourceDescriptor != "/v1" || handlers.size >= MAX_CONNECTIONS) {
            connection.close()
            return
        }

        val handler = SessionHandler(deviceId, appVersion, registry)
        handlers[connection] = handler
        connection.send(Codec.encode(handler.open()))
    }

    override fun onMessage(connection: WebSocket, text: String) {
        val handler = handlers[connection] ?: return
        val wasAuthenticated = handler.authenticated
        val reply = handler.receive(text)
        reply.messages.forEach { connection.send(Codec.encode(it)) }
        if (reply.close) {
            connection.close()
        } else if (!wasAuthenticated && handler.authenticated) {
            // One source, one consumer: a new authenticated session replaces the previous one.
            handlers.filter { it.key != connection && it.value.authenticated }.keys.forEach { it.close() }
        }
        onChange()
    }

    override fun onClose(connection: WebSocket, code: Int, reason: String?, remote: Boolean) {
        handlers.remove(connection)
        onChange()
    }

    override fun onError(connection: WebSocket?, error: Exception) {
        // Message contents are never logged: they may carry credentials or invite tokens.
        Log.w("AmbientTheme", "WebSocket error: ${error.javaClass.simpleName}")
    }

    private fun forSession(build: (SessionHandler) -> Message?) {
        for ((connection, handler) in handlers) {
            val message = build(handler) ?: continue
            if (connection.isOpen) connection.send(Codec.encode(message))
        }
    }

    private companion object {
        const val MAX_CONNECTIONS = 4
    }
}
