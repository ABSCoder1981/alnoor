package com.alnoor.app.ui

import android.net.Uri
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.heightIn
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import com.alnoor.app.AppContainer
import com.alnoor.app.BuildConfig
import com.alnoor.app.R
import com.alnoor.app.data.auth.AuthUser
import com.alnoor.app.ui.auth.AuthViewModel
import com.alnoor.app.ui.auth.ChangePasswordScreen
import com.alnoor.app.ui.classes.ClassDetailScreen
import com.alnoor.app.ui.classes.ClassDetailViewModel
import com.alnoor.app.ui.classes.ClassEditScreen
import com.alnoor.app.ui.classes.ClassEditViewModel
import com.alnoor.app.ui.classes.ClassListScreen
import com.alnoor.app.ui.classroom.ClassroomScreen
import com.alnoor.app.ui.home.HomeScreen
import com.alnoor.app.ui.home.HomeViewModel
import com.alnoor.app.ui.poc.PocScreen
import com.alnoor.app.ui.students.StudentsScreen
import com.alnoor.app.ui.students.StudentsViewModel

/** Signed-in navigation for both roles. Teacher-only screens are also enforced by the API. */
@Composable
fun AppNav(user: AuthUser, container: AppContainer, authViewModel: AuthViewModel) {
    val nav = rememberNavController()
    val api = container.api
    fun classroomRoute(sessionId: String, title: String) = "classroom/$sessionId?title=${Uri.encode(title)}"

    // Tapping the in-class notification brings the app back; return to the live class if one is running.
    val classroomState by container.classroom.state.collectAsStateWithLifecycle()
    LaunchedEffect(Unit) {
        val id = classroomState.sessionId
        if (container.classroom.isActive && id != null) nav.navigate(classroomRoute(id, classroomState.classTitle))
    }

    NavHost(navController = nav, startDestination = "home") {
        composable("home") {
            val vm: HomeViewModel = viewModel { HomeViewModel(api) }
            HomeScreen(
                user = user,
                viewModel = vm,
                onOpenClass = { nav.navigate("classes/$it") },
                onOpenClassroom = { id, title -> nav.navigate(classroomRoute(id, title)) },
                onNavigate = { nav.navigate(it) },
                onSignOut = {
                    container.classroom.leave()
                    authViewModel.logout()
                },
            )
        }
        composable("classes") {
            val vm: HomeViewModel = viewModel { HomeViewModel(api).also { it.load() } }
            val classes by vm.classes.collectAsStateWithLifecycle()
            ClassListScreen(classes, onRetry = { vm.load() }, onOpen = { nav.navigate("classes/$it") }, onNew = { nav.navigate("classes/new") }, onBack = { nav.popBackStack() })
        }
        composable("classes/new") {
            val vm: ClassEditViewModel = viewModel { ClassEditViewModel(api, null) }
            ClassEditScreen(vm, isNew = true, onSaved = { id -> nav.navigate("classes/$id") { popUpTo("classes/new") { inclusive = true } } }, onBack = { nav.popBackStack() })
        }
        composable("classes/{id}") { entry ->
            val id = entry.arguments?.getString("id").orEmpty()
            val vm: ClassDetailViewModel = viewModel(key = "class-$id") { ClassDetailViewModel(api, id, user.isTeacher) }
            LaunchedEffect(Unit) { vm.load() } // Fresh data when coming back from edit or a class.
            ClassDetailScreen(
                viewModel = vm,
                isTeacher = user.isTeacher,
                onEdit = { nav.navigate("classes/$it/edit") },
                onOpenClassroom = { sid, title -> nav.navigate(classroomRoute(sid, title)) },
                onBack = { nav.popBackStack() },
            )
        }
        composable("classes/{id}/edit") { entry ->
            val id = entry.arguments?.getString("id").orEmpty()
            val vm: ClassEditViewModel = viewModel(key = "edit-$id") { ClassEditViewModel(api, id) }
            ClassEditScreen(vm, isNew = false, onSaved = { nav.popBackStack() }, onBack = { nav.popBackStack() })
        }
        composable("students") {
            val vm: StudentsViewModel = viewModel { StudentsViewModel(api) }
            StudentsScreen(vm, onBack = { nav.popBackStack() })
        }
        composable("classroom/{sessionId}?title={title}") { entry ->
            ClassroomScreen(
                manager = container.classroom,
                sessionId = entry.arguments?.getString("sessionId").orEmpty(),
                classTitle = entry.arguments?.getString("title").orEmpty(),
                onExit = { nav.popBackStack("home", inclusive = false) },
            )
        }
        composable("account/password") {
            ChangePasswordScreen(authViewModel, forced = false, onDone = {
                authViewModel.resetPasswordForm()
                nav.popBackStack()
            })
        }
        if (BuildConfig.DEBUG) {
            composable("dev/livekit") {
                Column {
                    TextButton(onClick = { nav.popBackStack() }, modifier = Modifier.heightIn(min = 48.dp)) { Text(stringResource(R.string.back)) }
                    PocScreen()
                }
            }
        }
    }
}
