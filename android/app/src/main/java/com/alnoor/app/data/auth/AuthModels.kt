package com.alnoor.app.data.auth

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.contentOrNull

@Serializable
data class LoginRequest(
    val phoneNumber: String,
    val password: String,
    val deviceInfo: String,
    val clientType: String = "Android",
)

@Serializable
data class RefreshRequest(val refreshToken: String, val clientType: String = "Android")

@Serializable
data class LogoutRequest(val refreshToken: String?)

@Serializable
data class ChangePasswordRequest(val currentPassword: String, val newPassword: String, val refreshToken: String?)

@Serializable
data class AuthUser(val id: String, val fullName: String, val role: String, val mustChangePassword: Boolean) {
    val isTeacher: Boolean get() = role == "Teacher"
}

@Serializable
data class AuthResponse(
    val accessToken: String,
    val accessTokenExpiresAt: String,
    val refreshToken: String? = null,
    val refreshTokenExpiresAt: String,
    val user: AuthUser,
)

/** An API error with the stable machine-readable code from the server (PRD 16.1). */
class ApiException(
    val status: Int,
    val code: String,
    message: String,
    val fieldErrors: Map<String, List<String>> = emptyMap(),
    val lockoutEndsAt: String? = null,
) : Exception(message) {
    companion object {
        const val NETWORK_ERROR = "NETWORK_ERROR"
        const val NOT_CONFIGURED = "NOT_CONFIGURED"

        /** Parses an RFC 9457 ProblemDetails body; tolerates empty or non-JSON bodies. */
        fun fromProblem(status: Int, body: String?, json: Json): ApiException {
            val problem = runCatching { json.parseToJsonElement(body.orEmpty()) as? JsonObject }.getOrNull()
            fun text(key: String) = (problem?.get(key) as? JsonPrimitive)?.contentOrNull
            val errors = (problem?.get("errors") as? JsonObject)?.mapValues { (_, value) ->
                (value as? JsonArray)?.mapNotNull { (it as? JsonPrimitive)?.contentOrNull }.orEmpty()
            }.orEmpty()
            return ApiException(
                status = status,
                code = text("code") ?: "UNKNOWN",
                message = text("title") ?: "The server returned an error (HTTP $status).",
                fieldErrors = errors,
                lockoutEndsAt = text("lockoutEndsAt"),
            )
        }
    }
}
