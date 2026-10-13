package com.example.screenmonitoring

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.net.Uri
import android.os.Handler
import android.os.Looper
import android.text.InputType
import android.util.Base64
import android.util.TypedValue
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.webkit.MimeTypeMap
import android.widget.*
import androidx.core.content.FileProvider
import java.io.File
import java.text.SimpleDateFormat
import java.util.*

class MobiHubHandleView(context: Context) : View(context) {
    private val density = context.resources.displayMetrics.density.coerceAtLeast(1.0f)
    private fun dp(v: Float) = (v * density).toInt()

    private val bgPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.FILL
        color = Color.rgb(15, 23, 42) // #0F172A %100 opak
    }
    private val strokePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        color = Color.rgb(59, 130, 246) // #3B82F6
        strokeWidth = dp(1.5f).toFloat().coerceAtLeast(1.5f)
    }
    private val iconPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.rgb(251, 191, 36) // #FBBF24 Amber sarı şimşek
        textSize = 14f * density
        typeface = Typeface.DEFAULT_BOLD
    }
    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.WHITE
        textSize = 13.5f * density
        typeface = Typeface.DEFAULT_BOLD
    }
    private val badgeBgPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.FILL
        color = Color.rgb(239, 68, 68) // #EF4444
    }
    private val badgeTextPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.WHITE
        textSize = 10f * density
        typeface = Typeface.DEFAULT_BOLD
        textAlign = Paint.Align.CENTER
    }

    private val rectF = RectF()
    private val iconText = "⚡"
    private val labelText = "Hub"
    var badgeCount = 0
        set(value) {
            field = value
            requestLayout()
            invalidate()
        }

    init {
        // Donanım/yazılım katmanında kusursuz piksel çizimi
        isClickable = true
        isFocusable = true
    }

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val h = dp(44f).coerceAtLeast(44)
        val padX = dp(14f)
        val gap = dp(5f)
        val iconW = iconPaint.measureText(iconText)
        val labelW = textPaint.measureText(labelText)
        var totalW = padX * 2 + iconW + gap + labelW
        if (badgeCount > 0) {
            val badgeStr = if (badgeCount > 99) "99+" else badgeCount.toString()
            val badgeTextW = badgeTextPaint.measureText(badgeStr)
            val badgeH = dp(18f).toFloat()
            val badgeW = Math.max(badgeTextW + dp(8f), badgeH)
            totalW += dp(6f) + badgeW
        }
        setMeasuredDimension(totalW.toInt(), h)
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val w = width.toFloat()
        val h = height.toFloat()
        if (w <= 0f || h <= 0f) return

        val sw = strokePaint.strokeWidth
        val r = h / 2f
        rectF.set(sw / 2f, sw / 2f, w - sw / 2f, h - sw / 2f)

        // 1. Kusursuz Simetrik Kapsül Gövdesi ve Kenarlığı
        canvas.drawRoundRect(rectF, r, r, bgPaint)
        canvas.drawRoundRect(rectF, r, r, strokePaint)

        // 2. İkon ve Metin Matematiksel Olarak Tam Dikey Ortalı
        val padX = dp(14f).toFloat()
        val gap = dp(5f).toFloat()
        val iconW = iconPaint.measureText(iconText)

        val fontMetrics = textPaint.fontMetrics
        val textCenterY = (h - (fontMetrics.descent + fontMetrics.ascent)) / 2f

        val iconMetrics = iconPaint.fontMetrics
        val iconCenterY = (h - (iconMetrics.descent + iconMetrics.ascent)) / 2f

        canvas.drawText(iconText, padX, iconCenterY, iconPaint)
        canvas.drawText(labelText, padX + iconW + gap, textCenterY, textPaint)

        // 3. Bildirim Rozeti (Varsa)
        if (badgeCount > 0) {
            val labelW = textPaint.measureText(labelText)
            val badgeStartX = padX + iconW + gap + labelW + dp(6f)
            val badgeStr = if (badgeCount > 99) "99+" else badgeCount.toString()
            val badgeTextW = badgeTextPaint.measureText(badgeStr)
            val badgeH = dp(18f).toFloat()
            val badgeW = Math.max(badgeTextW + dp(8f), badgeH)
            val badgeTop = (h - badgeH) / 2f
            rectF.set(badgeStartX, badgeTop, badgeStartX + badgeW, badgeTop + badgeH)
            canvas.drawRoundRect(rectF, badgeH / 2f, badgeH / 2f, badgeBgPaint)

            val badgeMetrics = badgeTextPaint.fontMetrics
            val badgeTextY = (badgeTop + badgeH / 2f) - (badgeMetrics.descent + badgeMetrics.ascent) / 2f
            canvas.drawText(badgeStr, badgeStartX + badgeW / 2f, badgeTextY, badgeTextPaint)
        }
    }
}

