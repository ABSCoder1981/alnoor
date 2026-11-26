package com.alnoor.app

import android.app.Application
import android.content.Context
import android.os.Build
import com.alnoor.app.data.auth.AuthRepository
import com.alnoor.app.data.auth.EncryptedRefreshTokenStore

/**
 * Manual dependency container (one instance per process). Kept deliberately small; see docs/adr/0002 for why
 * Hilt is not used yet.
 */
class AppContainer(context: Context) {
    private val prefs = context.getSharedPreferences("settings", Context.MODE_PRIVATE)

    /** Debug builds let testers point the app at a PC on the same Wi-Fi; release builds use the built-in address. */
    var apiBaseUrl: String
        get() = if (BuildConfig.DEBUG) prefs.getString(KEY_API_URL, null) ?: BuildConfig.API_BASE_URL else BuildConfig.API_BASE_URL
        set(value) {
            if (BuildConfig.DEBUG) prefs.edit().putString(KEY_API_URL, value.trim()).apply()
        }

    val auth = AuthRepository(
        baseUrl = { apiBaseUrl },
        tokenStore = EncryptedRefreshTokenStore(context.applicationContext),
        deviceInfo = "${Build.MANUFACTURER} ${Build.MODEL} / Android ${Build.VERSION.RELEASE}".take(100),
    )

    private companion object {
        const val KEY_API_URL = "api_base_url"
    }
}

class ALNoorApplication : Application() {
    lateinit var container: AppContainer
        private set

    override fun onCreate() {
        super.onCreate()
        container = AppContainer(this)
    }
}
