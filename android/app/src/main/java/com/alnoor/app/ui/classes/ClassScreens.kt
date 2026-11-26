package com.alnoor.app.ui.classes

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringArrayResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.alnoor.app.R
import com.alnoor.app.data.api.ClassSummary
import com.alnoor.app.ui.common.ConfirmDialog
import com.alnoor.app.ui.common.ErrorMessage
import com.alnoor.app.ui.common.Formats
import com.alnoor.app.ui.common.LoadContent

/** Class details (PRD 11.2 screen 6) for both roles. */
@Composable
fun ClassDetailScreen(
    viewModel: ClassDetailViewModel,
    isTeacher: Boolean,
    onEdit: (String) -> Unit,
    onOpenClassroom: (sessionId: String, classTitle: String) -> Unit,
    onBack: () -> Unit,
) {
    val cls by viewModel.cls.collectAsStateWithLifecycle()
    val students by viewModel.students.collectAsStateWithLifecycle()
    val busy by viewModel.busy.collectAsStateWithLifecycle()
    val error by viewModel.error.collectAsStateWithLifecycle()
    val days = stringArrayResource(R.array.day_names)
    var removing by remember { mutableStateOf<Pair<String, String>?>(null) }
    var archiveOpen by remember { mutableStateOf(false) }
    var addOpen by remember { mutableStateOf(false) }

    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        TextButton(onClick = onBack, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.back)) }
        LoadContent(cls, onRetry = viewModel::load) { c ->
            Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                Text(c.title, style = MaterialTheme.typography.headlineSmall, color = MaterialTheme.colorScheme.primary)
                if (!c.isActive) Text(stringResource(R.string.class_archived), color = MaterialTheme.colorScheme.secondary)
                c.description?.let { Text(it) }

                val live = c.liveSessionId
                when {
                    live != null -> Button(onClick = { onOpenClassroom(live, c.title) }, modifier = Modifier.fillMaxWidth().heightIn(min = 64.dp)) {
                        Text(stringResource(if (isTeacher) R.string.home_return_to_class else R.string.home_join))
                    }
                    isTeacher && c.isActive -> Button(onClick = { viewModel.start { s -> onOpenClassroom(s.id, c.title) } }, enabled = !busy, modifier = Modifier.fillMaxWidth().heightIn(min = 56.dp)) {
                        Text(stringResource(if (busy) R.string.home_starting else R.string.home_start))
                    }
                    !isTeacher -> Text(stringResource(R.string.home_waiting), color = MaterialTheme.colorScheme.secondary)
                }
                error?.let { ErrorMessage(it) }

                Text(stringResource(R.string.class_schedule), style = MaterialTheme.typography.titleMedium)
                if (c.schedule.isEmpty()) Text(stringResource(R.string.class_no_schedule))
                c.schedule.forEach { s -> Text("${days[s.dayOfWeek]} ${s.startTimeLocal} (${s.durationMinutes} min)") }
                c.nextOccurrences.take(3).forEach { Text(stringResource(R.string.home_next, Formats.localDateTime(it))) }
                Text(stringResource(R.string.class_time_zone, c.timeZoneId), style = MaterialTheme.typography.bodySmall)

                if (isTeacher) {
                    HorizontalDivider()
                    val enrolled = c.students.orEmpty().filter { it.enrollmentStatus == "Active" }
                    Text(stringResource(R.string.class_students_title, enrolled.size, MAX_STUDENTS), style = MaterialTheme.typography.titleMedium)
                    if (enrolled.isEmpty()) Text(stringResource(R.string.class_no_students))
                    enrolled.forEach { s ->
                        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                            Text(s.fullName, modifier = Modifier.weight(1f))
                            TextButton(onClick = { removing = s.id to s.fullName }, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.remove)) }
                        }
                    }
                    OutlinedButton(onClick = { addOpen = true }, enabled = enrolled.size < MAX_STUDENTS && !busy, modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp)) {
                        Text(stringResource(if (enrolled.size < MAX_STUDENTS) R.string.class_add_students else R.string.class_full))
                    }
                    HorizontalDivider()
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        OutlinedButton(onClick = { onEdit(c.id) }, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.edit)) }
                        OutlinedButton(onClick = { archiveOpen = true }, modifier = Modifier.heightIn(min = 48.dp)) {
                            Text(stringResource(if (c.isActive) R.string.class_archive else R.string.class_reactivate))
                        }
                    }

                    if (addOpen) {
                        val enrolledIds = enrolled.map { it.id }.toSet()
                        AddStudentsDialog(
                            available = students.filter { it.isActive && it.id !in enrolledIds }.map { it.id to it.fullName },
                            places = MAX_STUDENTS - enrolled.size,
                            onAdd = { ids -> viewModel.enroll(ids); addOpen = false },
                            onDismiss = { addOpen = false },
                        )
                    }
                    if (archiveOpen) {
                        ConfirmDialog(
                            title = stringResource(if (c.isActive) R.string.class_archive else R.string.class_reactivate),
                            body = if (c.isActive) stringResource(R.string.class_archive_confirm) else null,
                            confirmLabel = stringResource(if (c.isActive) R.string.class_archive else R.string.class_reactivate),
                            danger = c.isActive,
                            onConfirm = { viewModel.setActive(c, !c.isActive); archiveOpen = false },
                            onDismiss = { archiveOpen = false },
                        )
                    }
                }
            }
        }
    }

    removing?.let { (id, name) ->
        ConfirmDialog(
            title = stringResource(R.string.class_remove_student_title, name),
            body = stringResource(R.string.class_remove_student_body),
            confirmLabel = stringResource(R.string.remove),
            danger = true,
            onConfirm = { viewModel.unenroll(id); removing = null },
            onDismiss = { removing = null },
        )
    }
}

