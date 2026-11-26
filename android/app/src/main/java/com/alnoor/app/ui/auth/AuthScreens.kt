package com.alnoor.app.ui.auth

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuAnchorType
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.alnoor.app.BuildConfig
import com.alnoor.app.R
import com.alnoor.app.AppContainer
import com.alnoor.app.ui.AppNav

/** Root of the app: splash -> login -> (forced password change) -> home (PRD 11.2 screens 1, 2, 2a). */
@Composable
fun AuthRoot(viewModel: AuthViewModel, container: AppContainer) {
    val session by viewModel.session.collectAsStateWithLifecycle()
    Scaffold { padding ->
        Box(Modifier.padding(padding).imePadding()) {
            when (val s = session) {
                SessionState.Loading -> Centered { CircularProgressIndicator() ; Text(stringResource(R.string.loading)) }
                is SessionState.Offline -> Centered {
                    ErrorText(s.message)
                    Button(onClick = viewModel::restore, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.retry)) }
                }
                is SessionState.SignedOut -> LoginScreen(viewModel, s.notice)
                is SessionState.SignedIn ->
                    if (s.user.mustChangePassword) ChangePasswordScreen(viewModel, forced = true, onDone = {})
                    else AppNav(s.user, container, viewModel)
            }
        }
    }
}

@Composable
private fun Centered(content: @Composable () -> Unit) {
    Column(
        Modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp, Alignment.CenterVertically),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) { content() }
}

@Composable
private fun text(t: UiText): String {
    val resources = LocalContext.current.resources
    return if (t.quantity != null) resources.getQuantityString(t.id, t.quantity, *t.args.toTypedArray())
    else stringResource(t.id, *t.args.toTypedArray())
}

@Composable
private fun ErrorText(t: UiText) {
    Text(
        text(t),
        color = MaterialTheme.colorScheme.error,
        modifier = Modifier.semantics { liveRegion = LiveRegionMode.Polite },
    )
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun LoginScreen(viewModel: AuthViewModel, notice: UiText?) {
    val form by viewModel.login.collectAsStateWithLifecycle()
    var showPassword by rememberSaveable { mutableStateOf(false) }
    var countryOpen by remember { mutableStateOf(false) }

    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Text(stringResource(R.string.app_name), style = MaterialTheme.typography.displaySmall, color = MaterialTheme.colorScheme.primary)
        Text(stringResource(R.string.tagline), style = MaterialTheme.typography.titleMedium)
        Text(stringResource(R.string.login_title), style = MaterialTheme.typography.headlineSmall, modifier = Modifier.padding(top = 16.dp))
        notice?.let { Text(text(it), color = MaterialTheme.colorScheme.secondary) }

        if (BuildConfig.DEBUG) {
            OutlinedTextField(
                value = form.serverUrl,
                onValueChange = { v -> viewModel.updateLogin { it.copy(serverUrl = v) } },
                label = { Text(stringResource(R.string.login_server)) },
                supportingText = { Text(stringResource(R.string.login_server_hint)) },
                singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
                modifier = Modifier.fillMaxWidth(),
            )
        }

        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            ExposedDropdownMenuBox(expanded = countryOpen, onExpandedChange = { countryOpen = it }, modifier = Modifier.width(120.dp)) {
                OutlinedTextField(
                    value = form.countryCode,
                    onValueChange = {},
                    readOnly = true,
                    label = { Text(stringResource(R.string.login_country)) },
                    trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = countryOpen) },
                    modifier = Modifier.menuAnchor(ExposedDropdownMenuAnchorType.PrimaryNotEditable),
                )
                DropdownMenu(expanded = countryOpen, onDismissRequest = { countryOpen = false }) {
                    PhoneInput.countryCodes.forEach { code ->
                        DropdownMenuItem(
                            text = { Text(code) },
                            onClick = {
                                viewModel.updateLogin { it.copy(countryCode = code) }
                                countryOpen = false
                            },
                            modifier = Modifier.heightIn(min = 48.dp),
                        )
                    }
                }
            }
            OutlinedTextField(
                value = form.number,
                onValueChange = { v -> viewModel.updateLogin { it.copy(number = v.take(20)) } },
                label = { Text(stringResource(R.string.login_phone)) },
                singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Phone, imeAction = ImeAction.Next),
                modifier = Modifier.weight(1f),
            )
        }

        OutlinedTextField(
            value = form.password,
            onValueChange = { v -> viewModel.updateLogin { it.copy(password = v.take(128)) } },
            label = { Text(stringResource(R.string.login_password)) },
            singleLine = true,
            visualTransformation = if (showPassword) VisualTransformation.None else PasswordVisualTransformation(),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = ImeAction.Done),
            keyboardActions = KeyboardActions(onDone = { viewModel.submitLogin() }),
            trailingIcon = {
                TextButton(onClick = { showPassword = !showPassword }) {
                    Text(stringResource(if (showPassword) R.string.login_hide_password else R.string.login_show_password))
                }
            },
            modifier = Modifier.fillMaxWidth(),
        )

        form.error?.let { ErrorText(it) }

        Button(
            onClick = viewModel::submitLogin,
            enabled = !form.submitting,
            modifier = Modifier.fillMaxWidth().heightIn(min = 56.dp),
        ) { Text(stringResource(if (form.submitting) R.string.login_submitting else R.string.login_submit)) }

        Text(stringResource(R.string.login_forgot), style = MaterialTheme.typography.bodyMedium)
    }
}

@Composable
internal fun ChangePasswordScreen(viewModel: AuthViewModel, forced: Boolean, onDone: () -> Unit) {
    val form by viewModel.password.collectAsStateWithLifecycle()

    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Text(
            stringResource(if (forced) R.string.password_forced_title else R.string.password_title),
            style = MaterialTheme.typography.headlineSmall,
        )
        if (forced) Text(stringResource(R.string.password_forced_body))

        if (form.done && !forced) {
            Text(stringResource(R.string.password_done), color = MaterialTheme.colorScheme.primary)
            Button(onClick = onDone, modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp)) { Text(stringResource(R.string.back)) }
            return@Column
        }

        PasswordField(form.current, R.string.password_current) { v -> viewModel.updatePassword { it.copy(current = v) } }
        PasswordField(form.new, R.string.password_new, AuthViewModel.MIN_PASSWORD) { v -> viewModel.updatePassword { it.copy(new = v) } }
        PasswordField(form.repeat, R.string.password_repeat) { v -> viewModel.updatePassword { it.copy(repeat = v) } }
        form.error?.let { ErrorText(it) }

        Button(
            onClick = viewModel::submitPasswordChange,
            enabled = !form.submitting,
            modifier = Modifier.fillMaxWidth().heightIn(min = 56.dp),
        ) { Text(stringResource(if (form.submitting) R.string.password_saving else R.string.password_save)) }

        OutlinedButton(
            onClick = if (forced) viewModel::logout else onDone,
            modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp),
        ) { Text(stringResource(if (forced) R.string.home_sign_out else R.string.back)) }
    }
}

@Composable
private fun PasswordField(value: String, label: Int, vararg args: Any, onChange: (String) -> Unit) {
    OutlinedTextField(
        value = value,
        onValueChange = { onChange(it.take(128)) },
        label = { Text(stringResource(label, *args)) },
        singleLine = true,
        visualTransformation = PasswordVisualTransformation(),
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
        modifier = Modifier.fillMaxWidth(),
    )
}
