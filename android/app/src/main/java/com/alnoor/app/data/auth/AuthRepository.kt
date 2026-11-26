package com.alnoor.app.data.auth

import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.json.Json
import okhttp3.Authenticator
import okhttp3.Interceptor
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.Route
import retrofit2.Retrofit
import retrofit2.converter.kotlinx.serialization.asConverterFactory
import java.io.IOException
import java.util.concurrent.TimeUnit

/**
 * Sign-in state and tokens (PRD 17.1). The access token is kept in memory only; the refresh token goes to
 * [RefreshTokenStore]. [authenticatedClient] adds the bearer token and refreshes once on 401.
 */
class AuthRepository(
    private val baseUrl: () -> String,
    private val tokenStore: RefreshTokenStore,
    private val deviceInfo: String,
    baseClient: OkHttpClient = OkHttpClient(),
) {
    // encodeDefaults: always send clientType explicitly.
    private val json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
    }
    private val refreshMutex = Mutex()

    @Volatile
    private var accessToken: String? = null

    private val _sessionEnded = MutableSharedFlow<Unit>(extraBufferCapacity = 1)

    /** Emits when the session ends without the user asking (refresh rejected, account deactivated). */
    val sessionEnded: SharedFlow<Unit> = _sessionEnded

    private val publicClient = baseClient.newBuilder()
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(20, TimeUnit.SECONDS)
        .build()

    /** For protected APIs: adds the bearer token and refreshes on 401 (once). */
    val authenticatedClient: OkHttpClient = publicClient.newBuilder()
        .addInterceptor(BearerInterceptor())
        .authenticator(RefreshAuthenticator())
        .build()

    private fun api(client: OkHttpClient): AuthApi {
        val url = baseUrl().trim()
        if (!url.startsWith("http://") && !url.startsWith("https://")) {
            throw ApiException(0, ApiException.NOT_CONFIGURED, "The server address is not set.")
        }
        return Retrofit.Builder()
            .baseUrl(if (url.endsWith("/")) url else "$url/")
            .client(client)
            .addConverterFactory(json.asConverterFactory("application/json".toMediaType()))
            .build()
            .create(AuthApi::class.java)
    }

    suspend fun login(phoneNumber: String, password: String): AuthUser {
        val response = call { api(publicClient).login(LoginRequest(phoneNumber, password, deviceInfo)) }
        val body = response.body()
        if (!response.isSuccessful || body == null) {
            throw ApiException.fromProblem(response.code(), response.errorBody()?.string(), json)
        }
        accept(body)
        return body.user
    }

    /** Restores the session at app start. Returns null if signed out; throws [ApiException] when offline. */
    suspend fun restore(): AuthUser? = refreshMutex.withLock { refreshLocked() }

    /**
     * Refreshes unless another caller already did after [failedToken] was rejected.
     * Returns the current access token, or null when the session has ended.
     */
    suspend fun refreshAfter(failedToken: String?): String? = refreshMutex.withLock {
        val current = accessToken
        if (current != null && current != failedToken) return@withLock current
        try {
            refreshLocked()
        } catch (_: ApiException) {
            return@withLock null // Offline or server error: fail this request, keep the stored token for later.
        }
        accessToken
    }

    private suspend fun refreshLocked(): AuthUser? {
        val stored = tokenStore.get() ?: return null
        val response = call { api(publicClient).refresh(RefreshRequest(stored)) }
        val body = response.body()
        if (response.isSuccessful && body != null) {
            accept(body)
            return body.user
        }
        if (response.code() == 401 || response.code() == 403) {
            endSession(notify = true)
            return null
        }
        throw ApiException.fromProblem(response.code(), response.errorBody()?.string(), json)
    }

    suspend fun logout() {
        val stored = tokenStore.get()
        try {
            if (accessToken != null) call { api(authenticatedClient).logout(LogoutRequest(stored)) }
        } catch (_: ApiException) {
            // Signing out locally must work offline too; the server token expires on its own.
        } finally {
            endSession(notify = false)
        }
    }

    /** Keeps this device signed in (sends its refresh token) and signs out everywhere else (PRD 16.2). */
    suspend fun changePassword(currentPassword: String, newPassword: String) {
        val response = call { api(authenticatedClient).changePassword(ChangePasswordRequest(currentPassword, newPassword, tokenStore.get())) }
        if (!response.isSuccessful) throw ApiException.fromProblem(response.code(), response.errorBody()?.string(), json)
    }

    private suspend fun accept(body: AuthResponse) {
        accessToken = body.accessToken
        body.refreshToken?.let { tokenStore.set(it) }
    }

    private suspend fun endSession(notify: Boolean) {
        accessToken = null
        tokenStore.set(null)
        if (notify) _sessionEnded.tryEmit(Unit)
    }

    private suspend fun <T> call(block: suspend () -> T): T = try {
        block()
    } catch (_: IOException) {
        throw ApiException(0, ApiException.NETWORK_ERROR, "The server cannot be reached.")
    }

    private inner class BearerInterceptor : Interceptor {
        override fun intercept(chain: Interceptor.Chain): Response {
            val token = accessToken ?: return chain.proceed(chain.request())
            return chain.proceed(chain.request().newBuilder().header("Authorization", "Bearer $token").build())
        }
    }

    private inner class RefreshAuthenticator : Authenticator {
        override fun authenticate(route: Route?, response: Response): Request? {
            if (response.priorResponse != null) return null // Already retried once.
            // Only an expired/invalid access token is a reason to refresh. Other 401s (e.g. wrong current password
            // when changing it) carry their own code and must reach the caller unchanged.
            val code = runCatching { ApiException.fromProblem(401, response.peekBody(4096).string(), json).code }.getOrNull()
            if (code != "UNAUTHENTICATED") return null
            val failed = response.request.header("Authorization")?.removePrefix("Bearer ") ?: return null
            // OkHttp authenticators are synchronous; this runs on OkHttp's background thread.
            val fresh = runBlocking { refreshAfter(failed) } ?: return null
            return response.request.newBuilder().header("Authorization", "Bearer $fresh").build()
        }
    }
}
