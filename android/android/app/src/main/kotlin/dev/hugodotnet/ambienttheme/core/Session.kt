package dev.hugodotnet.ambienttheme.core

import java.net.URI
import java.net.URLDecoder
import java.security.MessageDigest
import java.security.SecureRandom
import java.util.Base64
import java.util.UUID

/** Temporary single-use invitation read from the QR code shown by Windows. It carries no network address. */
data class Invite(val clientId: String, val clientName: String, val token: String, val expiresAtEpochSec: Long) {
    companion object {
        fun parse(text: String): Invite? = try {
            val uri = URI(text.trim())
            val query = (uri.rawQuery ?: "").split("&").mapNotNull { pair ->
                pair.split("=", limit = 2).takeIf { it.size == 2 }?.let { it[0] to URLDecoder.decode(it[1], "UTF-8") }
            }.toMap()
            if (uri.scheme != "ambienttheme" || uri.host != "pair" || query["v"] != "1") null
            else Invite(query.getValue("c"), query.getValue("n"), query.getValue("t"), query.getValue("e").toLong())
        } catch (_: Exception) {
            null
        }
    }
}

data class AuthorizedClient(val clientId: String, val name: String, val credentialHash: String)

interface ClientStore {
    fun load(): List<AuthorizedClient>
    fun save(clients: List<AuthorizedClient>)
}

/**
 * Who may connect. Scanning a QR arms one invite; the matching PAIR_REQUEST consumes it and creates a
 * credential unique to that computer. Only a hash of the credential is kept, so nothing secret is at rest.
 */
class PairingRegistry(
    private val store: ClientStore,
    private val nowEpochSec: () -> Long,
    private val newCredential: () -> String = ::randomCredential,
) {
    var pendingInvite: Invite? = null
        private set

    @Synchronized
    fun arm(invite: Invite): Boolean {
        pendingInvite = invite.takeIf { nowEpochSec() < it.expiresAtEpochSec }
        return pendingInvite != null
    }

    /** @return the new credential, or null when the invite is unknown, different, used or expired. */
    @Synchronized
    fun pair(request: PairRequest): String? {
        val invite = pendingInvite ?: return null
        val matches = invite.clientId == request.clientId &&
            nowEpochSec() < invite.expiresAtEpochSec &&
            MessageDigest.isEqual(invite.token.toByteArray(), request.inviteToken.toByteArray())
        if (!matches) return null

        pendingInvite = null
        val credential = newCredential()
        store.save(store.load().filter { it.clientId != request.clientId } +
            AuthorizedClient(request.clientId, invite.clientName, hash(credential)))
        return credential
    }

    @Synchronized
    fun authenticate(clientId: String, credential: String): Boolean {
        val client = store.load().firstOrNull { it.clientId == clientId } ?: return false
        return MessageDigest.isEqual(client.credentialHash.toByteArray(), hash(credential).toByteArray())
    }

    @Synchronized
    fun revoke(clientId: String) = store.save(store.load().filter { it.clientId != clientId })

    @Synchronized
    fun clients(): List<AuthorizedClient> = store.load()

    private fun hash(credential: String) =
        MessageDigest.getInstance("SHA-256").digest(credential.toByteArray()).joinToString("") { "%02x".format(it) }
}

private fun randomCredential(): String =
    Base64.getEncoder().encodeToString(ByteArray(32).also { SecureRandom().nextBytes(it) })

data class Reply(val messages: List<Message>, val close: Boolean)

/**
 * Protocol state of one connection, independent from sockets. A connection either authenticates with a
 * stored credential or performs one pairing exchange; nothing is sent to it before authentication.
 */
class SessionHandler(
    private val deviceId: String,
    private val appVersion: String,
    private val registry: PairingRegistry,
    newSessionId: () -> String = { UUID.randomUUID().toString().replace("-", "") },
) {
    val sessionId: String = newSessionId()
    private var sequence = 0L

    var authenticated = false
        private set
    var clientId: String? = null
        private set

    fun open() = Hello(deviceId, sessionId, appVersion, listOf("LIGHT_SENSOR"))

    fun receive(text: String): Reply {
        val message = Codec.decode(text) ?: return Reply(emptyList(), close = true)
        return if (authenticated) receiveInSession(message) else receiveHandshake(message)
    }

    @Synchronized
    fun reading(lux: Double, sensorEventAgeMs: Long, measuredAt: String): LightReading? =
        if (authenticated) LightReading(sessionId, ++sequence, measuredAt, lux, sensorEventAgeMs) else null

    fun status(state: SensorState, lastEventAgeMs: Long?, reason: String?): SensorStatus? =
        if (authenticated) SensorStatus(sessionId, state, lastEventAgeMs, reason) else null

    private fun receiveHandshake(message: Message): Reply = when (message) {
        is Auth -> {
            authenticated = registry.authenticate(message.clientId, message.credential)
            clientId = message.clientId.takeIf { authenticated }
            Reply(listOf(AuthResult(authenticated, if (authenticated) null else "REVOKED")), close = !authenticated)
        }
        is PairRequest -> {
            val credential = registry.pair(message)
            val answer = if (credential != null) PairApprove(deviceId, credential)
            else ProtocolError(message.messageId, "PAIRING_REJECTED", "Invite is unknown, used or expired.")
            Reply(listOf(answer), close = true)
        }
        else -> Reply(emptyList(), close = true)
    }

    private fun receiveInSession(message: Message): Reply {
        if (message is PairRevoke && message.clientId == clientId) {
            registry.revoke(message.clientId)
            authenticated = false
            return Reply(emptyList(), close = true)
        }
        return Reply(emptyList(), close = false)
    }
}

object SensorHealth {
    /**
     * Conservative by design: TYPE_LIGHT is usually a non-wake-up, on-change sensor, so with the screen off
     * the absence of events proves nothing. Until measured on real devices (issue #2) that state is UNCERTAIN.
     */
    fun evaluate(sensorPresent: Boolean, listenerRegistered: Boolean, screenInteractive: Boolean): Pair<SensorState, String?> =
        when {
            !sensorPresent -> SensorState.UNAVAILABLE to "NO_LIGHT_SENSOR"
            !listenerRegistered -> SensorState.ERROR to "LISTENER_NOT_REGISTERED"
            !screenInteractive -> SensorState.UNCERTAIN to "SCREEN_OFF"
            else -> SensorState.ACTIVE to null
        }
}
