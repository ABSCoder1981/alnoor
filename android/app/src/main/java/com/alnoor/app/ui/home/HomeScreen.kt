package com.alnoor.app.ui.home

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.repeatOnLifecycle
import com.alnoor.app.BuildConfig
import com.alnoor.app.R
import com.alnoor.app.data.api.ClassSummary
import com.alnoor.app.data.auth.AuthUser
import com.alnoor.app.ui.common.ErrorMessage
import com.alnoor.app.ui.common.Formats
import com.alnoor.app.ui.common.LoadContent
import kotlinx.coroutines.delay

/** Teacher and student dashboards (PRD 11.2 screens 3–4). */
@Composable
fun HomeScreen(
    user: AuthUser,
    viewModel: HomeViewModel,
    onOpenClass: (String) -> Unit,
    onOpenClassroom: (sessionId: String, classTitle: String) -> Unit,
    onNavigate: (String) -> Unit,
    onSignOut: () -> Unit,
) {
    val classes by viewModel.classes.collectAsStateWithLifecycle()
    val starting by viewModel.starting.collectAsStateWithLifecycle()
    val actionError by viewModel.actionError.collectAsStateWithLifecycle()

    // Refresh every 10 s while the screen is visible so "Join" appears soon after the teacher starts (FR-SES-05, US-14).
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    LaunchedEffect(lifecycle) {
        lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            viewModel.load(quiet = viewModel.classes.value !is com.alnoor.app.ui.common.Load.Loading)
            while (true) {
                delay(10_000)
                viewModel.load(quiet = true)
            }
        }
    }

    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Text(stringResource(R.string.app_name), style = MaterialTheme.typography.headlineMedium, color = MaterialTheme.colorScheme.primary)
        Text(stringResource(R.string.home_hello, user.fullName.substringBefore(' ')), style = MaterialTheme.typography.titleMedium)

        if (user.isTeacher) {
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedButton(onClick = { onNavigate("classes") }, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.nav_classes)) }
                OutlinedButton(onClick = { onNavigate("students") }, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.nav_students)) }
                OutlinedButton(onClick = { onNavigate("classes/new") }, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.nav_new_class)) }
            }
        }

        actionError?.let { ErrorMessage(it) }

        Text(stringResource(R.string.home_your_classes), style = MaterialTheme.typography.titleLarge)
        LoadContent(classes, onRetry = { viewModel.load() }) { list ->
            if (list.isEmpty()) {
                Text(stringResource(if (user.isTeacher) R.string.empty_classes_teacher else R.string.empty_classes_student))
            }
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                // Live classes first.
                list.sortedWith(compareByDescending<ClassSummary> { it.liveSessionId != null }.thenBy { it.nextOccurrences.firstOrNull() ?: "9999" })
                    .forEach { cls ->
                        ClassCard(
                            cls = cls,
                            isTeacher = user.isTeacher,
                            starting = starting == cls.id,
                            onOpen = { onOpenClass(cls.id) },
                            onJoin = { sessionId -> onOpenClassroom(sessionId, cls.title) },
                            onStart = { viewModel.start(cls.id) { s -> onOpenClassroom(s.id, cls.title) } },
                        )
                    }
            }
        }

        OutlinedButton(onClick = { onNavigate("account/password") }, modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp)) {
            Text(stringResource(R.string.home_change_password))
        }
        if (BuildConfig.DEBUG) {
            TextButton(onClick = { onNavigate("dev/livekit") }, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.home_livekit_test)) }
        }
        Button(onClick = onSignOut, modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp)) { Text(stringResource(R.string.home_sign_out)) }
    }
}

@Composable
private fun ClassCard(
    cls: ClassSummary,
    isTeacher: Boolean,
    starting: Boolean,
    onOpen: () -> Unit,
    onJoin: (String) -> Unit,
    onStart: () -> Unit,
) {
    val live = cls.liveSessionId
    Card(
        modifier = Modifier.fillMaxWidth().clickable(onClick = onOpen),
        border = if (live != null) BorderStroke(2.dp, MaterialTheme.colorScheme.error) else null,
    ) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(cls.title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
            if (live != null) {
                Text(stringResource(R.string.home_live_banner, cls.title), color = MaterialTheme.colorScheme.error, fontWeight = FontWeight.SemiBold)
            } else {
                Text(cls.nextOccurrences.firstOrNull()?.let { stringResource(R.string.home_next, Formats.localDateTime(it)) } ?: stringResource(R.string.home_no_schedule))
            }
            if (isTeacher) Text(pluralStringResource(R.plurals.students_count, cls.studentCount, cls.studentCount))

            when {
                live != null -> Button(onClick = { onJoin(live) }, modifier = Modifier.fillMaxWidth().heightIn(min = 64.dp)) {
                    Text(stringResource(if (isTeacher) R.string.home_return_to_class else R.string.home_join), style = MaterialTheme.typography.titleMedium)
                }
                isTeacher -> Button(onClick = onStart, enabled = !starting, modifier = Modifier.fillMaxWidth().heightIn(min = 56.dp)) {
                    Text(stringResource(if (starting) R.string.home_starting else R.string.home_start))
                }
                else -> Text(stringResource(R.string.home_waiting), color = MaterialTheme.colorScheme.secondary)
            }
        }
    }
}
