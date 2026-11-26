package com.alnoor.app

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import com.alnoor.app.ui.auth.AuthRoot
import com.alnoor.app.ui.auth.AuthViewModel
import com.alnoor.app.ui.theme.ALNoorTheme

class MainActivity : ComponentActivity() {
    private val authViewModel: AuthViewModel by viewModels {
        AuthViewModel.factory((application as ALNoorApplication).container)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            ALNoorTheme {
                AuthRoot(authViewModel)
            }
        }
    }
}
