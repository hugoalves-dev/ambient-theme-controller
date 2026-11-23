package dev.hugodotnet.ambienttheme.core

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

class MemoryStore : ClientStore {
    var clients: List<AuthorizedClient> = emptyList()
    override fun load() = clients
    override fun save(clients: List<AuthorizedClient>) { this.clients = clients }
}

class CodecTest {
    /** The same file is read by the Windows test suite: it is the contract between both sides. */
    private val golden = JSONObject(File("../../../docs/protocol/golden-v1.json").readText())

    @Test fun every_golden_sample_decodes_and_survives_a_roundtrip() {
        for (name in golden.keys()) {
            val decoded = Codec.decode(golden.getJSONObject(name).toString())
            assertNotNull("$name did not decode", decoded)
            assertEquals(name, decoded, Codec.decode(Codec.encode(decoded!!)))
        }
    }

    @Test fun reading_is_encoded_with_the_fields_windows_validates() {
        val json = JSONObject(Codec.encode(LightReading("s1", 3, "2026-11-16T10:00:00Z", 12.5, 80, messageId = "m")))

        assertEquals("LIGHT_READING", json.getString("type"))
        assertEquals(1, json.getInt("protocolVersion"))
        assertEquals("LIGHT_SENSOR", json.getString("source"))
        assertEquals("GOOD", json.getString("quality"))
        assertEquals(3L, json.getLong("sequence"))
        assertEquals(80L, json.getLong("sensorEventAgeMs"))
    }

    @Test fun oversized_malformed_foreign_version_and_unknown_type_are_refused() {
        assertNull(Codec.decode("x".repeat(MAX_MESSAGE_BYTES + 1)))
        assertNull(Codec.decode("not json"))
        assertNull(Codec.decode("""{"type":"ACK","protocolVersion":2,"messageId":"m","correlationId":"c"}"""))
        assertNull(Codec.decode("""{"type":"SET_THEME","protocolVersion":1,"messageId":"m"}"""))
        assertNull(Codec.decode("""{"type":"AUTH","protocolVersion":1,"messageId":"m"}"""))
    }
}

class PairingRegistryTest {
    private var now = 1_000L
    private val store = MemoryStore()
    private val registry = PairingRegistry(store, { now }, { "credential-${store.clients.size}" })
    private val invite = Invite("pc-1", "Desktop", "token", 1_120)

    @Test fun invite_is_parsed_from_the_qr_uri() {
        assertEquals(Invite("pc-1", "Meu PC & cia", "tok_en-1", 1_120),
            Invite.parse("ambienttheme://pair?v=1&c=pc-1&n=Meu%20PC%20%26%20cia&t=tok_en-1&e=1120"))
    }

    @Test fun foreign_or_incomplete_qr_content_is_refused() {
        assertNull(Invite.parse("https://example.com/pair?v=1&c=a&n=b&t=c&e=1"))
        assertNull(Invite.parse("ambienttheme://pair?v=2&c=a&n=b&t=c&e=1"))
        assertNull(Invite.parse("ambienttheme://pair?v=1&c=a&n=b"))
        assertNull(Invite.parse("garbage"))
    }

    @Test fun new_invite_pasted_into_an_old_one_is_refused_instead_of_using_the_old_expiry() {
        val old = "ambienttheme://pair?v=1&c=pc-1&n=Old&t=old-token&e=1000"
        val fresh = "ambienttheme://pair?v=1&c=pc-1&n=New&t=new-token&e=2000"

        assertNull(Invite.parse(old.substring(0, 40) + fresh + old.substring(40)))
        assertNull(Invite.parse(old + fresh))
        assertNull(Invite.parse("ambienttheme://pair?v=1&c=a&n=b&t=c&e=2000&e=1000"))
    }

    @Test fun invite_survives_a_phone_clock_up_to_a_minute_ahead() {
        now = 1_179
        assertTrue(registry.arm(invite))
        assertNotNull(registry.pair(PairRequest("pc-1", "Desktop", "token")))
    }

