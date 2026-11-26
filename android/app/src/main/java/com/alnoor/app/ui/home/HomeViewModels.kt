package com.alnoor.app.ui.home

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.alnoor.app.data.api.AlNoorApi
import com.alnoor.app.data.api.ClassSummary
import com.alnoor.app.data.api.Session
import com.alnoor.app.data.auth.ApiException
import com.alnoor.app.ui.common.Load
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

/** Dashboard data for both roles (PRD 11.2 screens 3–4). */
class HomeViewModel(private val api: AlNoorApi) : ViewModel() {
    private val _classes = MutableStateFlow<Load<List<ClassSummary>>>(Load.Loading)
    val classes: StateFlow<Load<List<ClassSummary>>> = _classes.asStateFlow()

    private val _starting = MutableStateFlow<String?>(null)
    val starting: StateFlow<String?> = _starting.asStateFlow()

    private val _actionError = MutableStateFlow<ApiException?>(null)
    val actionError: StateFlow<ApiException?> = _actionError.asStateFlow()

    /** [quiet] refreshes without showing the loading state (used by the 10-second polling, FR-SES-05). */
    fun load(quiet: Boolean = false) {
        if (!quiet) _classes.value = Load.Loading
        viewModelScope.launch {
            _classes.value = try {
                Load.Ready(api.require { classes() })
            } catch (e: ApiException) {
                if (quiet && _classes.value is Load.Ready) return@launch // Keep showing the last data on a blip.
                Load.Failed(e)
            }
        }
    }

    /** Teacher: start (or return to) the live session. */
    fun start(classId: String, onStarted: (Session) -> Unit) {
        _starting.value = classId
        _actionError.value = null
        viewModelScope.launch {
            try {
                onStarted(api.require { startSession(classId) })
            } catch (e: ApiException) {
                _actionError.value = e
            } finally {
                _starting.value = null
            }
        }
    }
}
