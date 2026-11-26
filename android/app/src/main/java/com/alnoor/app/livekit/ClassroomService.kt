package com.alnoor.app.livekit

import android.Manifest
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import com.alnoor.app.ALNoorApplication
import com.alnoor.app.MainActivity
import com.alnoor.app.R
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch

/**
 * Keeps the class running with the screen locked or the app in the background (PRD 20.4, FR-LIVE-13).
 * Type "microphone" always; "camera" only while the camera is on. Must be started from a visible screen.
 */
class ClassroomService : Service() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val manager get() = (application as ALNoorApplication).container.classroom

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_TOGGLE_MIC -> manager.toggleMic()
            ACTION_LEAVE -> manager.leave()
        }
        promote(manager.state.value)

        if (intent?.action == null || intent.action == ACTION_START) {
            scope.launch {
                manager.state
                    .map { Triple(it.status, it.micOn, it.cameraOn) }
                    .distinctUntilChanged()
                    .collect { (status, _, _) ->
                        if (status == ClassroomStatus.Ended || status == ClassroomStatus.Idle) {
                            ServiceCompat.stopForeground(this@ClassroomService, ServiceCompat.STOP_FOREGROUND_REMOVE)
                            stopSelf()
                        } else {
                            promote(manager.state.value)
                        }
                    }
            }
        }
        return START_NOT_STICKY // Never restart a class on our own after the process was killed.
    }

    private fun promote(state: ClassroomState) {
        var type = 0
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            type = ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE
            val cameraAllowed = ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED
            if (state.cameraOn && cameraAllowed) type = type or ServiceInfo.FOREGROUND_SERVICE_TYPE_CAMERA
        }
        ServiceCompat.startForeground(this, NOTIFICATION_ID, notification(state), type)
    }

    private fun notification(state: ClassroomState): Notification {
        val open = PendingIntent.getActivity(
            this, 0, Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        fun action(action: String, code: Int) = PendingIntent.getService(
            this, code, Intent(this, ClassroomService::class.java).setAction(action),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_notification_class)
            .setContentTitle(getString(R.string.notification_class_title, state.classTitle))
            .setContentText(getString(R.string.notification_class_text))
            .setContentIntent(open)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setCategory(NotificationCompat.CATEGORY_CALL)
            .setForegroundServiceBehavior(NotificationCompat.FOREGROUND_SERVICE_IMMEDIATE)
            .addAction(0, getString(if (state.micOn) R.string.classroom_mute else R.string.classroom_unmute), action(ACTION_TOGGLE_MIC, 1))
            .addAction(0, getString(R.string.classroom_leave), action(ACTION_LEAVE, 2))
            .build()
    }

    override fun onDestroy() {
        scope.cancel()
        super.onDestroy()
    }

    companion object {
        private const val CHANNEL_ID = "classroom"
        private const val NOTIFICATION_ID = 1001
        const val ACTION_START = "com.alnoor.app.classroom.START"
        const val ACTION_TOGGLE_MIC = "com.alnoor.app.classroom.TOGGLE_MIC"
        const val ACTION_LEAVE = "com.alnoor.app.classroom.LEAVE"

        fun createChannel(context: Context) {
            val channel = NotificationChannel(CHANNEL_ID, context.getString(R.string.notification_channel_classroom), NotificationManager.IMPORTANCE_LOW)
            context.getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
        }

        /** Call from a visible screen right after joining (Android 11+ background mic rule). */
        fun start(context: Context) {
            ContextCompat.startForegroundService(context, Intent(context, ClassroomService::class.java).setAction(ACTION_START))
        }
    }
}
