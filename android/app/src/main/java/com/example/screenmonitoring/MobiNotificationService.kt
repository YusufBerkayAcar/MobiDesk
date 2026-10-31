package com.example.screenmonitoring

import android.app.Notification
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.pm.ApplicationInfo
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.drawable.BitmapDrawable
import android.graphics.drawable.Drawable
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
            val appName = resolveAppName(pm, pkg, extras)
            val iconBase64 = resolveAppIcon(pm, pkg, notif, extras)

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

    private fun resolveAppName(pm: android.content.pm.PackageManager, pkg: String, extras: android.os.Bundle?): String {
        // Strateji 1: extras["android.appInfo"]
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.KITKAT) {
                val ai = extras?.get("android.appInfo") as? ApplicationInfo
                if (ai != null) {
                    val label = pm.getApplicationLabel(ai)?.toString()
                    if (!label.isNullOrEmpty()) return label
                }
            }
        } catch (_: Exception) {}

        // Strateji 2: pm.getApplicationInfo
        try {
            val ai = pm.getApplicationInfo(pkg, 0)
            val label = pm.getApplicationLabel(ai)?.toString()
            if (!label.isNullOrEmpty()) return label
        } catch (_: Exception) {}

        // Strateji 3: Popüler paket adları
        val known = resolveKnownPackageName(pkg)
        if (known != pkg) return known

        // Strateji 4: Temizlenmiş paket adı (örn. com.whatsapp -> Whatsapp)
        return cleanPackageName(pkg)
    }

    private fun resolveAppIcon(pm: android.content.pm.PackageManager, pkg: String, notif: Notification, extras: android.os.Bundle?): String? {
        var drawable: Drawable? = null

        // Strateji 1: extras["android.appInfo"]
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.KITKAT) {
                val ai = extras?.get("android.appInfo") as? ApplicationInfo
                if (ai != null) {
                    drawable = ai.loadIcon(pm)
                }
            }
        } catch (_: Exception) {}

        // Strateji 2: pm.getApplicationIcon
        if (drawable == null) {
            try {
                drawable = pm.getApplicationIcon(pkg)
            } catch (_: Exception) {}
        }

        // Strateji 3: notif.getLargeIcon() (Android 6.0+)
        if (drawable == null && Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            try {
                val icon = notif.getLargeIcon()
                drawable = icon?.loadDrawable(this@MobiNotificationService)
            } catch (_: Exception) {}
        }

        // Strateji 4: extras EXTRA_LARGE_ICON
        if (drawable == null) {
            try {
                val bmp = extras?.getParcelable<Bitmap>(Notification.EXTRA_LARGE_ICON)
                if (bmp != null) {
                    drawable = BitmapDrawable(resources, bmp)
                }
            } catch (_: Exception) {}
        }

        // Strateji 5: notif.smallIcon (Android 6.0+)
        if (drawable == null && Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            try {
                val icon = notif.smallIcon
                drawable = icon?.loadDrawable(this@MobiNotificationService)
            } catch (_: Exception) {}
        }

        // Strateji 6: pm.getResourcesForApplication(pkg)
        if (drawable == null) {
            try {
                val resId = notif.icon
                if (resId != 0) {
                    val appRes = pm.getResourcesForApplication(pkg)
                    drawable = appRes.getDrawable(resId)
                }
            } catch (_: Exception) {}
        }

        if (drawable == null) return null

        return try {
            val bmp = drawableToBitmap(drawable, 48)
            val baos = ByteArrayOutputStream()
            bmp.compress(Bitmap.CompressFormat.PNG, 90, baos)
            Base64.encodeToString(baos.toByteArray(), Base64.NO_WRAP)
        } catch (e: Exception) {
            Log.w(TAG, "Error encoding icon: ${e.message}")
            null
        }
    }

    private fun drawableToBitmap(drawable: Drawable, targetSize: Int = 48): Bitmap {
        if (drawable is BitmapDrawable && drawable.bitmap != null) {
            val src = drawable.bitmap
            if (src.width == targetSize && src.height == targetSize) return src
            return Bitmap.createScaledBitmap(src, targetSize, targetSize, true)
        }
        val bmp = Bitmap.createBitmap(targetSize, targetSize, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bmp)
        drawable.setBounds(0, 0, targetSize, targetSize)
        drawable.draw(canvas)
        return bmp
    }

    private fun resolveKnownPackageName(pkg: String): String {
        return when (pkg) {
            "com.google.android.gm" -> "Gmail"
            "com.google.android.youtube" -> "YouTube"
            "com.google.android.apps.messaging" -> "Google Mesajlar"
            "com.google.android.dialer" -> "Telefon"
            "com.google.android.calendar" -> "Google Takvim"
            "com.google.android.apps.photos" -> "Google Fotoğraflar"
            "com.google.android.apps.maps" -> "Google Haritalar"
            "com.android.chrome" -> "Chrome"
            "com.android.vending" -> "Google Play"
            "com.whatsapp" -> "WhatsApp"
            "com.whatsapp.w4b" -> "WhatsApp Business"
            "com.instagram.android" -> "Instagram"
            "org.telegram.messenger" -> "Telegram"
            "com.twitter.android", "com.x.android" -> "X"
            "com.spotify.music" -> "Spotify"
            "com.discord" -> "Discord"
            "com.samsung.android.messaging" -> "Samsung Mesajlar"
            "com.samsung.android.dialer" -> "Telefon"
            "com.sec.android.app.sbrowser" -> "Samsung Internet"
            "com.sec.android.gallery3d" -> "Galeri"
            "com.sec.android.app.camera" -> "Kamera"
            "com.samsung.android.app.notes" -> "Samsung Notes"
            "com.samsung.android.app.smartcapture" -> "Samsung yakalama"
            "com.microsoft.office.outlook" -> "Outlook"
            "com.microsoft.teams" -> "Microsoft Teams"
            "com.netflix.mediaclient" -> "Netflix"
            "com.facebook.katana" -> "Facebook"
            "com.facebook.orca" -> "Messenger"
            else -> pkg
        }
    }

    private fun cleanPackageName(pkg: String): String {
        val parts = pkg.split(".")
        if (parts.isNotEmpty()) {
            val candidate = parts.last()
            if (candidate.length > 2) {
                return candidate.replaceFirstChar { if (it.isLowerCase()) it.titlecase() else it.toString() }
            }
        }
        return pkg
    }
}