@Composable
private fun AddStudentsDialog(available: List<Pair<String, String>>, places: Int, onAdd: (List<String>) -> Unit, onDismiss: () -> Unit) {
    val selected = remember { mutableStateListOf<String>() }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(R.string.class_add_students)) },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState())) {
                if (available.isEmpty()) Text(stringResource(R.string.class_no_available))
                available.forEach { (id, name) ->
                    val checked = id in selected
                    val enabled = checked || selected.size < places
                    Row(
                        Modifier.fillMaxWidth().heightIn(min = 48.dp).toggleable(
                            value = checked, enabled = enabled, role = Role.Checkbox,
                            onValueChange = { if (it) selected.add(id) else selected.remove(id) },
                        ),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Checkbox(checked = checked, onCheckedChange = null, enabled = enabled)
                        Text(name)
                    }
                }
            }
        },
        confirmButton = { Button(onClick = { onAdd(selected.toList()) }, enabled = selected.isNotEmpty()) { Text(stringResource(R.string.class_add_selected)) } },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.cancel)) } },
    )
}

/** Teacher's class list (PRD 11.2 screen 5). */
@Composable
fun ClassListScreen(classes: com.alnoor.app.ui.common.Load<List<ClassSummary>>, onRetry: () -> Unit, onOpen: (String) -> Unit, onNew: () -> Unit, onBack: () -> Unit) {
    val days = stringArrayResource(R.array.day_names)
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        TextButton(onClick = onBack, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.back)) }
        Text(stringResource(R.string.nav_classes), style = MaterialTheme.typography.headlineSmall)
        Button(onClick = onNew, modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp)) { Text(stringResource(R.string.nav_new_class)) }
        LoadContent(classes, onRetry) { list ->
            if (list.isEmpty()) Text(stringResource(R.string.empty_classes_teacher))
            list.forEach { c ->
                TextButton(onClick = { onOpen(c.id) }, modifier = Modifier.fillMaxWidth().heightIn(min = 56.dp)) {
                    Column(Modifier.fillMaxWidth()) {
                        Text(c.title, fontWeight = FontWeight.SemiBold)
                        Text(c.schedule.joinToString { "${days[it.dayOfWeek].take(3)} ${it.startTimeLocal}" }.ifEmpty { stringResource(R.string.home_no_schedule) }, style = MaterialTheme.typography.bodySmall)
                    }
                }
            }
        }
    }
}

