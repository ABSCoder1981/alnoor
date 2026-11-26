package com.alnoor.app.ui.students

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.lifecycle.ViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewModelScope
import com.alnoor.app.R
import com.alnoor.app.data.api.AlNoorApi
import com.alnoor.app.data.api.RegisterStudentRequest
import com.alnoor.app.data.api.ResetPasswordRequest
import com.alnoor.app.data.api.Student
import com.alnoor.app.data.api.StudentStatusRequest
import com.alnoor.app.data.api.UpdateStudentRequest
import com.alnoor.app.data.auth.ApiException
import com.alnoor.app.ui.auth.PhoneInput
import com.alnoor.app.ui.common.ConfirmDialog
import com.alnoor.app.ui.common.ErrorMessage
import com.alnoor.app.ui.common.Formats
import com.alnoor.app.ui.common.Load
import com.alnoor.app.ui.common.LoadContent
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

data class ShownPassword(val name: String, val password: String)

class StudentsViewModel(private val api: AlNoorApi) : ViewModel() {
    private val _students = MutableStateFlow<Load<List<Student>>>(Load.Loading)
    val students: StateFlow<Load<List<Student>>> = _students.asStateFlow()

    private val _error = MutableStateFlow<ApiException?>(null)
    val error: StateFlow<ApiException?> = _error.asStateFlow()

    private val _shown = MutableStateFlow<ShownPassword?>(null)

    /** A temporary password, shown once after create/reset (PRD 10.5). */
    val shown: StateFlow<ShownPassword?> = _shown.asStateFlow()

    var search: String = ""
        private set

    init {
        load()
    }

    fun load(query: String = search) {
        search = query
        viewModelScope.launch {
            _students.value = try {
                Load.Ready(api.require { students(query.trim().ifEmpty { null }) })
            } catch (e: ApiException) {
                Load.Failed(e)
            }
        }
    }

    private fun act(block: suspend () -> Unit) {
        _error.value = null
        viewModelScope.launch {
            try {
                block()
                load()
            } catch (e: ApiException) {
                _error.value = e
            }
        }
    }

    /** The dialog closes either way; a server error (e.g. phone already registered) shows on the screen. */
    fun create(name: String, phone: String, onDone: () -> Unit) = act {
        try {
            val password = Formats.temporaryPassword()
            api.call { registerStudent(RegisterStudentRequest(name.trim(), phone, password)) }
            _shown.value = ShownPassword(name.trim(), password)
        } finally {
            onDone()
        }
    }

    fun update(s: Student, name: String, phone: String, onDone: () -> Unit) = act {
        try {
            api.call { updateStudent(s.id, UpdateStudentRequest(name.trim(), phone.trim())) }
        } finally {
            onDone()
        }
    }

    fun setActive(s: Student) = act { api.call { setStudentStatus(s.id, StudentStatusRequest(!s.isActive)) } }

    fun resetPassword(s: Student) = act {
        val password = Formats.temporaryPassword()
        api.call { resetPassword(s.id, ResetPasswordRequest(password)) }
        _shown.value = ShownPassword(s.fullName, password)
    }

    fun clearShown() {
        _shown.value = null
    }
}

private sealed interface StudentDialog {
    data object Add : StudentDialog
    data class Edit(val student: Student) : StudentDialog
    data class Status(val student: Student) : StudentDialog
    data class Reset(val student: Student) : StudentDialog
}

