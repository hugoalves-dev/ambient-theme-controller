package dev.hugodotnet.ambienttheme.core

import org.json.JSONArray
import org.json.JSONException
import org.json.JSONObject
import java.util.UUID

const val PROTOCOL_VERSION = 1
const val MAX_MESSAGE_BYTES = 4096

enum class SensorState { ACTIVE, UNAVAILABLE, UNCERTAIN, ERROR }

/** Wire messages of protocol v1 (docs/protocol/protocol-v1.md). */
sealed interface Message {
    val messageId: String
}

private fun newId() = UUID.randomUUID().toString().replace("-", "")

data class Hello(val deviceId: String, val sessionId: String, val appVersion: String,
                 val capabilities: List<String>, override val messageId: String = newId()) : Message

data class Auth(val clientId: String, val credential: String, override val messageId: String = newId()) : Message

data class AuthResult(val authorized: Boolean, val reason: String?, override val messageId: String = newId()) : Message

/** A physical sensor event. [sensorEventAgeMs] is measured here with the sensor's own monotonic clock. */
data class LightReading(val sessionId: String, val sequence: Long, val measuredAt: String, val lux: Double,
                        val sensorEventAgeMs: Long, val source: String = "LIGHT_SENSOR",
                        val quality: String = "GOOD", override val messageId: String = newId()) : Message

/** Service heartbeat and sensor health. Never a new physical reading. */
data class SensorStatus(val sessionId: String, val state: SensorState, val lastEventAgeMs: Long?,
                        val reason: String?, override val messageId: String = newId()) : Message

data class Ack(val correlationId: String, override val messageId: String = newId()) : Message

data class ProtocolError(val correlationId: String?, val code: String, val message: String,
                         override val messageId: String = newId()) : Message

data class PairRequest(val clientId: String, val clientName: String, val inviteToken: String,
                       override val messageId: String = newId()) : Message

data class PairApprove(val deviceId: String, val credential: String, override val messageId: String = newId()) : Message

data class PairRevoke(val clientId: String, override val messageId: String = newId()) : Message

object Codec {
    fun encode(message: Message): String {
        val json = JSONObject()
        fun type(name: String) = json.put("type", name)
        when (message) {
            is Hello -> type("HELLO").put("deviceId", message.deviceId).put("sessionId", message.sessionId)
                .put("appVersion", message.appVersion).put("capabilities", JSONArray(message.capabilities))
            is Auth -> type("AUTH").put("clientId", message.clientId).put("credential", message.credential)
            is AuthResult -> type("AUTH_RESULT").put("authorized", message.authorized).putOpt("reason", message.reason)
            is LightReading -> type("LIGHT_READING").put("sessionId", message.sessionId)
                .put("sequence", message.sequence).put("measuredAt", message.measuredAt).put("lux", message.lux)
                .put("source", message.source).put("quality", message.quality)
                .put("sensorEventAgeMs", message.sensorEventAgeMs)
            is SensorStatus -> type("SENSOR_STATUS").put("sessionId", message.sessionId)
                .put("state", message.state.name).putOpt("lastEventAgeMs", message.lastEventAgeMs)
                .putOpt("reason", message.reason)
            is Ack -> type("ACK").put("correlationId", message.correlationId)
            is ProtocolError -> type("ERROR").putOpt("correlationId", message.correlationId)
                .put("code", message.code).put("message", message.message)
            is PairRequest -> type("PAIR_REQUEST").put("clientId", message.clientId)
                .put("clientName", message.clientName).put("inviteToken", message.inviteToken)
            is PairApprove -> type("PAIR_APPROVE").put("deviceId", message.deviceId).put("credential", message.credential)
            is PairRevoke -> type("PAIR_REVOKE").put("clientId", message.clientId)
        }
        return json.put("protocolVersion", PROTOCOL_VERSION).put("messageId", message.messageId).toString()
    }

    /** Strict decoding: anything oversized, malformed, from another version or unknown yields null. */
    fun decode(text: String): Message? {
        if (text.toByteArray(Charsets.UTF_8).size > MAX_MESSAGE_BYTES) return null
        return try {
            val json = JSONObject(text)
            if (json.getInt("protocolVersion") != PROTOCOL_VERSION) return null
            val id = json.getString("messageId")
            when (json.getString("type")) {
                "HELLO" -> Hello(json.getString("deviceId"), json.getString("sessionId"), json.getString("appVersion"),
                    json.getJSONArray("capabilities").let { list -> List(list.length()) { list.getString(it) } }, id)
                "AUTH" -> Auth(json.getString("clientId"), json.getString("credential"), id)
                "AUTH_RESULT" -> AuthResult(json.getBoolean("authorized"), json.optionalString("reason"), id)
                "LIGHT_READING" -> LightReading(json.getString("sessionId"), json.getLong("sequence"),
                    json.getString("measuredAt"), json.getDouble("lux"), json.getLong("sensorEventAgeMs"),
                    json.getString("source"), json.getString("quality"), id)
                "SENSOR_STATUS" -> SensorStatus(json.getString("sessionId"), SensorState.valueOf(json.getString("state")),
                    if (json.has("lastEventAgeMs")) json.getLong("lastEventAgeMs") else null,
                    json.optionalString("reason"), id)
                "ACK" -> Ack(json.getString("correlationId"), id)
                "ERROR" -> ProtocolError(json.optionalString("correlationId"), json.getString("code"),
                    json.getString("message"), id)
                "PAIR_REQUEST" -> PairRequest(json.getString("clientId"), json.getString("clientName"),
                    json.getString("inviteToken"), id)
                "PAIR_APPROVE" -> PairApprove(json.getString("deviceId"), json.getString("credential"), id)
                "PAIR_REVOKE" -> PairRevoke(json.getString("clientId"), id)
                else -> null
            }
        } catch (_: JSONException) {
            null
        } catch (_: IllegalArgumentException) {
            null
        }
    }

    private fun JSONObject.optionalString(name: String): String? = if (has(name) && !isNull(name)) getString(name) else null
}
