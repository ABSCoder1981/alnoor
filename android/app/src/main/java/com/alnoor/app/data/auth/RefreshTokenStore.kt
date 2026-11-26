package com.alnoor.app.data.auth

import android.content.Context
import android.util.Base64
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import com.google.crypto.tink.Aead
import com.google.crypto.tink.KeyTemplates
import com.google.crypto.tink.RegistryConfiguration
import com.google.crypto.tink.aead.AeadConfig
import com.google.crypto.tink.integration.android.AndroidKeysetManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.withContext
import java.security.GeneralSecurityException

/** Where the long-lived refresh token is kept. The access token is never persisted (PRD 17.1). */
interface RefreshTokenStore {
    suspend fun get(): String?
    suspend fun set(token: String?)
}

private val Context.authDataStore by preferencesDataStore(name = "auth")

/**
 * Refresh token encrypted with AES-256-GCM (Tink). The Tink keyset is itself encrypted by a key that lives in the
 * Android Keystore, so the token cannot be read from a copy of the app's files. Backups are disabled
 * (allowBackup=false), and security-crypto's EncryptedSharedPreferences is deprecated (PRD 13).
 */
class EncryptedRefreshTokenStore(private val context: Context) : RefreshTokenStore {
    private val key = stringPreferencesKey("refresh_token")
    private val associatedData = "alnoor.refresh_token".toByteArray()

    private val aead: Aead by lazy {
        AeadConfig.register()
        AndroidKeysetManager.Builder()
            .withSharedPref(context, KEYSET_NAME, KEYSET_PREFS)
            .withKeyTemplate(KeyTemplates.get("AES256_GCM"))
            .withMasterKeyUri(MASTER_KEY_URI)
            .build()
            .keysetHandle
            .getPrimitive(RegistryConfiguration.get(), Aead::class.java)
    }

    override suspend fun get(): String? = withContext(Dispatchers.IO) {
        val stored = context.authDataStore.data.first()[key] ?: return@withContext null
        try {
            String(aead.decrypt(Base64.decode(stored, Base64.NO_WRAP), associatedData))
        } catch (_: GeneralSecurityException) {
            // Keystore key lost (e.g. device security reset): the token is unusable, so sign in again.
            context.authDataStore.edit { it.remove(key) }
            null
        } catch (_: IllegalArgumentException) {
            context.authDataStore.edit { it.remove(key) }
            null
        }
    }

    override suspend fun set(token: String?) = withContext(Dispatchers.IO) {
        context.authDataStore.edit { prefs ->
            if (token == null) {
                prefs.remove(key)
            } else {
                prefs[key] = Base64.encodeToString(aead.encrypt(token.toByteArray(), associatedData), Base64.NO_WRAP)
            }
        }
        Unit
    }

    private companion object {
        const val KEYSET_NAME = "alnoor_auth_keyset"
        const val KEYSET_PREFS = "alnoor_auth_keyset_prefs"
        const val MASTER_KEY_URI = "android-keystore://alnoor_auth_master_key"
    }
}