@Composable
fun StudentsScreen(viewModel: StudentsViewModel, onBack: () -> Unit) {
    val students by viewModel.students.collectAsStateWithLifecycle()
    val error by viewModel.error.collectAsStateWithLifecycle()
    val shown by viewModel.shown.collectAsStateWithLifecycle()
    var query by remember { mutableStateOf(viewModel.search) }
    var dialog by remember { mutableStateOf<StudentDialog?>(null) }

    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        TextButton(onClick = onBack, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.back)) }
        Text(stringResource(R.string.nav_students), style = MaterialTheme.typography.headlineSmall)
        Button(onClick = { dialog = StudentDialog.Add }, modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp)) { Text(stringResource(R.string.students_add)) }

        shown?.let { s ->
            Card(Modifier.fillMaxWidth()) {
                Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text(stringResource(R.string.students_temp_title, s.name), fontWeight = FontWeight.SemiBold)
                    Text(s.password, style = MaterialTheme.typography.headlineSmall, fontFamily = FontFamily.Monospace)
                    Text(stringResource(R.string.students_temp_body))
                    Button(onClick = viewModel::clearShown, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.done)) }
                }
            }
        }
        error?.let { ErrorMessage(it) }

        OutlinedTextField(query, { query = it.take(100); viewModel.load(query) }, label = { Text(stringResource(R.string.students_search)) }, singleLine = true, modifier = Modifier.fillMaxWidth())

        LoadContent(students, onRetry = { viewModel.load() }) { list ->
            if (list.isEmpty()) Text(stringResource(R.string.students_empty))
            list.forEach { s ->
                Card(Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                        Text(s.fullName, fontWeight = FontWeight.SemiBold)
                        Text(s.phoneNumber, style = MaterialTheme.typography.bodySmall)
                        if (!s.isActive) Text(stringResource(R.string.students_deactivated), color = MaterialTheme.colorScheme.error)
                        if (s.mustChangePassword) Text(stringResource(R.string.students_temporary), color = MaterialTheme.colorScheme.secondary)
                        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            OutlinedButton(onClick = { dialog = StudentDialog.Edit(s) }, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.edit)) }
                            OutlinedButton(onClick = { dialog = StudentDialog.Reset(s) }, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.students_reset)) }
                            OutlinedButton(onClick = { dialog = StudentDialog.Status(s) }, modifier = Modifier.heightIn(min = 48.dp)) {
                                Text(stringResource(if (s.isActive) R.string.students_deactivate else R.string.students_activate))
                            }
                        }
                    }
                }
            }
        }
    }

    when (val d = dialog) {
        StudentDialog.Add -> StudentFormDialog(null, onDismiss = { dialog = null }) { name, phone -> viewModel.create(name, phone) { dialog = null } }
        is StudentDialog.Edit -> StudentFormDialog(d.student, onDismiss = { dialog = null }) { name, phone -> viewModel.update(d.student, name, phone) { dialog = null } }
        is StudentDialog.Status -> ConfirmDialog(
            title = "${stringResource(if (d.student.isActive) R.string.students_deactivate else R.string.students_activate)}: ${d.student.fullName}",
            body = if (d.student.isActive) stringResource(R.string.students_deactivate_body) else null,
            confirmLabel = stringResource(if (d.student.isActive) R.string.students_deactivate else R.string.students_activate),
            danger = d.student.isActive,
            onConfirm = { viewModel.setActive(d.student); dialog = null },
            onDismiss = { dialog = null },
        )
        is StudentDialog.Reset -> ConfirmDialog(
            title = "${stringResource(R.string.students_reset)}: ${d.student.fullName}",
            body = stringResource(R.string.students_reset_body),
            confirmLabel = stringResource(R.string.students_reset),
            onConfirm = { viewModel.resetPassword(d.student); dialog = null },
            onDismiss = { dialog = null },
        )
        null -> Unit
    }
}

/** Add (student == null) or edit a student. New students use the country-code picker; edits take a full number. */
@Composable
private fun StudentFormDialog(student: Student?, onDismiss: () -> Unit, onSave: (name: String, phone: String) -> Unit) {
    var name by remember { mutableStateOf(student?.fullName.orEmpty()) }
    var country by remember { mutableStateOf(PhoneInput.countryCodes.first()) }
    var number by remember { mutableStateOf(student?.phoneNumber.orEmpty()) }
    var error by remember { mutableStateOf<Int?>(null) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(if (student == null) R.string.students_add else R.string.edit)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(name, { name = it.take(100) }, label = { Text(stringResource(R.string.students_full_name)) }, singleLine = true)
                if (student == null) {
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        var open by remember { mutableStateOf(false) }
                        OutlinedButton(onClick = { open = !open }, modifier = Modifier.width(96.dp).heightIn(min = 56.dp)) { Text(country) }
                        androidx.compose.material3.DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
                            PhoneInput.countryCodes.forEach { code ->
                                androidx.compose.material3.DropdownMenuItem(text = { Text(code) }, onClick = { country = code; open = false })
                            }
                        }
                        OutlinedTextField(number, { number = it.take(20) }, label = { Text(stringResource(R.string.login_phone)) },
                            supportingText = { Text(stringResource(R.string.students_phone_hint)) },
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Phone), singleLine = true)
                    }
                } else {
                    OutlinedTextField(number, { number = it.take(30) }, label = { Text(stringResource(R.string.students_edit_phone)) },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Phone), singleLine = true)
                }
                error?.let { Text(stringResource(it), color = MaterialTheme.colorScheme.error) }
            }
        },
        confirmButton = {
            Button(onClick = {
                val phone = if (student == null) PhoneInput.toInternational(country, number) else number.trim()
                error = when {
                    name.trim().length < 2 -> R.string.students_error_name
                    phone.isEmpty() -> R.string.auth_error_phone_required
                    else -> null
                }
                if (error == null) onSave(name, phone)
            }) { Text(stringResource(R.string.save)) }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.cancel)) } },
    )
}