class MobiHubOverlay(
    private val activity: Activity,
    private val rootLayout: FrameLayout
) : MobiFlowServer.FlowEventListener {

    private val handler = Handler(Looper.getMainLooper())
    private val timeFormat = SimpleDateFormat("HH:mm", Locale.getDefault())
    private val displayedMessageIds = Collections.synchronizedSet(HashSet<String>())

    // Views
    private lateinit var btnToggleHandle: MobiHubHandleView
    private lateinit var drawerLayout: LinearLayout

    private lateinit var tabFlowBtn: TextView
    private lateinit var tabNotifBtn: TextView
    private lateinit var flowContainer: LinearLayout
    private lateinit var notifContainer: LinearLayout

    private lateinit var flowScroll: ScrollView
    private lateinit var flowMessagesList: LinearLayout
    private lateinit var editMsgInput: EditText

    private lateinit var notifScroll: ScrollView
    private lateinit var notifList: LinearLayout
    private lateinit var txtConnectionStatus: TextView
    private lateinit var viewStatusDot: View

    private var isDrawerOpen = false
    private var activeTab = 0 // 0: Flow, 1: Notifications
    private val notifications = ArrayList<NotificationModel>()

    init {
        buildUI()
        MobiFlowServer.instance.addListener(this)
    }

    private fun dp(value: Float): Int {
        return TypedValue.applyDimension(
            TypedValue.COMPLEX_UNIT_DIP,
            value,
            activity.resources.displayMetrics
        ).toInt()
    }

    private fun buildUI() {
        // 1. Kenar Açma/Kapama Butonu (Özel Çizim Kapsül Handle)
        btnToggleHandle = MobiHubHandleView(activity).apply {
            setOnClickListener { toggleDrawer() }
        }

        val handleParams = FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT,
            ViewGroup.LayoutParams.WRAP_CONTENT
        ).apply {
            gravity = Gravity.RIGHT or Gravity.CENTER_VERTICAL
            rightMargin = dp(14f)
            marginEnd = dp(14f)
        }
        rootLayout.clipChildren = false
        rootLayout.clipToPadding = false
        rootLayout.addView(btnToggleHandle, handleParams)
        btnToggleHandle.bringToFront()

        // 2. Çekmece Paneli (Sliding Drawer)
        val screenW = activity.resources.displayMetrics.widthPixels
        val drawerW = Math.min((screenW * 0.48f).toInt(), dp(400f))

        drawerLayout = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            val drawerBg = GradientDrawable().apply {
                setColor(Color.argb(248, 15, 23, 42)) // #0F172A %97 opak
                setStroke(dp(1f), Color.argb(80, 148, 163, 184))
                cornerRadii = floatArrayOf(
                    dp(16f).toFloat(), dp(16f).toFloat(),
                    0f, 0f,
                    0f, 0f,
                    dp(16f).toFloat(), dp(16f).toFloat()
                )
            }
            background = drawerBg
            visibility = View.GONE

            // Çekmeceye tıklanıldığında arkadaki SurfaceView'a dokunma gitmesin
            setOnTouchListener { _, _ -> true }
        }

        buildDrawerHeader()
        buildTabs()
        buildFlowContainer()
        buildNotifContainer()

        val drawerParams = FrameLayout.LayoutParams(
            drawerW,
            ViewGroup.LayoutParams.MATCH_PARENT
        ).apply {
            gravity = Gravity.END
        }
        rootLayout.addView(drawerLayout, drawerParams)
    }

    private fun buildDrawerHeader() {
        val header = LinearLayout(activity).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(16f), dp(14f), dp(16f), dp(10f))

            val titleBox = LinearLayout(activity).apply {
                orientation = LinearLayout.VERTICAL
                layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)

                val txtTitle = TextView(activity).apply {
                    text = "MobiDesk Hub"
                    textSize = 15f
                    typeface = Typeface.DEFAULT_BOLD
                    setTextColor(Color.WHITE)
                }

                val statusRow = LinearLayout(activity).apply {
                    orientation = LinearLayout.HORIZONTAL
                    gravity = Gravity.CENTER_VERTICAL
                    setPadding(0, dp(2f), 0, 0)

                    viewStatusDot = View(activity).apply {
                        val dotBg = GradientDrawable().apply {
                            shape = GradientDrawable.OVAL
                            setColor(if (MobiFlowServer.instance.isConnected()) Color.parseColor("#10B981") else Color.parseColor("#94A3B8"))
                        }
                        background = dotBg
                        layoutParams = LinearLayout.LayoutParams(dp(7f), dp(7f))
                    }

                    txtConnectionStatus = TextView(activity).apply {
                        text = if (MobiFlowServer.instance.isConnected()) " PC Bağlandı" else " PC Bekleniyor"
                        textSize = 10f
                        setTextColor(Color.parseColor("#94A3B8"))
                    }

                    addView(viewStatusDot)
                    addView(txtConnectionStatus)
                }

                addView(txtTitle)
                addView(statusRow)
            }

            val btnClose = TextView(activity).apply {
                text = "✕"
                textSize = 18f
                setTextColor(Color.parseColor("#94A3B8"))
                setPadding(dp(10f), dp(6f), dp(10f), dp(6f))
                setOnClickListener { closeDrawer() }
            }

            addView(titleBox)
            addView(btnClose)
        }
        drawerLayout.addView(header)
    }

    private fun buildTabs() {
        val tabsRow = LinearLayout(activity).apply {
            orientation = LinearLayout.HORIZONTAL
            setPadding(dp(16f), dp(4f), dp(16f), dp(8f))

            tabFlowBtn = TextView(activity).apply {
                text = "💬 MobiFlow"
                textSize = 12f
                typeface = Typeface.DEFAULT_BOLD
                gravity = Gravity.CENTER
                setPadding(dp(12f), dp(7f), dp(12f), dp(7f))
                layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                    marginEnd = dp(6f)
                }
                setOnClickListener { selectTab(0) }
            }

            tabNotifBtn = TextView(activity).apply {
                text = "🔔 Bildirimler"
                textSize = 12f
                typeface = Typeface.DEFAULT_BOLD
                gravity = Gravity.CENTER
                setPadding(dp(12f), dp(7f), dp(12f), dp(7f))
                layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                setOnClickListener { selectTab(1) }
            }

            addView(tabFlowBtn)
            addView(tabNotifBtn)
        }
        drawerLayout.addView(tabsRow)
        updateTabStyles()
    }

    private fun selectTab(tab: Int) {
        activeTab = tab
        updateTabStyles()
        if (tab == 0) {
            flowContainer.visibility = View.VISIBLE
            notifContainer.visibility = View.GONE
        } else {
            flowContainer.visibility = View.GONE
            notifContainer.visibility = View.VISIBLE
            refreshNotificationsList()
        }
    }

    private fun updateTabStyles() {
        val activeBg = GradientDrawable().apply {
            setColor(Color.parseColor("#2563EB"))
            cornerRadius = dp(8f).toFloat()
        }
        val inactiveBg = GradientDrawable().apply {
            setColor(Color.argb(120, 30, 41, 59))
            cornerRadius = dp(8f).toFloat()
        }

        tabFlowBtn.background = if (activeTab == 0) activeBg else inactiveBg
        tabFlowBtn.setTextColor(if (activeTab == 0) Color.WHITE else Color.parseColor("#94A3B8"))

        tabNotifBtn.background = if (activeTab == 1) activeBg else inactiveBg
        tabNotifBtn.setTextColor(if (activeTab == 1) Color.WHITE else Color.parseColor("#94A3B8"))
    }

    private fun buildFlowContainer() {
        flowContainer = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f)
            visibility = View.VISIBLE

            // Mesajlar Kaydırma Alanı
            flowScroll = ScrollView(activity).apply {
                layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f)
                isFillViewport = true

                flowMessagesList = LinearLayout(activity).apply {
                    orientation = LinearLayout.VERTICAL
                    setPadding(dp(16f), dp(8f), dp(16f), dp(8f))
                }
                addView(flowMessagesList)
            }

            // Alt Giriş Çubuğu (Input + File Picker) - Tam Genişlik (100%)
            val bottomBar = LinearLayout(activity).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                layoutParams = LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.WRAP_CONTENT
                )
                setPadding(dp(10f), dp(6f), dp(10f), dp(6f))
                val barBg = GradientDrawable().apply {
                    setColor(Color.argb(220, 30, 41, 59))
                    setStroke(dp(1f), Color.argb(90, 148, 163, 184))
                    cornerRadius = dp(12f).toFloat()
                }
                background = barBg

                // Dosya Gönder Butonu (📎)
                val btnFile = TextView(activity).apply {
                    text = "📎"
                    textSize = 17f
                    setPadding(dp(8f), dp(6f), dp(8f), dp(6f))
                    setOnClickListener {
                        openFilePicker()
                    }
                }

                // Mesaj Yazma Alanı - Yatayda Kalan Tüm Alanı Kaplar
                editMsgInput = EditText(activity).apply {
                    hint = "Bir mesaj veya link yazın..."
                    setHintTextColor(Color.parseColor("#64748B"))
                    setTextColor(Color.WHITE)
                    textSize = 13f
                    inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_CAP_SENTENCES
                    background = null
                    setPadding(dp(8f), dp(6f), dp(8f), dp(6f))
                    layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                }

                // Gönder Butonu (➤)
                val btnSend = TextView(activity).apply {
                    text = "➤"
                    textSize = 17f
                    setTextColor(Color.parseColor("#3B82F6"))
                    setPadding(dp(8f), dp(6f), dp(8f), dp(6f))
                    setOnClickListener {
                        sendCurrentText()
                    }
                }

                addView(btnFile)
                addView(editMsgInput)
                addView(btnSend)
            }

            val btmWrap = LinearLayout(activity).apply {
                orientation = LinearLayout.VERTICAL
                layoutParams = LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.WRAP_CONTENT
                )
                setPadding(dp(12f), dp(4f), dp(12f), dp(12f))
                addView(bottomBar)
            }

            addView(flowScroll)
            addView(btmWrap)
        }
        drawerLayout.addView(flowContainer)
    }

    private fun buildNotifContainer() {
        notifContainer = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f)
            visibility = View.GONE

            notifScroll = ScrollView(activity).apply {
                layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f)
                isFillViewport = true

                notifList = LinearLayout(activity).apply {
                    orientation = LinearLayout.VERTICAL
                    setPadding(dp(16f), dp(8f), dp(16f), dp(8f))
                }
                addView(notifList)
            }

            addView(notifScroll)
        }
        drawerLayout.addView(notifContainer)
    }

    private fun sendCurrentText() {
        val text = editMsgInput.text.toString().trim()
        if (text.isEmpty()) return
        editMsgInput.setText("")

        MobiFlowServer.instance.sendText(text)
    }

    private fun openFilePicker() {
        try {
            val intent = Intent(Intent.ACTION_GET_CONTENT).apply {
                type = "*/*"
                addCategory(Intent.CATEGORY_OPENABLE)
            }
            activity.startActivityForResult(
                Intent.createChooser(intent, "MobiDesk Flow - Dosya Seç"),
                1001
            )
        } catch (e: Exception) {
            Toast.makeText(activity, "Dosya seçici açılamadı: ${e.message}", Toast.LENGTH_SHORT).show()
        }
    }

    fun handleFilePickedUri(uri: Uri) {
        Thread {
            try {
                val contentResolver = activity.contentResolver
                var fileName = "dosya_${System.currentTimeMillis()}"

                val cursor = contentResolver.query(uri, null, null, null, null)
                cursor?.use {
                    if (it.moveToFirst()) {
                        val nameIndex = it.getColumnIndex(android.provider.OpenableColumns.DISPLAY_NAME)
                        if (nameIndex != -1) {
                            fileName = it.getString(nameIndex) ?: fileName
                        }
                    }
                }

                // Geçici dosyaya kopyala ve gönder
                val tempFile = File(activity.cacheDir, fileName)
                contentResolver.openInputStream(uri)?.use { input ->
                    tempFile.outputStream().use { output ->
                        input.copyTo(output)
                    }
                }

                MobiFlowServer.instance.sendFile(tempFile)
            } catch (e: Exception) {
                handler.post {
                    Toast.makeText(activity, "Dosya gönderilemedi: ${e.message}", Toast.LENGTH_SHORT).show()
                }
            }
        }.start()
    }

    fun addMessageBubble(msg: FlowMessage) {
        if (!displayedMessageIds.add(msg.id)) {
            return
        }
        handler.post {
            val isMe = (msg.sender == "mobile")
            val row = LinearLayout(activity).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = if (isMe) Gravity.END else Gravity.START
                setPadding(0, dp(4f), 0, dp(4f))

                val bubble = LinearLayout(activity).apply {
                    orientation = LinearLayout.VERTICAL
                    setPadding(dp(12f), dp(8f), dp(12f), dp(8f))

                    val bubbleBg = GradientDrawable().apply {
                        if (isMe) {
                            setColor(Color.parseColor("#1D4ED8")) // #1D4ED8 Blue
                            cornerRadii = floatArrayOf(
                                dp(14f).toFloat(), dp(14f).toFloat(),
                                dp(14f).toFloat(), dp(14f).toFloat(),
                                0f, 0f,
                                dp(14f).toFloat(), dp(14f).toFloat()
                            )
                        } else {
                            setColor(Color.argb(220, 30, 41, 59)) // #1E293B Card
                            cornerRadii = floatArrayOf(
                                dp(14f).toFloat(), dp(14f).toFloat(),
                                dp(14f).toFloat(), dp(14f).toFloat(),
                                dp(14f).toFloat(), dp(14f).toFloat(),
                                0f, 0f
                            )
                        }
                    }
                    background = bubbleBg

                    if (msg.isFile) {
                        // Dosya kartı
                        val txtName = TextView(activity).apply {
                            text = "📁 " + (msg.fileName ?: "Dosya")
                            typeface = Typeface.DEFAULT_BOLD
                            textSize = 12f
                            setTextColor(Color.WHITE)
                        }
                        val txtSize = TextView(activity).apply {
                            tag = "file_status_${msg.id}"
                            val sizeKb = msg.fileSize / 1024
                            val sizeStr = if (sizeKb > 1024) String.format(Locale.US, "%.1f MB", sizeKb / 1024.0) else "$sizeKb KB"
                            text = if (msg.progress < 100) "Yükleniyor: %${msg.progress} • $sizeStr" else "Tamamlandı • $sizeStr"
                            textSize = 10f
                            setTextColor(Color.parseColor("#94A3B8"))
                        }
                        addView(txtName)
                        addView(txtSize)

                        val btnOpen = TextView(activity).apply {
                            tag = "file_open_${msg.id}"
                            text = "📂 Dosyayı Aç"
                            textSize = 11f
                            typeface = Typeface.DEFAULT_BOLD
                            setTextColor(Color.parseColor("#38BDF8"))
                            setPadding(0, dp(4f), 0, 0)
                            visibility = if (!msg.filePath.isNullOrEmpty() && msg.progress == 100) View.VISIBLE else View.GONE
                            setOnClickListener {
                                msg.filePath?.let { openDownloadedFile(it) }
                            }
                        }
                        addView(btnOpen)
                    } else {
                        // Düz metin / Link
                        val txtBody = TextView(activity).apply {
                            text = msg.text
                            textSize = 12f
                            setTextColor(Color.WHITE)
                            autoLinkMask = android.text.util.Linkify.WEB_URLS
                        }
                        addView(txtBody)
                    }

                    val txtTime = TextView(activity).apply {
                        text = timeFormat.format(Date(msg.timestamp))
                        textSize = 9f
                        setTextColor(Color.argb(160, 203, 213, 225))
                        gravity = Gravity.END
                        setPadding(0, dp(2f), 0, 0)
                    }
                    addView(txtTime)
                }

                val bubbleParams = LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT,
                    ViewGroup.LayoutParams.WRAP_CONTENT
                ).apply {
                    val maxW = (drawerLayout.width * 0.75f).toInt().coerceAtLeast(dp(220f))
                    width = ViewGroup.LayoutParams.WRAP_CONTENT
                }
                addView(bubble, bubbleParams)
            }

            flowMessagesList.addView(row)
            flowScroll.post { flowScroll.fullScroll(ScrollView.FOCUS_DOWN) }
        }
    }

    private fun openDownloadedFile(path: String) {
        try {
            val file = File(path)
            if (!file.exists()) {
                Toast.makeText(activity, "Dosya bulunamadı", Toast.LENGTH_SHORT).show()
                return
            }

            val contentUri: Uri = if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.N) {
                try {
                    FileProvider.getUriForFile(
                        activity,
                        "${activity.packageName}.fileprovider",
                        file
                    )
                } catch (e: Exception) {
                    Uri.fromFile(file)
                }
            } else {
                Uri.fromFile(file)
            }

            val extension = MimeTypeMap.getFileExtensionFromUrl(Uri.fromFile(file).toString())
            val mimeType = if (!extension.isNullOrEmpty()) {
                MimeTypeMap.getSingleton().getMimeTypeFromExtension(extension.lowercase(Locale.ROOT)) ?: "*/*"
            } else {
                "*/*"
            }

            val intent = Intent(Intent.ACTION_VIEW).apply {
                setDataAndType(contentUri, mimeType)
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }

            val chooser = Intent.createChooser(intent, "Dosyayı Aç: ${file.name}").apply {
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }

            val resInfoList = activity.packageManager.queryIntentActivities(
                intent,
                android.content.pm.PackageManager.MATCH_DEFAULT_ONLY
            )
            for (resolveInfo in resInfoList) {
                val pkg = resolveInfo.activityInfo.packageName
                activity.grantUriPermission(pkg, contentUri, Intent.FLAG_GRANT_READ_URI_PERMISSION)
            }

            activity.startActivity(chooser)
        } catch (e: Exception) {
            Toast.makeText(activity, "Dosya açılamadı: ${e.message}", Toast.LENGTH_SHORT).show()
        }
    }

    private fun refreshNotificationsList() {
        notifList.removeAllViews()

        // 1. İzin Kontrolü
        val isEnabled = MobiNotificationService.isNotificationListenerEnabled(activity)
        if (!isEnabled) {
            val permCard = LinearLayout(activity).apply {
                orientation = LinearLayout.VERTICAL
                setPadding(dp(14f), dp(14f), dp(14f), dp(14f))
                val pBg = GradientDrawable().apply {
                    setColor(Color.argb(180, 239, 68, 68)) // Kırmızımsı arka plan
                    cornerRadius = dp(12f).toFloat()
                }
                background = pBg

                val txtWarnTitle = TextView(activity).apply {
                    text = "⚠️ Bildirim Erişimi Gerekli"
                    typeface = Typeface.DEFAULT_BOLD
                    textSize = 13f
                    setTextColor(Color.WHITE)
                }
                val txtWarnSub = TextView(activity).apply {
                    text = "Mobil bildirimlerin PC'ye yansıtılabilmesi için cihaz ayarlarından MobiDesk'e izin vermelisiniz."
                    textSize = 11f
                    setTextColor(Color.argb(220, 255, 255, 255))
                    setPadding(0, dp(4f), 0, dp(10f))
                }
                val btnGrant = Button(activity).apply {
                    text = "Ayarları Aç ve İzin Ver"
                    textSize = 11f
                    setTextColor(Color.BLACK)
                    setBackgroundColor(Color.WHITE)
                    setOnClickListener {
                        MobiNotificationService.openNotificationSettings(activity)
                    }
                }

                addView(txtWarnTitle)
                addView(txtWarnSub)
                addView(btnGrant)
            }
            notifList.addView(permCard)
            return
        }

        val listToDisplay = if (notifications.isNotEmpty()) notifications else MobiFlowServer.instance.activeNotifications
        if (listToDisplay.isEmpty()) {
            val emptyTxt = TextView(activity).apply {
                text = "🔔 Henüz yeni bir bildirim yok\nCihazınıza gelen bildirimler burada listelenecek ve PC'ye anlık olarak iletilecektir."
                textSize = 12f
                setTextColor(Color.parseColor("#94A3B8"))
                gravity = Gravity.CENTER
                setPadding(dp(20f), dp(40f), dp(20f), dp(40f))
            }
            notifList.addView(emptyTxt)
            return
        }

        // Bildirimleri listele
        for (item in listToDisplay) {
            val card = LinearLayout(activity).apply {
                orientation = LinearLayout.HORIZONTAL
                setPadding(dp(12f), dp(10f), dp(12f), dp(10f))
                val cBg = GradientDrawable().apply {
                    setColor(Color.argb(200, 30, 41, 59))
                    cornerRadius = dp(10f).toFloat()
                    setStroke(dp(1f), Color.argb(40, 148, 163, 184))
                }
                background = cBg

                // İkon
                val iconView = ImageView(activity).apply {
                    layoutParams = LinearLayout.LayoutParams(dp(32f), dp(32f)).apply {
                        gravity = Gravity.TOP
                        marginEnd = dp(10f)
                    }
                    if (!item.iconBase64.isNullOrEmpty()) {
                        try {
                            val bytes = Base64.decode(item.iconBase64, Base64.DEFAULT)
                            val bmp = BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
                            setImageBitmap(bmp)
                        } catch (_: Exception) {
                            setImageResource(android.R.drawable.ic_dialog_info)
                        }
                    } else {
                        setImageResource(android.R.drawable.ic_dialog_info)
                    }
                }

                // Bilgi sütunu
                val infoBox = LinearLayout(activity).apply {
                    orientation = LinearLayout.VERTICAL
                    layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)

                    val topRow = LinearLayout(activity).apply {
                        orientation = LinearLayout.HORIZONTAL
                        val txtApp = TextView(activity).apply {
                            text = item.appName
                            textSize = 11f
                            typeface = Typeface.DEFAULT_BOLD
                            setTextColor(Color.parseColor("#60A5FA"))
                            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                        }
                        val txtTime = TextView(activity).apply {
                            text = timeFormat.format(Date(item.timestamp))
                            textSize = 9f
                            setTextColor(Color.parseColor("#94A3B8"))
                        }
                        addView(txtApp)
                        addView(txtTime)
                    }

                    val txtTitle = TextView(activity).apply {
                        text = item.title
                        textSize = 12f
                        typeface = Typeface.DEFAULT_BOLD
                        setTextColor(Color.WHITE)
                    }

                    val txtBody = TextView(activity).apply {
                        text = item.text
                        textSize = 11f
                        setTextColor(Color.parseColor("#CBD5E1"))
                    }

                    addView(topRow)
                    if (item.title.isNotEmpty()) addView(txtTitle)
                    if (item.text.isNotEmpty()) addView(txtBody)
                }

                addView(iconView)
                addView(infoBox)
            }

            val cardWrap = LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT
            ).apply {
                bottomMargin = dp(8f)
            }
            notifList.addView(card, cardWrap)
        }
    }

    fun toggleDrawer() {
        if (isDrawerOpen) closeDrawer() else openDrawer()
    }

    fun openDrawer() {
        isDrawerOpen = true
        drawerLayout.visibility = View.VISIBLE
        btnToggleHandle.visibility = View.GONE
        btnToggleHandle.badgeCount = 0

        if (activeTab == 0) {
            // Flow mesajlarını tazele
            if (flowMessagesList.childCount == 0) {
                for (m in MobiFlowServer.instance.messageHistory) {
                    addMessageBubble(m)
                }
            }
        } else {
            refreshNotificationsList()
        }
    }

    fun closeDrawer() {
        isDrawerOpen = false
        drawerLayout.visibility = View.GONE
        btnToggleHandle.visibility = View.VISIBLE
    }

    fun handleBackPressed(): Boolean {
        if (isDrawerOpen) {
            closeDrawer()
            return true
        }
        return false
    }

    // --- MobiFlowServer.FlowEventListener Callbacks ---

    override fun onMessageReceived(msg: FlowMessage) {
        addMessageBubble(msg)
        if (!isDrawerOpen) {
            handler.post {
                btnToggleHandle.badgeCount++
            }
        }
    }

    override fun onFileProgress(msg: FlowMessage) {
        handler.post {
            val statusView = flowMessagesList.findViewWithTag<TextView>("file_status_${msg.id}")
            val openView = flowMessagesList.findViewWithTag<TextView>("file_open_${msg.id}")
            if (statusView != null) {
                val sizeKb = msg.fileSize / 1024
                val sizeStr = if (sizeKb > 1024) String.format(Locale.US, "%.1f MB", sizeKb / 1024.0) else "$sizeKb KB"
                statusView.text = if (msg.progress < 100) "Yükleniyor: %${msg.progress} • $sizeStr" else "Tamamlandı • $sizeStr"
            }
            if (openView != null && msg.progress == 100 && !msg.filePath.isNullOrEmpty()) {
                openView.visibility = View.VISIBLE
            }
        }
    }

    override fun onConnectionChanged(connected: Boolean) {
        handler.post {
            viewStatusDot.background = GradientDrawable().apply {
                shape = GradientDrawable.OVAL
                setColor(if (connected) Color.parseColor("#10B981") else Color.parseColor("#94A3B8"))
            }
            txtConnectionStatus.text = if (connected) " PC Bağlandı" else " PC Bekleniyor"
        }
    }

    override fun onNotificationDismissRequested(key: String, pkg: String, id: Int) {
        // Handled by MobiNotificationService
    }

    override fun onNotificationPosted(notif: NotificationModel) {
        handler.post {
            // Var olanı güncelle veya ekle
            notifications.removeAll { it.key == notif.key }
            notifications.add(0, notif)
            if (activeTab == 1 && isDrawerOpen) {
                refreshNotificationsList()
            } else if (!isDrawerOpen) {
                btnToggleHandle.badgeCount++
            }
        }
    }

    override fun onNotificationRemoved(key: String) {
        handler.post {
            notifications.removeAll { it.key == key }
            if (activeTab == 1 && isDrawerOpen) {
                refreshNotificationsList()
            }
        }
    }
}
