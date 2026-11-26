package com.alnoor.app

import com.alnoor.app.data.auth.ApiException
import com.alnoor.app.ui.auth.AuthViewModel
import com.alnoor.app.ui.auth.PhoneInput
import org.junit.Assert.assertEquals
import org.junit.Test
import java.time.Instant

class AuthUiLogicTest {
    @Test
    fun phoneNumbersAreJoinedWithTheCountryCode() {
        assertEquals("+919876543210", PhoneInput.toInternational("+91", "98765 43210"))
        assertEquals("+919876543210", PhoneInput.toInternational("+91", "098765-43210"))
        assertEquals("+447911123456", PhoneInput.toInternational("+44", "(07911) 123456"))
        assertEquals("", PhoneInput.toInternational("+91", "  "))
    }

    @Test
    fun lockoutMessageShowsMinutesRoundedUp() {
        val error = ApiException(423, "ACCOUNT_LOCKED", "Locked", lockoutEndsAt = "2026-11-25T10:14:10Z")

        val text = AuthViewModel.loginError(error, now = Instant.parse("2026-11-25T10:00:00Z"))

        assertEquals(R.plurals.auth_error_locked_minutes_plural, text.id)
        assertEquals(listOf<Any>(15L), text.args)
        assertEquals(15, text.quantity)
    }

    @Test
    fun wrongPasswordAndUnknownPhoneShareOneMessage() {
        assertEquals(R.string.auth_error_invalid_credentials, AuthViewModel.loginError(ApiException(401, "INVALID_CREDENTIALS", "x")).id)
    }

    @Test
    fun unexpectedErrorsNeverShowServerDetails() {
        assertEquals(R.string.auth_error_generic, AuthViewModel.loginError(ApiException(500, "INTERNAL_ERROR", "stack trace")).id)
        assertEquals(R.string.auth_error_network, AuthViewModel.loginError(ApiException(0, ApiException.NETWORK_ERROR, "x")).id)
    }
}
