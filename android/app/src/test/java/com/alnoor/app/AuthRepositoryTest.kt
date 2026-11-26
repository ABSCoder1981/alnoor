package com.alnoor.app

import com.alnoor.app.data.auth.ApiException
import com.alnoor.app.data.auth.AuthRepository
import com.alnoor.app.data.auth.RefreshTokenStore
import kotlinx.coroutines.test.runTest
import okhttp3.Request
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Before
import org.junit.Test

private class MemoryTokenStore(var token: String? = null) : RefreshTokenStore {
    override suspend fun get() = token
    override suspend fun set(token: String?) {
        this.token = token
    }
}

class AuthRepositoryTest {
    private lateinit var server: MockWebServer
    private lateinit var store: MemoryTokenStore
    private lateinit var repo: AuthRepository

    private fun authBody(access: String, refresh: String, mustChange: Boolean = false) = """
        {"accessToken":"$access","accessTokenExpiresAt":"2026-11-25T10:15:00Z","refreshToken":"$refresh",
         "refreshTokenExpiresAt":"2026-12-25T10:00:00Z",
         "user":{"id":"u1","fullName":"Aisha","role":"Student","mustChangePassword":$mustChange}}
    """.trimIndent()

    private fun json(code: Int, body: String) = MockResponse().setResponseCode(code).setHeader("Content-Type", "application/json").setBody(body)

    @Before
    fun setUp() {
        server = MockWebServer().apply { start() }
        store = MemoryTokenStore()
        repo = AuthRepository(baseUrl = { server.url("/").toString() }, tokenStore = store, deviceInfo = "test-device")
    }

    @After
    fun tearDown() = server.shutdown()

    @Test
    fun loginStoresOnlyTheRefreshTokenAndSendsAndroidClientType() = runTest {
        server.enqueue(json(200, authBody("access-1", "refresh-1")))

        val user = repo.login("+919800000001", "secret-pass")

        assertEquals("Aisha", user.fullName)
        assertEquals("refresh-1", store.token)
        val request = server.takeRequest()
        assertEquals("/api/auth/login", request.path)
        val body = request.body.readUtf8()
        assertTrue(body, body.contains("\"clientType\":\"Android\"") && body.contains("\"deviceInfo\":\"test-device\""))
    }

    @Test
    fun loginErrorsCarryTheServerCodeAndLockoutTime() = runTest {
        server.enqueue(json(423, """{"code":"ACCOUNT_LOCKED","title":"Locked","lockoutEndsAt":"2026-11-25T10:15:00Z"}"""))

        try {
            repo.login("+919800000001", "x")
            fail("expected ApiException")
        } catch (e: ApiException) {
            assertEquals(423, e.status)
            assertEquals("ACCOUNT_LOCKED", e.code)
            assertEquals("2026-11-25T10:15:00Z", e.lockoutEndsAt)
        }
        assertNull(store.token)
    }

    @Test
    fun unreachableServerIsANetworkError() = runTest {
        server.shutdown()

        try {
            repo.login("+919800000001", "x")
            fail("expected ApiException")
        } catch (e: ApiException) {
            assertEquals(ApiException.NETWORK_ERROR, e.code)
        }
    }

    @Test
    fun restoreRotatesTheStoredRefreshToken() = runTest {
        store.token = "refresh-old"
        server.enqueue(json(200, authBody("access-2", "refresh-new")))

        val user = repo.restore()

        assertEquals("u1", user?.id)
        assertEquals("refresh-new", store.token)
        assertTrue(server.takeRequest().body.readUtf8().contains("\"refreshToken\":\"refresh-old\""))
    }

    @Test
    fun rejectedRefreshSignsOutAndClearsTheStoredToken() = runTest {
        store.token = "refresh-stolen"
        server.enqueue(json(401, """{"code":"REFRESH_TOKEN_REUSED"}"""))

        assertNull(repo.restore())
        assertNull(store.token)
    }

    @Test
    fun restoreWithoutStoredTokenDoesNotCallTheServer() = runTest {
        assertNull(repo.restore())
        assertEquals(0, server.requestCount)
    }

    @Test
    fun expiredAccessTokenIsRefreshedOnceAndTheRequestRetried() = runTest {
        server.enqueue(json(200, authBody("access-1", "refresh-1")))
        repo.login("+919800000001", "secret-pass")
        server.takeRequest()

        server.enqueue(json(401, """{"code":"UNAUTHENTICATED"}""")) // protected call with expired token
        server.enqueue(json(200, authBody("access-2", "refresh-2"))) // refresh
        server.enqueue(json(200, "[]")) // retried call

        val response = repo.authenticatedClient.newCall(Request.Builder().url(server.url("/api/classes")).build()).execute()

        assertEquals(200, response.code)
        assertEquals("Bearer access-1", server.takeRequest().getHeader("Authorization"))
        assertEquals("/api/auth/refresh", server.takeRequest().path)
        assertEquals("Bearer access-2", server.takeRequest().getHeader("Authorization"))
        assertEquals("refresh-2", store.token)
    }

    @Test
    fun otherUnauthorizedErrorsAreNotTreatedAsExpiredSessions() = runTest {
        server.enqueue(json(200, authBody("access-1", "refresh-1")))
        repo.login("+919800000001", "secret-pass")
        server.takeRequest()
        server.enqueue(json(401, """{"code":"INVALID_CREDENTIALS","title":"The current password is incorrect."}"""))

        try {
            repo.changePassword("wrong-current", "a-new-password")
            fail("expected ApiException")
        } catch (e: ApiException) {
            assertEquals("INVALID_CREDENTIALS", e.code)
        }
        assertEquals(2, server.requestCount) // login + password, no refresh
        assertEquals("refresh-1", store.token)
    }

    @Test
    fun changePasswordSendsTheRefreshTokenSoThisDeviceStaysSignedIn() = runTest {
        server.enqueue(json(200, authBody("access-1", "refresh-1")))
        repo.login("+919800000001", "secret-pass")
        server.takeRequest()
        server.enqueue(MockResponse().setResponseCode(204))

        repo.changePassword("old-password", "new-password-1")

        val request = server.takeRequest()
        assertEquals("PUT", request.method)
        assertEquals("Bearer access-1", request.getHeader("Authorization"))
        assertTrue(request.body.readUtf8().contains("\"refreshToken\":\"refresh-1\""))
    }

    @Test
    fun logoutClearsTokensEvenWhenOffline() = runTest {
        server.enqueue(json(200, authBody("access-1", "refresh-1")))
        repo.login("+919800000001", "secret-pass")
        server.shutdown()

        repo.logout()

        assertNull(store.token)
    }

    @Test
    fun missingServerAddressIsReportedClearly() = runTest {
        val unconfigured = AuthRepository(baseUrl = { "" }, tokenStore = store, deviceInfo = "d")

        try {
            unconfigured.login("+919800000001", "x")
            fail("expected ApiException")
        } catch (e: ApiException) {
            assertEquals(ApiException.NOT_CONFIGURED, e.code)
        }
    }
}