    @Test fun invite_past_the_clock_tolerance_cannot_be_armed() {
        now = 1_180
        assertFalse(registry.arm(invite))
    }

    @Test fun scanned_invite_pairs_once_and_yields_a_working_credential() {
        assertTrue(registry.arm(invite))

        val credential = registry.pair(PairRequest("pc-1", "Desktop", "token"))

        assertNotNull(credential)
        assertTrue(registry.authenticate("pc-1", credential!!))
        assertNull(registry.pair(PairRequest("pc-1", "Desktop", "token")))
    }

    @Test fun unknown_wrong_or_expired_invite_is_rejected() {
        assertNull(registry.pair(PairRequest("pc-1", "Desktop", "token")))
        registry.arm(invite)
        assertNull(registry.pair(PairRequest("pc-1", "Desktop", "other")))
        assertNull(registry.pair(PairRequest("pc-2", "Desktop", "token")))
        now = 1_180
        assertNull(registry.pair(PairRequest("pc-1", "Desktop", "token")))
    }

    @Test fun expired_invite_cannot_be_armed() {
        now = 2_000
        assertFalse(registry.arm(invite))
        assertNull(registry.pendingInvite)
    }

    @Test fun credential_is_stored_only_as_a_hash() {
        registry.arm(invite)
        val credential = registry.pair(PairRequest("pc-1", "Desktop", "token"))!!

        assertFalse(store.clients.single().credentialHash.contains(credential))
        assertEquals("Desktop", store.clients.single().name)
    }

    @Test fun wrong_credential_and_unknown_client_fail_authentication() {
        registry.arm(invite)
        registry.pair(PairRequest("pc-1", "Desktop", "token"))

        assertFalse(registry.authenticate("pc-1", "wrong"))
        assertFalse(registry.authenticate("pc-9", "credential-0"))
    }

    @Test fun revoked_computer_can_no_longer_authenticate() {
        registry.arm(invite)
        val credential = registry.pair(PairRequest("pc-1", "Desktop", "token"))!!

        registry.revoke("pc-1")

        assertFalse(registry.authenticate("pc-1", credential))
        assertTrue(registry.clients().isEmpty())
    }

    @Test fun authorizations_survive_a_restart() {
        registry.arm(invite)
        val credential = registry.pair(PairRequest("pc-1", "Desktop", "token"))!!

        assertTrue(PairingRegistry(store, { now }).authenticate("pc-1", credential))
    }
}

class SessionHandlerTest {
    private val store = MemoryStore()
    private val registry = PairingRegistry(store, { 1_000L }, { "credential" })
    private fun handler() = SessionHandler("dev-1", "0.1.0", registry) { "s1" }

    private fun authenticated(): SessionHandler {
        registry.arm(Invite("pc-1", "Desktop", "token", 1_120))
        registry.pair(PairRequest("pc-1", "Desktop", "token"))
        return handler().also { it.receive(Codec.encode(Auth("pc-1", "credential"))) }
    }

    @Test fun session_opens_with_hello_carrying_identity_and_session() {
        val hello = handler().open()

        assertEquals("dev-1", hello.deviceId)
        assertEquals("s1", hello.sessionId)
    }

    @Test fun valid_credential_authenticates_and_keeps_the_connection() {
        val reply = authenticated().let { it to it.authenticated }

        assertTrue(reply.second)
    }

    @Test fun rejected_credential_answers_revoked_and_closes() {
        val handler = handler()

        val reply = handler.receive(Codec.encode(Auth("pc-1", "nope")))

        assertEquals(AuthResult(false, "REVOKED", reply.messages.single().messageId), reply.messages.single())
        assertTrue(reply.close)
        assertFalse(handler.authenticated)
    }

