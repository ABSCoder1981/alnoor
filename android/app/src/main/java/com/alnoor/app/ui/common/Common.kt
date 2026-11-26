package com.alnoor.app.ui.common

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.alnoor.app.R
import com.alnoor.app.data.auth.ApiException
import java.security.SecureRandom
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle

/** Loading / error / ready state of a screen's data (CLAUDE.md: loading, error and empty states everywhere). */
sealed interface Load<out T> {
    data object Loading : Load<Nothing>
    data class Failed(val error: ApiException) : Load<Nothing>
    data class Ready<T>(val value: T) : Load<T>
}

/** User-facing text for an API error. Shows the server's own message only for known, user-safe codes. */
@Composable
fun errorText(e: ApiException): String = when {
    e.code == ApiException.NETWORK_ERROR -> stringResource(R.string.auth_error_network)
    e.code == "RTC_UNAVAILABLE" -> stringResource(R.string.error_rtc_unavailable)
    e.code == "FORBIDDEN" -> stringResource(R.string.error_forbidden)
    e.code == "NOT_FOUND" -> stringResource(R.string.error_not_found)
    e.fieldErrors.isNotEmpty() -> e.fieldErrors.values.first().firstOrNull() ?: stringResource(R.string.error_generic)
    e.status in 400..499 -> e.message ?: stringResource(R.string.error_generic)
    else -> stringResource(R.string.error_generic)
}

@Composable
fun ErrorMessage(e: ApiException, onRetry: (() -> Unit)? = null) {
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(errorText(e), color = MaterialTheme.colorScheme.error, modifier = Modifier.semantics { liveRegion = LiveRegionMode.Polite })
        if (onRetry != null) {
            OutlinedButton(onClick = onRetry, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.retry)) }
        }
    }
}

@Composable
fun <T> LoadContent(state: Load<T>, onRetry: () -> Unit, content: @Composable (T) -> Unit) {
    when (state) {
        Load.Loading -> Column(Modifier.fillMaxWidth().padding(24.dp), horizontalAlignment = Alignment.CenterHorizontally) {
            CircularProgressIndicator()
        }
        is Load.Failed -> ErrorMessage(state.error, onRetry)
        is Load.Ready -> content(state.value)
    }
}

@Composable
fun ConfirmDialog(title: String, body: String?, confirmLabel: String, onConfirm: () -> Unit, onDismiss: () -> Unit, danger: Boolean = false) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        text = body?.let { { Text(it) } },
        confirmButton = {
            Button(
                onClick = onConfirm,
                modifier = Modifier.heightIn(min = 48.dp),
                colors = if (danger) androidx.compose.material3.ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error) else androidx.compose.material3.ButtonDefaults.buttonColors(),
            ) { Text(confirmLabel) }
        },
        dismissButton = { TextButton(onClick = onDismiss, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.cancel)) } },
    )
}

object Formats {
    private val dateTime = DateTimeFormatter.ofLocalizedDateTime(FormatStyle.MEDIUM, FormatStyle.SHORT)

    /** UTC from the API, shown in the phone's own time zone (PRD M5). */
    fun localDateTime(isoUtc: String, zone: ZoneId = ZoneId.systemDefault()): String =
        runCatching { dateTime.format(Instant.parse(isoUtc).atZone(zone)) }.getOrDefault(isoUtc)

    private val words = listOf("noor", "quran", "sabr", "ilm", "huda", "amal", "rahma", "salam", "barakah", "iman", "taqwa", "shukr")
    private val random = SecureRandom()

    /** Readable temporary password, e.g. "noor-4821-quran" (same scheme as the web app). */
    fun temporaryPassword(): String = "${words[random.nextInt(words.size)]}-${1000 + random.nextInt(9000)}-${words[random.nextInt(words.size)]}"
}
