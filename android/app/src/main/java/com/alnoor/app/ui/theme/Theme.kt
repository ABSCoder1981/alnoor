package com.alnoor.app.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

// PRD section 11.1 starting palette; the final palette is decided in Phase 4.
val DeepGreen = Color(0xFF0F5132)
val SubtleGold = Color(0xFFC9A227)
val OffWhite = Color(0xFFFAFAF7)
val ErrorRed = Color(0xFFB3261E)

private val LightColors = lightColorScheme(
    primary = DeepGreen,
    onPrimary = Color.White,
    secondary = SubtleGold,
    onSecondary = Color.Black,
    background = OffWhite,
    surface = OffWhite,
    error = ErrorRed,
    onError = Color.White,
)

@Composable
fun ALNoorTheme(content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = LightColors, content = content)
}