    @Test fun pairing_request_with_scanned_invite_is_approved_then_closed() {
        registry.arm(Invite("pc-1", "Desktop", "token", 1_120))

        val reply = handler().receive(Codec.encode(PairRequest("pc-1", "Desktop", "token")))

        val approve = reply.messages.single() as PairApprove
        assertEquals("dev-1", approve.deviceId)
        assertEquals("credential", approve.credential)
        assertTrue(reply.close)
    }

    @Test fun pairing_request_without_invite_is_rejected() {
        val reply = handler().receive(Codec.encode(PairRequest("pc-1", "Desktop", "token")))

        assertEquals("PAIRING_REJECTED", (reply.messages.single() as ProtocolError).code)
        assertTrue(reply.close)
    }

    @Test fun nothing_is_sent_to_an_unauthenticated_connection() {
        val handler = handler()

        assertNull(handler.reading(10.0, 5, "2026-11-16T10:00:00Z"))
        assertNull(handler.status(SensorState.ACTIVE, null, null))
    }

    @Test fun garbage_before_authentication_closes_without_reply() {
        val reply = handler().receive("not json")

        assertTrue(reply.messages.isEmpty())
        assertTrue(reply.close)
    }

    @Test fun readings_carry_the_session_and_a_strictly_increasing_sequence() {
        val handler = authenticated()

        val first = handler.reading(10.0, 5, "2026-11-16T10:00:00Z")!!
        val second = handler.reading(11.0, 7, "2026-11-16T10:00:01Z")!!

        assertEquals("s1", first.sessionId)
        assertEquals(1L, first.sequence)
        assertEquals(2L, second.sequence)
        assertEquals(7L, second.sensorEventAgeMs)
    }

    @Test fun acks_are_accepted_silently() {
        val reply = authenticated().receive(Codec.encode(Ack("m1")))

        assertTrue(reply.messages.isEmpty())
        assertFalse(reply.close)
    }

    @Test fun computer_can_revoke_itself() {
        val handler = authenticated()

        val reply = handler.receive(Codec.encode(PairRevoke("pc-1")))

        assertTrue(reply.close)
        assertFalse(registry.authenticate("pc-1", "credential"))
    }

    @Test fun computer_cannot_revoke_another_computer() {
        val handler = authenticated()
        store.clients = store.clients + AuthorizedClient("pc-2", "Other", "hash")

        handler.receive(Codec.encode(PairRevoke("pc-2")))

        assertEquals(2, store.clients.size)
    }
}

class SensorHealthTest {
    @Test fun missing_sensor_is_unavailable() =
        assertEquals(SensorState.UNAVAILABLE to "NO_LIGHT_SENSOR", SensorHealth.evaluate(false, false, true))

    @Test fun unregistered_listener_is_an_error() =
        assertEquals(SensorState.ERROR to "LISTENER_NOT_REGISTERED", SensorHealth.evaluate(true, false, true))

    @Test fun screen_off_is_uncertain_until_proven_otherwise_on_real_devices() =
        assertEquals(SensorState.UNCERTAIN to "SCREEN_OFF", SensorHealth.evaluate(true, true, false))

    @Test fun registered_sensor_with_screen_on_is_active() =
        assertEquals(SensorState.ACTIVE to null, SensorHealth.evaluate(true, true, true))
}

class AddressesTest {
    @Test fun lan_ranges_are_private() {
        assertTrue(Addresses.isPrivateLan("192.168.1.236"))
        assertTrue(Addresses.isPrivateLan("192.168.137.175"))
        assertTrue(Addresses.isPrivateLan("10.0.2.15"))
        assertTrue(Addresses.isPrivateLan("172.16.0.5"))
        assertTrue(Addresses.isPrivateLan("172.31.255.1"))
    }

    @Test fun carrier_vpn_and_public_ranges_are_not() {
        assertFalse(Addresses.isPrivateLan("100.66.106.147"))
        assertFalse(Addresses.isPrivateLan("172.32.0.1"))
        assertFalse(Addresses.isPrivateLan("8.8.8.8"))
        assertFalse(Addresses.isPrivateLan("not an ip"))
    }
}
