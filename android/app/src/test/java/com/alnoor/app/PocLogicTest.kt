package com.alnoor.app

import com.alnoor.app.data.DevTokenRepository
import com.alnoor.app.livekit.AudioTestSettings
import com.alnoor.app.livekit.audioCaptureOptions
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class PocLogicTest {
    @Test
    fun defaultsMatchPrdStartingValues() {
        val settings = AudioTestSettings()
        assertEquals(32, settings.bitrateKbps)
        assertTrue(settings.dtx)
        assertFalse(settings.autoGainControl)
        assertEquals("32 kbps · DTX on · NS on · AGC off", settings.describe())
    }

    @Test
    fun captureOptionsFollowSettingsAndKeepEchoCancellation() {
        val options = AudioTestSettings(noiseSuppression = false, autoGainControl = true).audioCaptureOptions()
        assertFalse(options.noiseSuppression)
        assertTrue(options.autoGainControl)
        assertTrue(options.echoCancellation)
    }

    @Test
    fun unsupportedBitrateIsRejected() {
        assertThrows(IllegalArgumentException::class.java) { AudioTestSettings(bitrateKbps = 64) }
    }

    @Test
    fun baseUrlIsNormalisedForRetrofit() {
        assertEquals("http://10.0.2.2:5080/", DevTokenRepository.normalizeBaseUrl(" http://10.0.2.2:5080 "))
        assertEquals("http://192.168.1.8:5080/", DevTokenRepository.normalizeBaseUrl("http://192.168.1.8:5080/"))
        assertNull(DevTokenRepository.normalizeBaseUrl("192.168.1.8:5080"))
        assertNull(DevTokenRepository.normalizeBaseUrl("http://"))
    }
}