/** Create or edit a class with its weekly schedule (PRD FR-CLS-01/02). */
@Composable
fun ClassEditScreen(viewModel: ClassEditViewModel, isNew: Boolean, onSaved: (String) -> Unit, onBack: () -> Unit) {
    val form by viewModel.form.collectAsStateWithLifecycle()
    val loading by viewModel.loading.collectAsStateWithLifecycle()
    val saving by viewModel.saving.collectAsStateWithLifecycle()
    val error by viewModel.error.collectAsStateWithLifecycle()
    val localError by viewModel.localError.collectAsStateWithLifecycle()
    val days = stringArrayResource(R.array.day_names)
    val number = KeyboardOptions(keyboardType = KeyboardType.Number)

    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        TextButton(onClick = onBack, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.back)) }
        Text(stringResource(if (isNew) R.string.class_new_title else R.string.class_edit_title), style = MaterialTheme.typography.headlineSmall)
        if (loading) return@Column

        OutlinedTextField(form.title, { v -> viewModel.update { it.copy(title = v.take(100)) } }, label = { Text(stringResource(R.string.class_title)) }, singleLine = true, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(form.description, { v -> viewModel.update { it.copy(description = v.take(500)) } }, label = { Text(stringResource(R.string.class_description)) }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(form.timeZoneId, { v -> viewModel.update { it.copy(timeZoneId = v.take(64)) } }, label = { Text(stringResource(R.string.class_tz)) }, singleLine = true, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(form.duration, { v -> viewModel.update { it.copy(duration = v.filter(Char::isDigit).take(3)) } }, label = { Text(stringResource(R.string.class_duration)) }, keyboardOptions = number, singleLine = true, modifier = Modifier.fillMaxWidth())

        Text(stringResource(R.string.class_schedule), style = MaterialTheme.typography.titleMedium)
        form.slots.forEachIndexed { i, slot ->
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                var open by remember { mutableStateOf(false) }
                Box(Modifier.weight(1.3f)) {
                    OutlinedButton(onClick = { open = true }, modifier = Modifier.fillMaxWidth().heightIn(min = 56.dp)) { Text(days[slot.dayOfWeek]) }
                    DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
                        days.forEachIndexed { d, name ->
                            DropdownMenuItem(text = { Text(name) }, onClick = {
                                viewModel.update { f -> f.copy(slots = f.slots.mapIndexed { n, s -> if (n == i) s.copy(dayOfWeek = d) else s }) }
                                open = false
                            })
                        }
                    }
                }
                OutlinedTextField(slot.time, { v -> viewModel.update { f -> f.copy(slots = f.slots.mapIndexed { n, s -> if (n == i) s.copy(time = v.take(5)) else s }) } },
                    label = { Text(stringResource(R.string.class_slot_time)) }, singleLine = true, modifier = Modifier.weight(1f))
                OutlinedTextField(slot.minutes, { v -> viewModel.update { f -> f.copy(slots = f.slots.mapIndexed { n, s -> if (n == i) s.copy(minutes = v.filter(Char::isDigit).take(3)) else s }) } },
                    label = { Text(stringResource(R.string.class_slot_minutes)) }, keyboardOptions = number, singleLine = true, modifier = Modifier.weight(0.8f))
                TextButton(onClick = { viewModel.update { f -> f.copy(slots = f.slots.filterIndexed { n, _ -> n != i }) } }) { Text(stringResource(R.string.remove)) }
            }
        }
        if (form.slots.size < 14) {
            OutlinedButton(onClick = { viewModel.update { it.copy(slots = it.slots + SlotForm(minutes = it.duration.ifEmpty { "60" })) } }, modifier = Modifier.heightIn(min = 48.dp)) {
                Text(stringResource(R.string.class_add_time))
            }
        }

        localError?.let { Text(stringResource(it), color = MaterialTheme.colorScheme.error) }
        error?.let { ErrorMessage(it) }
        Button(onClick = { viewModel.save(onSaved) }, enabled = !saving, modifier = Modifier.fillMaxWidth().heightIn(min = 56.dp)) {
            Text(stringResource(if (saving) R.string.saving else R.string.save))
        }
    }
}
