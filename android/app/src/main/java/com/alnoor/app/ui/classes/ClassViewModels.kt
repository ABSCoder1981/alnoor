package com.alnoor.app.ui.classes

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.alnoor.app.data.api.AlNoorApi
import com.alnoor.app.data.api.ClassSummary
import com.alnoor.app.data.api.EnrollRequest
import com.alnoor.app.data.api.SaveClassRequest
import com.alnoor.app.data.api.ScheduleEntry
import com.alnoor.app.data.api.Session
import com.alnoor.app.data.api.Student
import com.alnoor.app.data.auth.ApiException
import com.alnoor.app.ui.common.Load
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import java.time.ZoneId

const val MAX_STUDENTS = 9

class ClassDetailViewModel(private val api: AlNoorApi, private val classId: String, private val isTeacher: Boolean) : ViewModel() {
    private val _cls = MutableStateFlow<Load<ClassSummary>>(Load.Loading)
    val cls: StateFlow<Load<ClassSummary>> = _cls.asStateFlow()

    private val _students = MutableStateFlow<List<Student>>(emptyList())
    val students: StateFlow<List<Student>> = _students.asStateFlow()

    private val _busy = MutableStateFlow(false)
    val busy: StateFlow<Boolean> = _busy.asStateFlow()

    private val _error = MutableStateFlow<ApiException?>(null)
    val error: StateFlow<ApiException?> = _error.asStateFlow()

    init {
        load()
    }

    fun load() {
        viewModelScope.launch {
            _cls.value = try {
                Load.Ready(api.require { classDetails(classId) })
            } catch (e: ApiException) {
                Load.Failed(e)
            }
            if (isTeacher) runCatching { _students.value = api.require { students() } }
        }
    }

    private fun act(block: suspend () -> Unit) {
        _busy.value = true
        _error.value = null
        viewModelScope.launch {
            try {
                block()
                load()
            } catch (e: ApiException) {
                _error.value = e
            } finally {
                _busy.value = false
            }
        }
    }

    fun enroll(ids: List<String>) = act { api.call { enroll(classId, EnrollRequest(ids)) } }

    fun unenroll(studentId: String) = act { api.call { unenroll(classId, studentId) } }

    fun setActive(c: ClassSummary, active: Boolean) = act {
        api.call { updateClass(classId, SaveClassRequest(c.title, c.description, c.timeZoneId, c.defaultDurationMinutes, c.schedule, active)) }
    }

    fun start(onStarted: (Session) -> Unit) {
        _busy.value = true
        _error.value = null
        viewModelScope.launch {
            try {
                onStarted(api.require { startSession(classId) })
            } catch (e: ApiException) {
                _error.value = e
            } finally {
                _busy.value = false
            }
        }
    }
}

data class SlotForm(val dayOfWeek: Int = 1, val time: String = "17:00", val minutes: String = "60")

data class ClassForm(
    val title: String = "",
    val description: String = "",
    val timeZoneId: String = ZoneId.systemDefault().id,
    val duration: String = "60",
    val slots: List<SlotForm> = emptyList(),
    val isActive: Boolean? = null,
) {
    /** Client checks mirror the server (PRD 16.4); returns a string resource id or null. */
    fun validate(): Int? {
        val t = title.trim()
        if (t.length !in 2..100) return com.alnoor.app.R.string.class_error_title
        if (duration.toIntOrNull() !in 10..180) return com.alnoor.app.R.string.class_error_duration
        slots.forEach { s ->
            if (!TIME.matches(s.time)) return com.alnoor.app.R.string.class_error_time
            if (s.minutes.toIntOrNull() !in 10..180) return com.alnoor.app.R.string.class_error_duration
        }
        return null
    }

    fun toRequest() = SaveClassRequest(
        title = title.trim(),
        description = description.trim().ifEmpty { null },
        timeZoneId = timeZoneId.trim(),
        defaultDurationMinutes = duration.toInt(),
        schedule = slots.map { ScheduleEntry(it.dayOfWeek, it.time, it.minutes.toInt()) },
        isActive = isActive,
    )

    companion object {
        private val TIME = Regex("^([01]\\d|2[0-3]):[0-5]\\d$")

        fun from(c: ClassSummary) = ClassForm(
            title = c.title,
            description = c.description.orEmpty(),
            timeZoneId = c.timeZoneId,
            duration = c.defaultDurationMinutes.toString(),
            slots = c.schedule.map { SlotForm(it.dayOfWeek, it.startTimeLocal, it.durationMinutes.toString()) },
            isActive = c.isActive,
        )
    }
}

/** Create (classId == null) or edit a class. */
class ClassEditViewModel(private val api: AlNoorApi, private val classId: String?) : ViewModel() {
    private val _form = MutableStateFlow(ClassForm())
    val form: StateFlow<ClassForm> = _form.asStateFlow()

    private val _loading = MutableStateFlow(classId != null)
    val loading: StateFlow<Boolean> = _loading.asStateFlow()

    private val _saving = MutableStateFlow(false)
    val saving: StateFlow<Boolean> = _saving.asStateFlow()

    private val _error = MutableStateFlow<ApiException?>(null)
    val error: StateFlow<ApiException?> = _error.asStateFlow()

    private val _localError = MutableStateFlow<Int?>(null)
    val localError: StateFlow<Int?> = _localError.asStateFlow()

    init {
        if (classId != null) {
            viewModelScope.launch {
                try {
                    _form.value = ClassForm.from(api.require { classDetails(classId) })
                } catch (e: ApiException) {
                    _error.value = e
                } finally {
                    _loading.value = false
                }
            }
        }
    }

    fun update(transform: (ClassForm) -> ClassForm) {
        _form.value = transform(_form.value)
        _localError.value = null
    }

    fun save(onSaved: (String) -> Unit) {
        val f = _form.value
        f.validate()?.let { _localError.value = it; return }
        _saving.value = true
        _error.value = null
        viewModelScope.launch {
            try {
                val saved = if (classId == null) api.require { createClass(f.toRequest()) } else api.require { updateClass(classId, f.toRequest()) }
                onSaved(saved.id)
            } catch (e: ApiException) {
                _error.value = e
            } finally {
                _saving.value = false
            }
        }
    }
}
