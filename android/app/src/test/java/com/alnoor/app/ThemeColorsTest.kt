package com.alnoor.app

import androidx.compose.ui.graphics.toArgb
import com.alnoor.app.ui.theme.DeepGreen
import com.alnoor.app.ui.theme.ErrorRed
import org.junit.Assert.assertEquals
import org.junit.Test

class ThemeColorsTest {
    @Test
    fun paletteMatchesPrd() {
        assertEquals(0xFF0F5132.toInt(), DeepGreen.toArgb())
        assertEquals(0xFFB3261E.toInt(), ErrorRed.toArgb())
    }
}
