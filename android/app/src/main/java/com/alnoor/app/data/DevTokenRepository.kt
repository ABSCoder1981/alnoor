package com.alnoor.app.data

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonPrimitive
import okhttp3.MediaType.Companion.toMediaType
import retrofit2.Response
import retrofit2.Retrofit
import retrofit2.converter.kotlinx.serialization.asConverterFactory
import retrofit2.http.Body
import retrofit2.http.POST
import java.io.IOException

@Serializable
data class DevTokenRequest(val name: String, val room: String, val role: String)

@Serializable
data class DevTokenResponse(val url: String, val token: String, val identity: String, val expiresAt: String)

private interface DevTokenApi {
    @POST("api/dev/livekit-token")
    suspend fun createToken(@Body request: DevTokenRequest): Response<DevTokenResponse>
}

sealed interface DevTokenResult {
    data class Success(val response: DevTokenResponse) : DevTokenResult
    data class Failure(val message: String) : DevTokenResult
}

/**
 * Phase 2 only: requests a LiveKit token from the API's Development-only endpoint.
 * Replaced by the authorised session join call in Phase 4.
 */
class DevTokenRepository {
    private val json = Json { ignoreUnknownKeys = true }

    suspend fun requestToken(apiBaseUrl: String, request: DevTokenRequest): DevTokenResult {
        val baseUrl = normalizeBaseUrl(apiBaseUrl)
            ?: return DevTokenResult.Failure("Enter the API address, e.g. http://192.168.1.8:5080")

        val api = Retrofit.Builder()
            .baseUrl(baseUrl)
            .addConverterFactory(json.asConverterFactory("application/json".toMediaType()))
            .build()
            .create(DevTokenApi::class.java)

        val response = try {
            api.createToken(request)
        } catch (_: IOException) {
            return DevTokenResult.Failure("The API at $baseUrl cannot be reached. Check the address and that the API runs with the 'lan' profile.")
        }

        val body = response.body()
        return when {
            response.isSuccessful && body != null -> DevTokenResult.Success(body)
            response.code() == 404 -> DevTokenResult.Failure("The test token endpoint is not available. Run the API in the Development environment.")
            response.code() == 400 -> DevTokenResult.Failure(validationMessage(response.errorBody()?.string()))
            else -> DevTokenResult.Failure("The API returned an error (HTTP ${response.code()}).")
        }
    }

    private fun validationMessage(problemJson: String?): String {
        val messages = runCatching {
            val errors = json.parseToJsonElement(problemJson.orEmpty()) as? JsonObject
            (errors?.get("errors") as? JsonObject)?.values
                ?.flatMap { (it as? JsonArray).orEmpty() }
                ?.map { it.jsonPrimitive.content }
        }.getOrNull()
        return if (messages.isNullOrEmpty()) "The request was not valid." else messages.joinToString(" ")
    }

    companion object {
        /** Returns the URL with a trailing slash (Retrofit requirement), or null if it is not http(s). */
        fun normalizeBaseUrl(input: String): String? {
            val trimmed = input.trim()
            if (!trimmed.startsWith("http://") && !trimmed.startsWith("https://")) return null
            if (trimmed.length <= "https://".length) return null
            return if (trimmed.endsWith("/")) trimmed else "$trimmed/"
        }
    }
}
