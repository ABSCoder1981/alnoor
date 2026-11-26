package com.alnoor.app.ui.auth

/** Country codes on the login screen. The server validates the full number (E.164). */
object PhoneInput {
    val countryCodes = listOf("+91", "+92", "+880", "+971", "+966", "+44", "+1")

    /**
     * Joins a country code and the typed number: keeps digits only and drops a leading 0 (trunk prefix,
     * e.g. 09876543210 in India). Returns "" when there are no digits.
     */
    fun toInternational(countryCode: String, nationalNumber: String): String {
        val digits = nationalNumber.filter(Char::isDigit).trimStart('0')
        return if (digits.isEmpty()) "" else countryCode + digits
    }
}
