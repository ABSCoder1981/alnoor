package com.alnoor.app

import com.alnoor.app.data.api.AlNoorApi
import com.alnoor.app.data.auth.ApiException
import com.alnoor.app.ui.classes.ClassForm
import com.alnoor.app.ui.classes.SlotForm
import com.alnoor.app.ui.common.Formats
import kotlinx.coroutines.test.runTest
import okhttp3.OkHttpClient
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Before
import org.junit.Test
import java.time.ZoneId

class ClassFormTest {
    private val valid = ClassForm(title = "Tajweed A", duration = "60", slots = listOf(SlotForm(1, "17:00", "45")))

    @Test
    fun validFormHasNoError() = assertNull(valid.validate())

    @Test
    fun rejectsShortTitleBadTimeAndLongSlot() {
        assertEquals(R.string.class_error_title, valid.copy(title = " T ").validate())
        assertEquals(R.string.class_error_time, valid.copy(slots = listOf(SlotForm(1, "25:00", "45"))).validate())
        assertEquals(R.string.class_error_time, valid.copy(slots = listOf(SlotForm(1, "5pm", "45"))).validate())
        assertEquals(R.string.class_error_duration, valid.copy(slots = listOf(SlotForm(1, "17:00", "500"))).validate())
        assertEquals(R.string.class_error_duration, valid.copy(duration = "").validate())
    }

    @Test
    fun requestTrimsAndConverts() {
        val r = valid.copy(title = "  Tajweed A  ", description = "  ").toRequest()
        assertEquals("Tajweed A", r.title)
        assertNull(r.description)
        assertEquals(45, r.schedule.single().durationMinutes)
    }
}

class FormatsTest {
    @Test
    fun temporaryPasswordsAreReadableAndLongEnough() {
        repeat(50) {
            val p = Formats.temporaryPassword()
            assertTrue(p, Regex("^[a-z]+-\\d{4}-[a-z]+$").matches(p))
            assertTrue(p.length >= 8)
        }
    }

    @Test
    fun utcTimesAreShownInTheGivenZone() {
        val text = Formats.localDateTime("2026-11-30T11:30:00Z", ZoneId.of("Asia/Kolkata"))
        assertTrue(text, text.contains("5:00") || text.contains("17:00"))
    }
}

class AlNoorApiTest {
    private lateinit var server: MockWebServer
    private lateinit var api: AlNoorApi

    @Before
    fun setUp() {
        server = MockWebServer().apply { start() }
        api = AlNoorApi(baseUrl = { server.url("/").toString() }, client = OkHttpClient())
    }

    @After
    fun tearDown() = server.shutdown()

    @Test
    fun noContentMeansNoActiveSession() = runTest {
        server.enqueue(MockResponse().setResponseCode(204))

        assertNull(api.call { activeSession("c1") })
    }

    @Test
    fun problemDetailsBecomeApiExceptionWithCode() = runTest {
        server.enqueue(MockResponse().setResponseCode(409).setBody("""{"code":"ROOM_FULL","title":"The class is full."}"""))

        try {
            api.require { join("s1") }
            fail("expected ApiException")
        } catch (e: ApiException) {
            assertEquals("ROOM_FULL", e.code)
            assertEquals("The class is full.", e.message)
        }
        assertEquals("/api/sessions/s1/join", server.takeRequest().path)
    }

    @Test
    fun moderationCallsUseTheDocumentedPaths() = runTest {
        repeat(3) { server.enqueue(MockResponse().setResponseCode(204)) }
        server.enqueue(MockResponse().setBody("""{"mutedCount":3}"""))

        api.call { mute("s1", "u1") }
        api.call { remove("s1", "u2") }
        api.call { unenroll("c1", "u3") }
        val muted = api.require { muteAll("s1") }

        assertEquals(3, muted.mutedCount)
        assertEquals("POST /api/sessions/s1/participants/u1/mute", server.takeRequest().let { "${it.method} ${it.path}" })
        assertEquals("POST /api/sessions/s1/participants/u2/remove", server.takeRequest().let { "${it.method} ${it.path}" })
        assertEquals("DELETE /api/classes/c1/students/u3", server.takeRequest().let { "${it.method} ${it.path}" })
        assertEquals("POST /api/sessions/s1/participants/mute-all", server.takeRequest().let { "${it.method} ${it.path}" })
    }

    @Test
    fun unconfiguredServerIsReported() = runTest {
        val unconfigured = AlNoorApi(baseUrl = { "" }, client = OkHttpClient())
        try {
            unconfigured.call { classes() }
            fail("expected ApiException")
        } catch (e: ApiException) {
            assertEquals(ApiException.NOT_CONFIGURED, e.code)
        }
    }
}
