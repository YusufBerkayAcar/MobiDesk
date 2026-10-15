package com.example.screenmonitoring

import android.app.Notification
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.Canvas
import android.os.Build
import android.provider.Settings
import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification
import android.util.Base64
import android.util.Log
import java.io.ByteArrayOutputStream

class MobiNotificationService : NotificationListenerService(), MobiFlowServer.FlowEventListener {

    companion object {
        private const val TAG = "MobiNotifService"
        var isServiceConnected = false
            private set

        fun isNotificationListenerEnabled(context: Context): Boolean {
            val pkgName = context.packageName
            val flat = Settings.Secure.getString(context.contentResolver, "enabled_notification_listeners")
            if (!flat.isNullOrEmpty()) {
                val names = flat.split(":")
                for (name in names) {
                    val cn = ComponentName.unflattenFromString(name)
                    if (cn != null && cn.packageName == pkgName) {
                        return true
                    }
                }
            }
            return false
        }

        fun openNotificationSettings(context: Context) {
            try {
                val intent = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.LOLLIPOP_MR1) {
                    Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS)
                } else {
                    Intent("android.settings.ACTION_NOTIFICATION_LISTENER_SETTINGS")
                }
                intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                context.startActivity(intent)
            } catch (e: Exception) {
                Log.w(TAG, "Cannot open notification settings: ${e.message}")
            }
        }
    }

    override fun onCreate() {
        super.onCreate()
        try {
            MobiFlowServer.instance.init(applicationContext)
            MobiFlowServer.instance.start()
            MobiFlowServer.instance.addListener(this)
            Log.i(TAG, "MobiNotificationService created & MobiFlowServer started")
        } catch (e: Exception) {
            Log.w(TAG, "MobiNotificationService onCreate error: ${e.message}")
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        MobiFlowServer.instance.removeListener(this)
        isServiceConnected = false
        Log.i(TAG, "MobiNotificationService destroyed")
    }

    override fun onListenerConnected() {
        isServiceConnected = true
        Log.i(TAG, "Notification listener connected!")
        try {
            MobiFlowServer.instance.init(applicationContext)
            MobiFlowServer.instance.start()
            val active = activeNotifications
            if (active != null) {
                for (sbn in active) {
                    processNotification(sbn)
                }
            }
        } catch (e: Exception) {
            Log.w(TAG, "Error syncing active notifications: ${e.message}")
        }
    }

    override fun onListenerDisconnected() {
        isServiceConnected = false
        Log.i(TAG, "Notification listener disconnected")
    }

    override fun onNotificationPosted(sbn: StatusBarNotification?) {
        try {
            if (!MobiFlowServer.instance.isRunningNow) {
                MobiFlowServer.instance.init(applicationContext)
                MobiFlowServer.instance.start()
            }
        } catch (_: Exception) {}
        processNotification(sbn)
    }

    private fun processNotification(sbn: StatusBarNotification?) {
        if (sbn == null) return
        val pkg = sbn.packageName ?: return

        // Kendi bildirimlerimizi filtrele
        if (pkg == packageName) return

        // Sistem / devam eden arka plan bildirimlerini (Ongoing) filtrele (İsteğe bağlı, ekran kaydedici vb. spam yapmasın)
        val notif = sbn.notification ?: return
        if ((notif.flags and Notification.FLAG_ONGOING_EVENT) != 0) {
            // Sadece kritik veya medya değilse geç
            return
        }

        try {
            val extras = notif.extras
            var title = extras?.getCharSequence(Notification.EXTRA_TITLE)?.toString() ?: ""
            var text = extras?.getCharSequence(Notification.EXTRA_TEXT)?.toString() ?: ""
            if (text.isEmpty()) {
                text = extras?.getCharSequence("android.bigText")?.toString() ?: ""
            }
            if (text.isEmpty()) {
                text = extras?.getCharSequence(Notification.EXTRA_SUB_TEXT)?.toString() ?: ""
            }
            if (title.isEmpty() && text.isEmpty()) {
                val ticker = notif.tickerText?.toString()
                if (!ticker.isNullOrEmpty()) text = ticker
            }
            if (title.isEmpty() && text.isEmpty()) return
            Log.i(TAG, "Notification accepted: pkg=$pkg, title='$title', text='$text'")

            val pm = packageManager
            val appName = try {
                val ai = pm.getApplicationInfo(pkg, 0)
                pm.getApplicationLabel(ai).toString()
            } catch (_: Exception) {
                pkg
            }

            // Uygulama ikonunu 36x36 küçük PNG Base64'e dönüştür
            var iconBase64: String? = null
            try {
                val drawable = pm.getApplicationIcon(pkg)
                val bmp = Bitmap.createBitmap(36, 36, Bitmap.Config.ARGB_8888)
                val canvas = Canvas(bmp)
                drawable.setBounds(0, 0, 36, 36)
                drawable.draw(canvas)

                val baos = ByteArrayOutputStream()
                bmp.compress(Bitmap.CompressFormat.PNG, 85, baos)
                iconBase64 = Base64.encodeToString(baos.toByteArray(), Base64.NO_WRAP)
            } catch (_: Exception) {
            }

            val key = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.LOLLIPOP) {
                sbn.key ?: "${pkg}_${sbn.id}"
            } else {
                "${pkg}_${sbn.id}"
            }

            val model = NotificationModel(
                id = sbn.id,
                key = key,
                packageName = pkg,
                appName = appName,
                title = title,
                text = text,
                timestamp = sbn.postTime,
                iconBase64 = iconBase64
            )

            MobiFlowServer.instance.sendNotification(model)
        } catch (e: Exception) {
            Log.w(TAG, "onNotificationPosted error: ${e.message}")
        }
    }

    override fun onNotificationRemoved(sbn: StatusBarNotification?) {
        if (sbn == null) return
        val pkg = sbn.packageName ?: return
        if (pkg == packageName) return

        val key = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.LOLLIPOP) {
            sbn.key ?: "${pkg}_${sbn.id}"
        } else {
            "${pkg}_${sbn.id}"
        }

        MobiFlowServer.instance.sendNotificationRemove(key, pkg, sbn.id)
    }

    // --- MobiFlowServer.FlowEventListener Callbacks ---

    override fun onNotificationDismissRequested(key: String, pkg: String, id: Int) {
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.LOLLIPOP && key.isNotEmpty()) {
                cancelNotification(key)
            } else {
                cancelNotification(pkg, null, id)
            }
        } catch (e: Exception) {
            Log.w(TAG, "cancelNotification error: ${e.message}")
        }
    }

    override fun onMessageReceived(msg: FlowMessage) {}
    override fun onFileProgress(msg: FlowMessage) {}
    override fun onConnectionChanged(connected: Boolean) {}
}
