package com.alnoor.app

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import com.alnoor.app.ui.AppShell
import com.alnoor.app.ui.poc.PocScreen
import com.alnoor.app.ui.theme.ALNoorTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            ALNoorTheme {
                // Phase 2 LiveKit test client is only reachable in debug builds.
                if (BuildConfig.DEBUG) PocScreen() else AppShell()
            }
        }
    }
}
