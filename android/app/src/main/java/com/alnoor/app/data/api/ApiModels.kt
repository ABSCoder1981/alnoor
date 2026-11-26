package com.alnoor.app.data.api

import kotlinx.serialization.Serializable

// Response and request shapes of the ALNoor API (PRD 16).

@Serializable
data class ScheduleEntry(val dayOfWeek: Int, val startTimeLocal: String, val durationMinutes: Int)

@Serializable
data class TeacherRef(val id: String, val fullName: String)

@Serializable
data class EnrolledStudent(val id: String, val fullName: String, val enrollmentStatus: String)

@Serializable
data class ClassSummary(
    val id: String,
    val title: String,
    val description: String? = null,
    val timeZoneId: String,
    val defaultDurationMinutes: Int,
    val isActive: Boolean,
    val schedule: List<ScheduleEntry> = emptyList(),
    val nextOccurrences: List<String> = emptyList(),
    val studentCount: Int = 0,
    val liveSessionId: String? = null,
    val teacher: TeacherRef,
    val students: List<EnrolledStudent>? = null,
)

@Serializable
data class SaveClassRequest(
    val title: String,
    val description: String?,
    val timeZoneId: String,
    val defaultDurationMinutes: Int,
    val schedule: List<ScheduleEntry>,
    val isActive: Boolean? = null,
)

@Serializable
data class EnrollRequest(val studentIds: List<String>)

@Serializable
data class Student(
    val id: String,
    val fullName: String,
    val phoneNumber: String,
    val isActive: Boolean,
    val mustChangePassword: Boolean = false,
    val classCount: Int = 0,
)

@Serializable
data class RegisterStudentRequest(val fullName: String, val phoneNumber: String, val temporaryPassword: String)

@Serializable
data class UpdateStudentRequest(val fullName: String, val phoneNumber: String)

@Serializable
data class StudentStatusRequest(val isActive: Boolean)

@Serializable
data class ResetPasswordRequest(val temporaryPassword: String)

@Serializable
data class Session(
    val id: String,
    val classId: String,
    val classTitle: String,
    val status: String,
    val startedAt: String,
    val endedAt: String? = null,
    val endReason: String? = null,
) {
    val isLive: Boolean get() = status == "Live"
}

@Serializable
data class JoinInfo(
    val livekitUrl: String,
    val token: String,
    val expiresAt: String,
    val role: String,
    val roomName: String,
    val identity: String,
)

@Serializable
data class MuteAllResponse(val mutedCount: Int)

@Serializable
class EmptyBody
