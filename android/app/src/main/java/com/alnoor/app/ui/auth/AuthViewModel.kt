package com.alnoor.app.ui.auth

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import com.alnoor.app.AppContainer
import com.alnoor.app.R
import com.alnoor.app.data.auth.ApiException
import com.alnoor.app.data.auth.AuthUser
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import java.time.Duration
import java.time.Instant

/** A message from a string resource, so all user-visible text stays translatable (PRD M4). */
/** [quantity] set means [id] is a plurals resource. */
data class UiText(val id: Int, val args: List<Any> = emptyList(), val quantity: Int? = null)

sealed interface SessionState {
    data object Loading : SessionState
    data class Offline(val message: UiText) : SessionState
    data class SignedOut(val notice: UiText? = null) : SessionState
    data class SignedIn(val user: AuthUser) : SessionState
}

data class LoginForm(
    val serverUrl: String = "",
    val countryCode: String = PhoneInput.countryCodes.first(),
    val number: String = "",
    val password: String = "",
    val submitting: Boolean = false,
    val error: UiText? = null,
)

data class PasswordForm(
    val current: String = "",
    val new: String = "",
    val repeat: String = "",
    val submitting: Boolean = false,
    val error: UiText? = null,
    val done: Boolean = false,
)

class AuthViewModel(private val container: AppContainer) : ViewModel() {
    private val auth = container.auth

    private val _session = MutableStateFlow<SessionState>(SessionState.Loading)
    val session: StateFlow<SessionState> = _session.asStateFlow()

    private val _login = MutableStateFlow(LoginForm(serverUrl = container.apiBaseUrl))
    val login: StateFlow<LoginForm> = _login.asStateFlow()

    private val _password = MutableStateFlow(PasswordForm())
    val password: StateFlow<PasswordForm> = _password.asStateFlow()

    init {
        restore()
        viewModelScope.launch {
            auth.sessionEnded.collect { _session.value = SessionState.SignedOut(UiText(R.string.auth_session_ended)) }
        }
    }

    /** Splash step (PRD 11.2 screen 1): use the stored refresh token, if any. */
    fun restore() {
        _session.value = SessionState.Loading
        viewModelScope.launch {
            _session.value = try {
                auth.restore()?.let { SessionState.SignedIn(it) } ?: SessionState.SignedOut()
            } catch (e: ApiException) {
                if (e.code == ApiException.NOT_CONFIGURED) SessionState.SignedOut() else SessionState.Offline(UiText(R.string.auth_error_network))
            }
        }
    }

    fun updateLogin(transform: (LoginForm) -> LoginForm) = _login.update { transform(it).copy(error = null) }

    fun submitLogin() {
        val form = _login.value
        val phone = PhoneInput.toInternational(form.countryCode, form.number)
        when {
            phone.isEmpty() -> return _login.update { it.copy(error = UiText(R.string.auth_error_phone_required)) }
            form.password.isEmpty() -> return _login.update { it.copy(error = UiText(R.string.auth_error_password_required)) }
        }
        container.apiBaseUrl = form.serverUrl
        _login.update { it.copy(submitting = true, error = null) }
        viewModelScope.launch {
            try {
                val user = auth.login(phone, form.password)
                _login.update { LoginForm(serverUrl = it.serverUrl, countryCode = it.countryCode, number = it.number) }
                _session.value = SessionState.SignedIn(user)
            } catch (e: ApiException) {
                _login.update { it.copy(submitting = false, password = "", error = loginError(e)) }
            }
        }
    }

    fun updatePassword(transform: (PasswordForm) -> PasswordForm) = _password.update { transform(it).copy(error = null) }

    fun submitPasswordChange() {
        val form = _password.value
        when {
            form.new.length < MIN_PASSWORD -> return _password.update { it.copy(error = UiText(R.string.password_error_too_short, listOf(MIN_PASSWORD))) }
            form.new != form.repeat -> return _password.update { it.copy(error = UiText(R.string.password_error_mismatch)) }
            form.new == form.current -> return _password.update { it.copy(error = UiText(R.string.password_error_same)) }
        }
        _password.update { it.copy(submitting = true) }
        viewModelScope.launch {
            try {
                auth.changePassword(form.current, form.new)
                _password.value = PasswordForm(done = true)
                _session.update { s -> if (s is SessionState.SignedIn) s.copy(user = s.user.copy(mustChangePassword = false)) else s }
            } catch (e: ApiException) {
                val message = when {
                    e.code == "INVALID_CREDENTIALS" -> UiText(R.string.password_error_current_wrong)
                    e.fieldErrors["newPassword"] != null -> UiText(R.string.password_error_rejected)
                    e.code == ApiException.NETWORK_ERROR -> UiText(R.string.auth_error_network)
                    else -> UiText(R.string.auth_error_generic)
                }
                _password.update { it.copy(submitting = false, error = message) }
            }
        }
    }

    fun resetPasswordForm() {
        _password.value = PasswordForm()
    }

    fun logout() {
        viewModelScope.launch {
            auth.logout()
            _password.value = PasswordForm()
            _session.value = SessionState.SignedOut()
        }
    }

    companion object {
        const val MIN_PASSWORD = 8

        /** US-01: clear errors; lockout shows how long to wait. */
        fun loginError(e: ApiException, now: Instant = Instant.now()): UiText = when (e.code) {
            "INVALID_CREDENTIALS" -> UiText(R.string.auth_error_invalid_credentials)
            "ACCOUNT_LOCKED" -> {
                val until = e.lockoutEndsAt?.let { runCatching { Instant.parse(it) }.getOrNull() }
                val minutes = until?.let { maxOf(1L, (Duration.between(now, it).toSeconds() + 59) / 60) }
                if (minutes == null) UiText(R.string.auth_error_locked) else UiText(R.plurals.auth_error_locked_minutes_plural, listOf(minutes), quantity = minutes.toInt())
            }
            "ACCOUNT_DEACTIVATED" -> UiText(R.string.auth_error_deactivated)
            "RATE_LIMITED" -> UiText(R.string.auth_error_rate_limited)
            "VALIDATION_FAILED" -> UiText(R.string.auth_error_phone_invalid)
            ApiException.NETWORK_ERROR -> UiText(R.string.auth_error_network)
            ApiException.NOT_CONFIGURED -> UiText(R.string.auth_error_not_configured)
            else -> UiText(R.string.auth_error_generic)
        }

        fun factory(container: AppContainer) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T = AuthViewModel(container) as T
        }
    }
}
