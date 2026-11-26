package com.alnoor.app.data.api

import com.alnoor.app.data.auth.ApiException
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import retrofit2.Response
import retrofit2.Retrofit
import retrofit2.converter.kotlinx.serialization.asConverterFactory
import retrofit2.http.Body
import retrofit2.http.DELETE
import retrofit2.http.GET
import retrofit2.http.POST
import retrofit2.http.PUT
import retrofit2.http.Path
import retrofit2.http.Query
import java.io.IOException

/** Protected API endpoints (PRD 16.3–16.5). The client adds the bearer token and refreshes on 401. */
interface AlNoorService {
    @GET("api/classes")
    suspend fun classes(@Query("includeInactive") includeInactive: Boolean = false): Response<List<ClassSummary>>

    @GET("api/classes/{id}")
    suspend fun classDetails(@Path("id") id: String): Response<ClassSummary>

    @POST("api/classes")
    suspend fun createClass(@Body body: SaveClassRequest): Response<ClassSummary>

    @PUT("api/classes/{id}")
    suspend fun updateClass(@Path("id") id: String, @Body body: SaveClassRequest): Response<ClassSummary>

    @POST("api/classes/{id}/students")
    suspend fun enroll(@Path("id") id: String, @Body body: EnrollRequest): Response<List<EnrolledStudent>>

    @DELETE("api/classes/{id}/students/{studentId}")
    suspend fun unenroll(@Path("id") id: String, @Path("studentId") studentId: String): Response<Unit>

    @GET("api/students")
    suspend fun students(@Query("search") search: String? = null): Response<List<Student>>

    @POST("api/auth/register")
    suspend fun registerStudent(@Body body: RegisterStudentRequest): Response<Student>

    @PUT("api/students/{id}")
    suspend fun updateStudent(@Path("id") id: String, @Body body: UpdateStudentRequest): Response<Student>

    @PUT("api/students/{id}/status")
    suspend fun setStudentStatus(@Path("id") id: String, @Body body: StudentStatusRequest): Response<Student>

    @POST("api/students/{id}/reset-password")
    suspend fun resetPassword(@Path("id") id: String, @Body body: ResetPasswordRequest): Response<Unit>

    @POST("api/classes/{id}/sessions")
    suspend fun startSession(@Path("id") classId: String, @Body body: EmptyBody = EmptyBody()): Response<Session>

    @GET("api/classes/{id}/sessions/active")
    suspend fun activeSession(@Path("id") classId: String): Response<Session>

    @GET("api/sessions/{id}")
    suspend fun session(@Path("id") id: String): Response<Session>

    @POST("api/sessions/{id}/join")
    suspend fun join(@Path("id") id: String, @Body body: EmptyBody = EmptyBody()): Response<JoinInfo>

    @POST("api/sessions/{id}/leave")
    suspend fun leave(@Path("id") id: String, @Body body: EmptyBody = EmptyBody()): Response<Unit>

    @POST("api/sessions/{id}/end")
    suspend fun end(@Path("id") id: String, @Body body: EmptyBody = EmptyBody()): Response<Session>

    @POST("api/sessions/{id}/participants/{userId}/mute")
    suspend fun mute(@Path("id") id: String, @Path("userId") userId: String, @Body body: EmptyBody = EmptyBody()): Response<Unit>

    @POST("api/sessions/{id}/participants/mute-all")
    suspend fun muteAll(@Path("id") id: String, @Body body: EmptyBody = EmptyBody()): Response<MuteAllResponse>

    @POST("api/sessions/{id}/participants/{userId}/remove")
    suspend fun remove(@Path("id") id: String, @Path("userId") userId: String, @Body body: EmptyBody = EmptyBody()): Response<Unit>
}

/** Builds the service for the current server address and unwraps responses into values or [ApiException]. */
class AlNoorApi(private val baseUrl: () -> String, private val client: OkHttpClient) {
    private val json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
    }

    @Volatile
    private var cached: Pair<String, AlNoorService>? = null

    fun service(): AlNoorService {
        val url = baseUrl().trim().let { if (it.endsWith("/")) it else "$it/" }
        if (!url.startsWith("http://") && !url.startsWith("https://")) {
            throw ApiException(0, ApiException.NOT_CONFIGURED, "The server address is not set.")
        }
        cached?.let { (u, s) -> if (u == url) return s }
        val created = Retrofit.Builder()
            .baseUrl(url)
            .client(client)
            .addConverterFactory(json.asConverterFactory("application/json".toMediaType()))
            .build()
            .create(AlNoorService::class.java)
        cached = url to created
        return created
    }

    /** Runs a call; returns the body (null for 204) or throws [ApiException] with the server's code. */
    suspend fun <T> call(block: suspend AlNoorService.() -> Response<T>): T? {
        val response = try {
            service().block()
        } catch (_: IOException) {
            throw ApiException(0, ApiException.NETWORK_ERROR, "The server cannot be reached.")
        }
        if (!response.isSuccessful) throw ApiException.fromProblem(response.code(), response.errorBody()?.string(), json)
        return response.body()
    }

    suspend fun <T : Any> require(block: suspend AlNoorService.() -> Response<T>): T =
        call(block) ?: throw ApiException(0, "EMPTY_RESPONSE", "The server returned an empty response.")
}
