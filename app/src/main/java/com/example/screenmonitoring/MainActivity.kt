package com.example.screenmonitoring

import android.app.Activity
import android.graphics.*
import android.os.Build
import android.os.Bundle
import android.view.*
import java.io.DataInputStream
import java.io.OutputStream
import java.net.ServerSocket
import java.net.Socket
import java.util.concurrent.atomic.AtomicBoolean

private class FrameData(var sequence: Long, var buffer: ByteArray, var length: Int)
private class RenderItem(val sequence: Long, val bitmap: Bitmap)

class MainActivity : Activity(), SurfaceHolder.Callback {

    private lateinit var surfaceView: SurfaceView
    private var serverThread: Thread? = null
    private val isRunning = AtomicBoolean(false)
    private var serverSocket: ServerSocket? = null

    private var outputStream: OutputStream? = null
    private var activeSocket: Socket? = null
    private val socketLock = Any()

    private var serverWorker: NetworkServerWorker? = null
    private var surfaceWidth = 1280
    private var surfaceHeight = 800

    private var lastFpsTime = System.currentTimeMillis()
    private var frameCount = 0
    private var currentFps = 0
    @Volatile private var showFpsCounter = true
    private var lastTapTime = 0L
    private var mobiHubOverlay: MobiHubOverlay? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // Ekranın kapanmasını engelle
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        // MobiFlow sunucusunu başlat
        MobiFlowServer.instance.init(this)
        MobiFlowServer.instance.start()

        val rootLayout = android.widget.FrameLayout(this)
        surfaceView = SurfaceView(this)
        surfaceView.holder.setFormat(PixelFormat.RGBA_8888)
        surfaceView.holder.addCallback(this)

        rootLayout.addView(
            surfaceView,
            android.widget.FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT
            )
        )

        // Modern Hub (Flow & Bildirimler Çekmecesi) katmanını ekle
        mobiHubOverlay = MobiHubOverlay(this, rootLayout)
        setContentView(rootLayout)

        setupTouchEvents()
    }

    override fun onBackPressed() {
        if (mobiHubOverlay?.handleBackPressed() == true) {
            return
        }
        // Geri tuşuna basıldığında uygulamayı kapatmak yerine arka plana al
        moveTaskToBack(true)
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: android.content.Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode == 1001 && resultCode == Activity.RESULT_OK) {
            data?.data?.let { uri ->
                mobiHubOverlay?.handleFilePickedUri(uri)
            }
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        MobiFlowServer.instance.stop()
    }

    override fun onResume() {
        super.onResume()
        setFullscreen()
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) {
            setFullscreen()
        }
    }

    private fun setFullscreen() {
        window.decorView.systemUiVisibility = (
            View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                or View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                or View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                or View.SYSTEM_UI_FLAG_FULLSCREEN
                or View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
        )
    }

    private fun setupTouchEvents() {
        surfaceView.setOnTouchListener { _, event ->
            val dp = resources.displayMetrics.density.coerceAtLeast(1.0f)
            if (event.actionMasked == MotionEvent.ACTION_DOWN) {
                // Sol üst köşe çift dokunma alanı (DPI duyarlı)
                if (event.x < 140f * dp && event.y < 80f * dp) {
                    val now = System.currentTimeMillis()
                    if (now - lastTapTime < 400) {
                        showFpsCounter = !showFpsCounter
                        lastTapTime = 0L
                        sendFpsStatePacket(showFpsCounter)
                        return@setOnTouchListener true
                    } else {
                        lastTapTime = now
                    }
                }
            }

            val w = surfaceWidth.toFloat()
            val h = surfaceHeight.toFloat()
            if (w > 0 && h > 0) {
                val normX = (event.x / w).coerceIn(0f, 1f)
                val normY = (event.y / h).coerceIn(0f, 1f)

                val action = when (event.actionMasked) {
                    MotionEvent.ACTION_DOWN -> "DOWN"
                    MotionEvent.ACTION_MOVE -> "MOVE"
                    MotionEvent.ACTION_UP -> "UP"
                    else -> null
                }

                if (action != null) {
                    sendTouchPacket(action, normX, normY)
                }
            }
            true
        }
    }

    private fun sendFpsStatePacket(enabled: Boolean) {
        Thread {
            try {
                synchronized(socketLock) {
                    val out = outputStream
                    if (out != null) {
                        val state = if (enabled) 1 else 0
                        val msg = "FPS_STATE $state\n"
                        out.write(msg.toByteArray(Charsets.US_ASCII))
                        out.flush()
                        android.util.Log.i("TabletStream", "Sent FPS_STATE $state to PC")
                    }
                }
            } catch (_: Exception) {
            }
        }.start()
    }

    private fun sendTouchPacket(action: String, x: Float, y: Float) {
        Thread {
            try {
                synchronized(socketLock) {
                    val out = outputStream
                    if (out != null) {
                        val msg = "TOUCH $action $x $y\n"
                        out.write(msg.toByteArray(Charsets.US_ASCII))
                        out.flush()
                    }
                }
            } catch (_: Exception) {
            }
        }.start()
    }

    override fun surfaceCreated(holder: SurfaceHolder) {
        isRunning.set(true)
        val worker = NetworkServerWorker(holder)
        serverWorker = worker
        serverThread = Thread(worker).apply { start() }
    }

    override fun surfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {
        surfaceWidth = width
        surfaceHeight = height
    }

    override fun surfaceDestroyed(holder: SurfaceHolder) {
        isRunning.set(false)
        serverWorker?.stopStandby()
        serverWorker = null
        synchronized(socketLock) {
            outputStream = null
            try { activeSocket?.close() } catch (_: Exception) {}
            activeSocket = null
        }
        try {
            serverSocket?.close()
        } catch (_: Exception) {
        }
        serverThread?.interrupt()
        serverThread = null
    }

    private inner class NetworkServerWorker(private val holder: SurfaceHolder) : Runnable {
        private val paint = Paint(Paint.FILTER_BITMAP_FLAG or Paint.DITHER_FLAG)
        private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = Color.GREEN
            textSize = 28f
            typeface = Typeface.DEFAULT_BOLD
            setShadowLayer(4f, 2f, 2f, Color.BLACK)
        }
        private val dstRect = Rect()
        private var fpsText = "0 FPS"

        // --- Modern Standby Screen Renderer State & Paints ---
        private val isStandbyActive = AtomicBoolean(false)
        private var standbyThread: Thread? = null

        private var cachedWifiIp: String = ""
        private var lastWifiIpCheckTime: Long = 0L

        private val sbBgPaint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.DITHER_FLAG)
        private val sbGridDotPaint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.DITHER_FLAG).apply {
            color = Color.argb(12, 148, 163, 184)
            style = Paint.Style.FILL
        }
        private val sbGlowPaint = Paint(Paint.ANTI_ALIAS_FLAG)
        private val sbWavePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
        }
        private val sbCardBgPaint = Paint(Paint.ANTI_ALIAS_FLAG)
        private val sbCardBorderPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
        }
        private val sbBadgeBgPaint = Paint(Paint.ANTI_ALIAS_FLAG)
        private val sbBadgeBorderPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
        }
        private val sbTextBold = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Typeface.DEFAULT_BOLD
        }
        private val sbTextRegular = Paint(Paint.ANTI_ALIAS_FLAG)
        private val sbVectorPaint = Paint(Paint.ANTI_ALIAS_FLAG)
        private val sbRectF = RectF()
        private val sbRectF2 = RectF()
        private var cachedLogoBitmap: Bitmap? = null
        private var cachedLogoSize: Int = 0

        private fun getOrCreateLogoBitmap(targetPx: Int, radiusPx: Float): Bitmap? {
            if (cachedLogoBitmap != null && cachedLogoSize == targetPx && !cachedLogoBitmap!!.isRecycled) {
                return cachedLogoBitmap
            }
            try {
                val rawBmp = BitmapFactory.decodeResource(resources, R.drawable.app_logo) ?: return null
                val scaledBmp = Bitmap.createScaledBitmap(rawBmp, targetPx, targetPx, true)
                if (scaledBmp != rawBmp) {
                    rawBmp.recycle()
                }

                val rounded = Bitmap.createBitmap(targetPx, targetPx, Bitmap.Config.ARGB_8888)
                val c = Canvas(rounded)
                val p = Paint(Paint.ANTI_ALIAS_FLAG)
                val r = RectF(0f, 0f, targetPx.toFloat(), targetPx.toFloat())
                c.drawRoundRect(r, radiusPx, radiusPx, p)
                p.xfermode = PorterDuffXfermode(PorterDuff.Mode.SRC_IN)
                c.drawBitmap(scaledBmp, 0f, 0f, p)
                scaledBmp.recycle()

                cachedLogoBitmap = rounded
                cachedLogoSize = targetPx
                return rounded
            } catch (_: Exception) {
                return null
            }
        }

        fun stopStandby() {
            stopStandbyAnimation()
        }

        private fun startStandbyAnimation(errorMsg: String? = null) {
            if (isStandbyActive.getAndSet(true)) return
            standbyThread = Thread {
                var animPhase = 0f
                while (isStandbyActive.get() && isRunning.get()) {
                    val canvas = try {
                        lockCanvasCompat()
                    } catch (_: Exception) {
                        null
                    }
                    if (canvas != null) {
                        try {
                            drawModernStandbyScreen(canvas, animPhase, errorMsg)
                        } catch (_: Exception) {
                        } finally {
                            try {
                                holder.unlockCanvasAndPost(canvas)
                            } catch (_: Exception) {
                            }
                        }
                    }
                    animPhase += 0.05f
                    if (animPhase > 1000f) animPhase = 0f
                    try {
                        Thread.sleep(40) // ~25 FPS akıcı nabız animasyonu
                    } catch (_: InterruptedException) {
                        break
                    }
                }
            }.apply {
                name = "StandbyAnimationThread"
                priority = Thread.MIN_PRIORITY + 1
                start()
            }
        }

        private fun stopStandbyAnimation() {
            isStandbyActive.set(false)
            val thread = standbyThread
            if (thread != null) {
                thread.interrupt()
                try {
                    thread.join(250)
                } catch (_: Exception) {
                }
                standbyThread = null
            }
        }

        private fun updateWifiIpAddress() {
            val now = System.currentTimeMillis()
            if (now - lastWifiIpCheckTime < 4000 && cachedWifiIp.isNotEmpty()) return
            lastWifiIpCheckTime = now
            try {
                val interfaces = java.net.NetworkInterface.getNetworkInterfaces()
                if (interfaces != null) {
                    while (interfaces.hasMoreElements()) {
                        val iface = interfaces.nextElement()
                        if (iface.isLoopback || !iface.isUp) continue
                        val addrs = iface.inetAddresses
                        while (addrs.hasMoreElements()) {
                            val addr = addrs.nextElement()
                            if (!addr.isLoopbackAddress && addr is java.net.Inet4Address) {
                                val host = addr.hostAddress
                                if (host != null && !host.startsWith("127.")) {
                                    cachedWifiIp = host
                                    return
                                }
                            }
                        }
                    }
                }
            } catch (_: Exception) {}
            cachedWifiIp = ""
        }

        private fun lockCanvasCompat(): Canvas? {
            return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                try {
                    holder.lockHardwareCanvas()
                } catch (_: Exception) {
                    holder.lockCanvas()
                }
            } else {
                holder.lockCanvas()
            }
        }

        override fun run() {
            try {
                val server = ServerSocket()
                server.reuseAddress = true
                server.bind(java.net.InetSocketAddress(55555))
                serverSocket = server
            } catch (e: Exception) {
                startStandbyAnimation("Port 55555 kullanılamıyor: ${e.message}")
                return
            }

            while (isRunning.get() && serverSocket?.isClosed == false) {
                var socket: Socket? = null
                try {
                    startStandbyAnimation()

                    socket = serverSocket!!.accept()
                    stopStandbyAnimation()

                    socket.tcpNoDelay = true
                    socket.receiveBufferSize = 64 * 1024

                    synchronized(socketLock) {
                        activeSocket = socket
                        outputStream = socket.getOutputStream()
                    }

                    // Cihazın gerçek çözünürlüğünü ve DPI bilgisini PC'ye bildir
                    try {
                        val metrics = resources.displayMetrics
                        val w = if (surfaceWidth > 0) surfaceWidth else metrics.widthPixels
                        val h = if (surfaceHeight > 0) surfaceHeight else metrics.heightPixels
                        val dpi = metrics.densityDpi
                        val model = android.os.Build.MODEL.replace(" ", "_")
                        val infoMsg = "DEVICE_INFO $w $h $dpi $model\n"
                        outputStream?.write(infoMsg.toByteArray(Charsets.US_ASCII))
                        outputStream?.flush()
                    } catch (_: Exception) {
                    }

                    val inputStream = DataInputStream(socket.getInputStream())

                    // Aşama A & D: Çok Çekirdekli Paralel Decode ve Triple/Quad Buffering Donanım Hattı
                    val NUM_DECODERS = 2
                    val rawFrameQueue = java.util.concurrent.ArrayBlockingQueue<FrameData>(2)
                    val freePool = java.util.concurrent.ArrayBlockingQueue<FrameData>(6).apply {
                        for (i in 0..5) offer(FrameData(0L, ByteArray(512 * 1024), 0))
                    }

                    val targetW = if (surfaceWidth > 0) surfaceWidth else 1280
                    val targetH = if (surfaceHeight > 0) surfaceHeight else 800

                    val bitmapList = Array(4) { Bitmap.createBitmap(targetW, targetH, Bitmap.Config.RGB_565) }
                    val freeBitmapQueue = java.util.concurrent.ArrayBlockingQueue<Bitmap>(4).apply {
                        for (b in bitmapList) offer(b)
                    }

                    // Resequencer: Çok çekirdekli çözücülerin kareleri sırayla (monotonik) ekrana vermesini sağlar
                    val resequenceLock = java.lang.Object()
                    var nextExpectedSeq = 1L
                    val pendingFrames = java.util.TreeMap<Long, Bitmap>()

                    val optionsMap = java.util.IdentityHashMap<Bitmap, BitmapFactory.Options>().apply {
                        for (b in bitmapList) {
                            put(b, BitmapFactory.Options().apply {
                                inPreferredConfig = Bitmap.Config.RGB_565
                                inDither = false
                                inMutable = true
                                inBitmap = b
                                inSampleSize = 1
                            })
                        }
                    }

                    val decoderThreads = mutableListOf<Thread>()

                    // Telemetri Sayaçları (Hassas Darboğaz Tespiti)
                    val rxCounter = java.util.concurrent.atomic.AtomicInteger(0)
                    val decodeCounter = java.util.concurrent.atomic.AtomicInteger(0)
                    val inBitmapFailCounter = java.util.concurrent.atomic.AtomicInteger(0)
                    val lastInBitmapEx = java.util.concurrent.atomic.AtomicReference<String>("none")
                    val dropOrderCounter = java.util.concurrent.atomic.AtomicInteger(0)
                    val dropRawCounter = java.util.concurrent.atomic.AtomicInteger(0)
                    val dropReadyCounter = java.util.concurrent.atomic.AtomicInteger(0)
                    val totalDecodeMs = java.util.concurrent.atomic.AtomicLong(0)
                    val totalRenderMs = java.util.concurrent.atomic.AtomicLong(0)
                    val totalLockMs = java.util.concurrent.atomic.AtomicLong(0)

                    // 1. Çok Çekirdekli Paralel Decode İş Parçacıkları (Intel Atom 4 Çekirdeğinden 2'si Çözmeye Ayrılır)
                    for (tIdx in 0 until NUM_DECODERS) {
                        val dt = Thread {
                            while (isRunning.get() && socket.isConnected && !socket.isClosed) {
                                val frame = try {
                                    rawFrameQueue.poll(50, java.util.concurrent.TimeUnit.MILLISECONDS)
                                } catch (_: Exception) {
                                    null
                                } ?: continue

                                try {
                                    val freeBmp = try {
                                        freeBitmapQueue.poll(50, java.util.concurrent.TimeUnit.MILLISECONDS)
                                    } catch (_: Exception) {
                                        null
                                    } ?: continue

                                    val opts = optionsMap[freeBmp] ?: BitmapFactory.Options().apply {
                                        inPreferredConfig = Bitmap.Config.RGB_565
                                        inDither = false
                                        inMutable = true
                                        inBitmap = freeBmp
                                        inSampleSize = 1
                                    }

                                    val t0 = System.currentTimeMillis()
                                    var decodedBmp: Bitmap? = null
                                    var inBitmapError: String? = null
                                    try {
                                        decodedBmp = BitmapFactory.decodeByteArray(frame.buffer, 0, frame.length, opts)
                                    } catch (ex: Throwable) {
                                        inBitmapError = ex.javaClass.simpleName + ": " + ex.message
                                    }

                                    if (decodedBmp == null) {
                                        inBitmapFailCounter.incrementAndGet()
                                        if (inBitmapError != null) {
                                            lastInBitmapEx.set(inBitmapError)
                                        }
                                        opts.inBitmap = null
                                        try {
                                            decodedBmp = BitmapFactory.decodeByteArray(frame.buffer, 0, frame.length, opts)
                                        } catch (_: Throwable) {}
                                        opts.inBitmap = freeBmp
                                    }
                                    val dMs = System.currentTimeMillis() - t0
                                    totalDecodeMs.addAndGet(dMs)
                                    decodeCounter.incrementAndGet()

                                    if (decodedBmp != null) {
                                        synchronized(resequenceLock) {
                                            pendingFrames[frame.sequence] = decodedBmp
                                            resequenceLock.notifyAll()
                                        }
                                    } else {
                                        freeBitmapQueue.offer(freeBmp)
                                    }
                                } finally {
                                    freePool.offer(frame)
                                }
                            }
                        }
                        dt.name = "PipelinedDecoder-$tIdx"
                        dt.priority = Thread.NORM_PRIORITY + 1
                        dt.start()
                        decoderThreads.add(dt)
                    }

                    // 2. Render & VSYNC Çizim İş Parçacığı (GPU / Ekran)
                    val renderThread = Thread {
                        while (isRunning.get() && socket.isConnected && !socket.isClosed) {
                            var readyBmp: Bitmap? = null

                            synchronized(resequenceLock) {
                                val deadline = System.currentTimeMillis() + 15
                                while (isRunning.get() && !pendingFrames.containsKey(nextExpectedSeq)) {
                                    val remaining = deadline - System.currentTimeMillis()
                                    if (remaining <= 0) break
                                    try {
                                        resequenceLock.wait(remaining)
                                    } catch (_: InterruptedException) {
                                        break
                                    }
                                }

                                if (pendingFrames.containsKey(nextExpectedSeq)) {
                                    readyBmp = pendingFrames.remove(nextExpectedSeq)
                                    nextExpectedSeq++

                                    // SIFIR GECİKME (ZERO-LAG): Eğer arkada birikmiş başka kareler varsa, eski olanları atla ve doğrudan en güncel kareye yetiş!
                                    if (pendingFrames.size > 1) {
                                        val latestKey = pendingFrames.lastKey()
                                        val it = pendingFrames.entries.iterator()
                                        while (it.hasNext()) {
                                            val entry = it.next()
                                            if (entry.key != latestKey) {
                                                freeBitmapQueue.offer(entry.value)
                                                dropOrderCounter.incrementAndGet()
                                                it.remove()
                                            }
                                        }
                                        val latestBmp = pendingFrames.remove(latestKey)
                                        if (latestBmp != null) {
                                            freeBitmapQueue.offer(readyBmp)
                                            readyBmp = latestBmp
                                            nextExpectedSeq = latestKey + 1
                                        }
                                    }
                                } else if (pendingFrames.isNotEmpty()) {
                                    // 15ms zaman aşımı: Beklenen kare gecikti/düştü, en güncel (en son) kareye doğrudan atla!
                                    val lastEntry = pendingFrames.lastEntry()
                                    if (lastEntry != null) {
                                        val latestKey = lastEntry.key
                                        val latestBmp = lastEntry.value
                                        for ((seq, bmp) in pendingFrames) {
                                            if (seq != latestKey) {
                                                freeBitmapQueue.offer(bmp)
                                            }
                                        }
                                        pendingFrames.clear()
                                        readyBmp = latestBmp
                                        nextExpectedSeq = latestKey + 1
                                        dropOrderCounter.incrementAndGet()
                                    }
                                }
                            }

                            if (readyBmp == null) {
                                continue
                            }

                            try {
                                val tLock0 = System.currentTimeMillis()
                                val canvas = lockCanvasCompat()
                                val lockMs = System.currentTimeMillis() - tLock0
                                totalLockMs.addAndGet(lockMs)

                                if (canvas != null) {
                                    try {
                                        val tDraw0 = System.currentTimeMillis()
                                        val sw = surfaceWidth
                                        val sh = surfaceHeight
                                        if (readyBmp.width == sw && readyBmp.height == sh) {
                                            canvas.drawBitmap(readyBmp, 0f, 0f, null)
                                        } else {
                                            dstRect.set(0, 0, sw, sh)
                                            canvas.drawBitmap(readyBmp, null, dstRect, paint)
                                        }
                                        val renderMs = System.currentTimeMillis() - tDraw0
                                        totalRenderMs.addAndGet(renderMs)

                                        // FPS Hesabı ve Kapsamlı Telemetri Günlüğü
                                        frameCount++
                                        val now = System.currentTimeMillis()
                                        if (now - lastFpsTime >= 1000) {
                                            currentFps = frameCount
                                            frameCount = 0
                                            val rx = rxCounter.getAndSet(0)
                                            val dec = decodeCounter.getAndSet(0)
                                            val dOrder = dropOrderCounter.getAndSet(0)
                                            val dRaw = dropRawCounter.getAndSet(0)
                                            val dReady = dropReadyCounter.getAndSet(0)
                                            val avgDec = if (dec > 0) totalDecodeMs.getAndSet(0) / dec else 0
                                            val avgRnd = if (currentFps > 0) totalRenderMs.getAndSet(0) / currentFps else 0
                                            val avgLck = if (currentFps > 0) totalLockMs.getAndSet(0) / currentFps else 0
                                            val inBmpFails = inBitmapFailCounter.getAndSet(0)
                                            val lastEx = lastInBitmapEx.get()
                                            lastFpsTime = now
                                            fpsText = "$currentFps FPS"
                                            android.util.Log.i(
                                                "TabletStream",
                                                "STATS -> RenderFPS: $currentFps | Rx: $rx | Decoded: $dec | InBitmapFail: $inBmpFails ($lastEx) | Drops(Order:$dOrder, Raw:$dRaw, Ready:$dReady) | AvgDecode: ${avgDec}ms | LockCanvas: ${avgLck}ms | Draw: ${avgRnd}ms | Bmp: ${readyBmp.width}x${readyBmp.height} | Surf: ${sw}x${sh}"
                                            )
                                        }

                                        if (showFpsCounter) {
                                            canvas.drawText(fpsText, 24f, 38f, textPaint)
                                        }
                                    } finally {
                                        holder.unlockCanvasAndPost(canvas)
                                    }
                                }
                            } finally {
                                freeBitmapQueue.offer(readyBmp)
                            }
                        }
                    }
                    renderThread.name = "PipelinedRenderThread"
                    renderThread.priority = Thread.MAX_PRIORITY
                    renderThread.start()

                    // 3. Ağ Alıcı İş Parçacığı (Producer)
                    var globalSeq = 0L
                    try {
                        while (isRunning.get() && socket.isConnected && !socket.isClosed) {
                            val frameSize = inputStream.readInt()
                            if (frameSize == -101) {
                                val showFps = inputStream.readInt()
                                showFpsCounter = (showFps != 0)
                                android.util.Log.i("TabletStream", "showFpsCounter updated from PC: $showFpsCounter")
                                continue
                            }
                            if (frameSize <= 0 || frameSize > 10 * 1024 * 1024) {
                                break
                            }

                            var frame = freePool.poll() ?: FrameData(0L, ByteArray(frameSize + 64 * 1024), 0)
                            if (frame.buffer.size < frameSize) {
                                frame.buffer = ByteArray(frameSize + 128 * 1024)
                            }
                            frame.length = frameSize
                            frame.sequence = ++globalSeq

                            inputStream.readFully(frame.buffer, 0, frameSize)
                            rxCounter.incrementAndGet()

                            // Çözücü kuyruğuna ver; kuyruk doluysa eski kareyi atıp tazeyi koy (0ms gecikme)
                            if (!rawFrameQueue.offer(frame)) {
                                dropRawCounter.incrementAndGet()
                                val stale = rawFrameQueue.poll()
                                if (stale != null) freePool.offer(stale)
                                rawFrameQueue.offer(frame)
                            }
                        }
                    } finally {
                        for (dt in decoderThreads) {
                            try { dt.interrupt() } catch (_: Exception) {}
                        }
                        try { renderThread.interrupt() } catch (_: Exception) {}
                        synchronized(resequenceLock) {
                            resequenceLock.notifyAll()
                        }
                        for (dt in decoderThreads) {
                            try { dt.join(200) } catch (_: Exception) {}
                        }
                        try { renderThread.join(200) } catch (_: Exception) {}
                        synchronized(resequenceLock) {
                            for (b in pendingFrames.values) {
                                freeBitmapQueue.offer(b)
                            }
                            pendingFrames.clear()
                        }
                    }
                } catch (e: Exception) {
                    // Bağlantı kapandı veya hata oluştu
                } finally {
                    synchronized(socketLock) {
                        outputStream = null
                        activeSocket = null
                    }
                    try {
                        socket?.close()
                    } catch (_: Exception) {
                    }
                }
            }
        }

        private fun drawModernStandbyScreen(canvas: Canvas, phase: Float, errorMsg: String?) {
            val w = if (canvas.width > 0) canvas.width.toFloat() else surfaceWidth.toFloat()
            val h = if (canvas.height > 0) canvas.height.toFloat() else surfaceHeight.toFloat()
            if (w <= 0f || h <= 0f) return

            val metrics = resources.displayMetrics
            val dp = metrics.density.coerceAtLeast(1.0f)
            val sp = (dp * (h / (480f * dp)).coerceIn(0.95f, 1.45f)).coerceAtLeast(1.35f)

            val padX = 28f * dp
            val padY = 16f * dp
            val logoSize = 44f * dp
            val topBarBottom = padY + logoSize

            // 1. Dinamik İçerik Boyutlu Kablolu Bağlantı ve USB Hata Ayıklama Kartı (Ön Hesaplama)
            // A) Metin genişliklerini dinamik olarak ölç
            sbTextRegular.textSize = 11.2f * sp
            val t1W = sbTextRegular.measureText("• Cihazı USB kablosu ile bilgisayara bağlayın")
            val t2W = sbTextRegular.measureText("• MobiDesk PC uygulaması ADB tünelini otomatik kurar")
            val t3W = sbTextRegular.measureText("• 60 FPS donanım hızlandırmalı sıfır gecikme ve şarj desteği")
            val maxSolTextW = maxOf(t1W, t2W, t3W)

            sbTextRegular.textSize = 11.2f * sp
            val b1W = sbTextRegular.measureText("1. Cihaz Ayarları > Geliştirici Seçenekleri menüsüne gidin")
            sbTextBold.textSize = 11.2f * sp
            val b2W = sbTextBold.measureText("2. \"USB Hata Ayıklama\" (USB Debugging) seçeneğini AÇIN")
            sbTextRegular.textSize = 9.8f * sp
            val b3W = sbTextRegular.measureText("(Menü yoksa: Ayarlar > Cihaz Hakkında > 'Yapım Numarası'na 7 kez dokunun)")
            val maxSagTextW = maxOf(b1W, b2W, b3W)

            // B) Kolon ve Kart Genişliği Hesaplaması (Yazıların uzunluğuna tam uyumlu)
            val colGap = 16f * dp
            val cardPadX = 18f * dp
            val neededColW = maxOf(maxSolTextW + 18f * dp, maxSagTextW + 30f * dp)

            val maxAllowedCardW = w - padX * 2f
            val idealCardW = neededColW * 2f + colGap + cardPadX * 2f
            val cardW = idealCardW.coerceIn(280f * dp, maxAllowedCardW)
            val cardLeft = (w - cardW) / 2f
            val cardRadius = 12f * dp

            val colW = (cardW - cardPadX * 2f - colGap) / 2f
            val col1L = cardLeft + cardPadX
            val col2L = col1L + colW + colGap

            // C) Kart Yüksekliği ve Konumlandırma (Metin satırlarıyla tam orantılı, alt bilgiye yapışmayan temiz boşluk)
            val neededContentH = 44f * dp + 86f * sp
            val cardH = neededContentH.coerceIn(145f * dp, h * 0.42f)

            // Alt bilgi çubuğuna yapışmaması için temiz alt boşluk (38dp margin)
            val cardBottom = h - 38f * dp
            val cardTop = cardBottom - cardH

            // 2. Merkez Hub Dikey Konumlandırma ("Bilgisayarınız Bekleniyor" ekrana tam ortalı)
            val centerX = w / 2f
            val availableMidH = (cardTop - topBarBottom).coerceAtLeast(120f)
            val hubCenterY = topBarBottom + availableMidH * 0.46f
            val radarY = hubCenterY - (22f * dp + 12f * sp)

            // 3. Zemin: Derin uzay stüdyo tonu (#090D16) & Pürüzsüz Degrade
            canvas.drawColor(Color.rgb(9, 13, 22))

            // A) Yumuşak, kadifemsi merkez aydınlatması (Bantlaşma ve kesilmeyi önleyen çok kademeli yumuşak geçiş)
            try {
                val glowRadius = (minOf(w, h) * 0.58f).coerceAtLeast(180f * dp)
                val radialShader = RadialGradient(
                    centerX, radarY, glowRadius,
                    intArrayOf(
                        Color.argb(55, 14, 165, 233),
                        Color.argb(32, 56, 189, 248),
                        Color.argb(16, 99, 102, 241),
                        Color.argb(6, 30, 41, 59),
                        Color.TRANSPARENT
                    ),
                    floatArrayOf(0f, 0.28f, 0.58f, 0.82f, 1f),
                    Shader.TileMode.CLAMP
                )
                sbBgPaint.shader = radialShader
                canvas.drawCircle(centerX, radarY, glowRadius, sbBgPaint)
                sbBgPaint.shader = null
            } catch (_: Exception) {}

            // B) Pürüzsüz, yumuşak anti-aliased grid mikro-noktaları (Tablette kesik/pikselleşmiş görünümü engeller)
            val dotStep = 48f * dp
            var px = dotStep / 2f
            while (px < w) {
                var py = dotStep / 2f
                while (py < h) {
                    canvas.drawCircle(px, py, 1.1f * dp, sbGridDotPaint)
                    py += dotStep
                }
                px += dotStep
            }

            // 4. Üst Bar (Header Bar)
            // A) Logo Rozeti (Özel Uygulama Logosu)
            sbRectF.set(padX, padY, padX + logoSize, padY + logoSize)

            val logoBmp = getOrCreateLogoBitmap(logoSize.toInt().coerceAtLeast(1), 10f * dp)
            if (logoBmp != null && !logoBmp.isRecycled) {
                canvas.drawBitmap(logoBmp, padX, padY, null)
            } else {
                sbCardBgPaint.color = Color.rgb(17, 26, 44)
                canvas.drawRoundRect(sbRectF, 10f * dp, 10f * dp, sbCardBgPaint)
            }

            sbCardBorderPaint.color = Color.argb(160, 14, 165, 233)
            sbCardBorderPaint.strokeWidth = 1.6f * dp
            canvas.drawRoundRect(sbRectF, 10f * dp, 10f * dp, sbCardBorderPaint)

            // B) Marka Başlığı (Beta etiketiyle)
            val titleX = padX + logoSize + 12f * dp
            sbTextBold.textSize = 16f * sp
            sbTextBold.color = Color.WHITE
            sbTextBold.textAlign = Paint.Align.LEFT
            canvas.drawText("MobiDesk ", titleX, padY + 20f * dp, sbTextBold)

            val mobiW = sbTextBold.measureText("MobiDesk ")
            sbTextBold.color = Color.rgb(56, 189, 248)
            canvas.drawText("STUDIO ", titleX + mobiW, padY + 20f * dp, sbTextBold)

            val studioW = sbTextBold.measureText("STUDIO ")

            // Mikro Rozet (Warm Amber Beta Badge)
            val betaText = "BETA"
            sbTextBold.textSize = 9f * sp
            val betaW = sbTextBold.measureText(betaText)
            val betaBadgeW = betaW + 10f * dp
            val betaBadgeH = 16f * dp
            val betaBadgeX = titleX + mobiW + studioW + 2f * dp
            val betaBadgeY = padY + 7.5f * dp

            sbRectF2.set(betaBadgeX, betaBadgeY, betaBadgeX + betaBadgeW, betaBadgeY + betaBadgeH)
            sbBadgeBgPaint.color = Color.argb(45, 245, 158, 11)
            canvas.drawRoundRect(sbRectF2, 4f * dp, 4f * dp, sbBadgeBgPaint)

            sbBadgeBorderPaint.color = Color.argb(120, 245, 158, 11)
            sbBadgeBorderPaint.strokeWidth = 1f * dp
            canvas.drawRoundRect(sbRectF2, 4f * dp, 4f * dp, sbBadgeBorderPaint)

            sbTextBold.color = Color.rgb(251, 191, 36)
            canvas.drawText(betaText, betaBadgeX + 5f * dp, betaBadgeY + 11.5f * dp, sbTextBold)

            sbTextRegular.textSize = 10.5f * sp
            sbTextRegular.color = Color.rgb(148, 163, 184)
            sbTextRegular.textAlign = Paint.Align.LEFT
            canvas.drawText("Yüksek Hızlı İkinci Ekran Alıcısı (Receiver)", titleX, padY + 38f * dp, sbTextRegular)

            // C) Sağ Üst Cihaz & Donanım Rozeti (Hap)
            val modelStr = (Build.MODEL ?: "Cihaz").replace("_", " ")
            val specStr = "📱 $modelStr  •  ${canvas.width}×${canvas.height}  •  ${resources.displayMetrics.densityDpi} DPI"
            sbTextRegular.textSize = 10.5f * sp
            sbTextRegular.color = Color.rgb(203, 213, 225)
            val specW = sbTextRegular.measureText(specStr)
            val pillW = specW + 24f * dp
            val pillH = 30f * dp
            val pillL = w - padX - pillW
            val pillT = padY + 6f * dp
            sbRectF.set(pillL, pillT, pillL + pillW, pillT + pillH)

            sbBadgeBgPaint.color = Color.rgb(15, 23, 42)
            canvas.drawRoundRect(sbRectF, pillH / 2f, pillH / 2f, sbBadgeBgPaint)

            sbBadgeBorderPaint.color = Color.rgb(30, 41, 59)
            sbBadgeBorderPaint.strokeWidth = 1.2f * dp
            canvas.drawRoundRect(sbRectF, pillH / 2f, pillH / 2f, sbBadgeBorderPaint)

            canvas.drawText(specStr, pillL + 12f * dp, pillT + 19.5f * dp, sbTextRegular)

            // 5. Merkez Hub (Radar Nabzı, Durum Kapsülü, Büyük Başlık)
            val isError = errorMsg != null
            val mainAccent = if (isError) Color.rgb(239, 68, 68) else Color.rgb(16, 185, 129)
            val subAccent = if (isError) Color.rgb(248, 113, 113) else Color.rgb(110, 231, 183)

            if (!isError) {
                // Radar Dalgası 1
                val wave1 = (phase * 16f) % 46f
                val r1 = (16f + wave1) * dp
                val a1 = ((1f - wave1 / 46f) * 150f).toInt().coerceIn(0, 255)
                sbWavePaint.color = Color.argb(a1, 16, 185, 129)
                sbWavePaint.strokeWidth = 1.8f * dp
                canvas.drawCircle(centerX, radarY, r1, sbWavePaint)

                // Radar Dalgası 2
                val wave2 = (phase * 16f + 23f) % 46f
                val r2 = (16f + wave2) * dp
                val a2 = ((1f - wave2 / 46f) * 150f).toInt().coerceIn(0, 255)
                sbWavePaint.color = Color.argb(a2, 16, 185, 129)
                sbWavePaint.strokeWidth = 1.8f * dp
                canvas.drawCircle(centerX, radarY, r2, sbWavePaint)
            }

            // Merkez Işıltı Dairesi
            sbGlowPaint.color = Color.argb(45, Color.red(mainAccent), Color.green(mainAccent), Color.blue(mainAccent))
            canvas.drawCircle(centerX, radarY, 22f * dp, sbGlowPaint)

            // Merkez Katı Çekirdek
            sbGlowPaint.color = mainAccent
            canvas.drawCircle(centerX, radarY, 10f * dp, sbGlowPaint)

            // Çekirdek Merkez Parlaması
            sbGlowPaint.color = Color.WHITE
            canvas.drawCircle(centerX, radarY, 3.5f * dp, sbGlowPaint)

            // Durum Hapı (Status Pill)
            val statusText = if (isError) "⚠️  PORT BAŞLATILAMADI" else "●  HAZIR • BAĞLANTI BEKLENİYOR"
            sbTextBold.textSize = 11f * sp
            sbTextBold.color = subAccent
            val stTextW = sbTextBold.measureText(statusText)

            val stPillW = stTextW + 28f * dp
            val stPillH = 26f * dp
            val stPillL = centerX - stPillW / 2f
            val stPillT = radarY + 20f * dp
            sbRectF.set(stPillL, stPillT, stPillL + stPillW, stPillT + stPillH)

            sbBadgeBgPaint.color = if (isError) Color.argb(120, 69, 10, 10) else Color.argb(120, 6, 78, 59)
            canvas.drawRoundRect(sbRectF, stPillH / 2f, stPillH / 2f, sbBadgeBgPaint)

            sbBadgeBorderPaint.color = mainAccent
            sbBadgeBorderPaint.strokeWidth = 1.2f * dp
            canvas.drawRoundRect(sbRectF, stPillH / 2f, stPillH / 2f, sbBadgeBorderPaint)

            sbTextBold.textAlign = Paint.Align.CENTER
            canvas.drawText(statusText, centerX, stPillT + 17f * dp, sbTextBold)

            // Büyük Başlık ("Bilgisayarınız Bekleniyor" - Ekrana Tam Ortalı)
            val mainTitle = if (isError) (errorMsg ?: "Soket Hatası") else "Bilgisayarınız Bekleniyor"
            sbTextBold.textSize = 21f * sp
            sbTextBold.color = Color.rgb(248, 250, 252)
            sbTextBold.textAlign = Paint.Align.CENTER
            val mainTitleY = stPillT + stPillH + 20f * sp
            canvas.drawText(mainTitle, centerX, mainTitleY, sbTextBold)

            // Alt Açıklama
            val subTitle = if (isError) "Lütfen başka bir uygulamanın port 55555'i kullanmadığından emin olun"
                           else "PC'de MobiDesk uygulamasını açtığınızda ekranınız otomatik olarak açılacaktır"
            sbTextRegular.textSize = 11f * sp
            sbTextRegular.color = Color.rgb(148, 163, 184)
            sbTextRegular.textAlign = Paint.Align.CENTER
            val subTitleY = mainTitleY + 15f * sp
            canvas.drawText(subTitle, centerX, subTitleY, sbTextRegular)

            // 6. Altta Konumlandırılmış Container Çizimi
            sbRectF.set(cardLeft, cardTop, cardLeft + cardW, cardBottom)
            sbCardBgPaint.color = Color.rgb(15, 23, 42)
            canvas.drawRoundRect(sbRectF, cardRadius, cardRadius, sbCardBgPaint)

            sbCardBorderPaint.color = Color.rgb(30, 41, 59)
            sbCardBorderPaint.strokeWidth = 1.2f * dp
            canvas.drawRoundRect(sbRectF, cardRadius, cardRadius, sbCardBorderPaint)

            // --- SOL KOLON: KABLOLU BAĞLANTI (USB) ---
            sbTextBold.textAlign = Paint.Align.LEFT
            sbTextRegular.textAlign = Paint.Align.LEFT

            val tag1 = "⚡ AKTİF MOD (0 MS GECİKME)"
            sbTextBold.textSize = 9.8f * sp
            sbTextBold.color = Color.rgb(56, 189, 248)
            val tag1W = sbTextBold.measureText(tag1)
            val tag1H = 22f * dp
            val tag1L = col1L
            val tag1T = cardTop + 12f * dp
            sbRectF2.set(tag1L, tag1T, tag1L + tag1W + 16f * dp, tag1T + tag1H)
            sbBadgeBgPaint.color = Color.rgb(12, 74, 110)
            canvas.drawRoundRect(sbRectF2, tag1H / 2f, tag1H / 2f, sbBadgeBgPaint)
            canvas.drawText(tag1, tag1L + 8f * dp, tag1T + 14.5f * dp, sbTextBold)

            // Sol Başlık
            sbTextBold.textSize = 15f * sp
            sbTextBold.color = Color.WHITE
            val solTitleY = tag1T + tag1H + 18f * sp
            canvas.drawText("Kablolu Bağlantı (USB)", col1L, solTitleY, sbTextBold)

            // Sol Maddeler
            val stepY = solTitleY + 20f * sp
            val lineSpace = 18f * sp

            sbTextRegular.textSize = 11.2f * sp
            sbTextRegular.color = Color.rgb(241, 245, 249)
            canvas.drawText("• Cihazı USB kablosu ile bilgisayara bağlayın", col1L, stepY, sbTextRegular)

            sbTextRegular.color = Color.rgb(148, 163, 184)
            canvas.drawText("• MobiDesk PC uygulaması ADB tünelini otomatik kurar", col1L, stepY + lineSpace, sbTextRegular)

            sbTextRegular.color = Color.rgb(100, 116, 139)
            canvas.drawText("• 60 FPS donanım hızlandırmalı sıfır gecikme ve şarj desteği", col1L, stepY + lineSpace * 2f, sbTextRegular)

            // --- SAĞ KOLON: ÖNEMLİ GEREKSİNİM (USB HATA AYIKLAMA KUTUSU) ---
            val boxT = cardTop + 8f * dp
            val boxB = cardBottom - 8f * dp
            sbRectF2.set(col2L, boxT, col2L + colW, boxB)

            // Kutu Dolgusu (Derin lacivert) & Amber Çerçeve
            sbBadgeBgPaint.color = Color.argb(160, 19, 30, 53)
            canvas.drawRoundRect(sbRectF2, 10f * dp, 10f * dp, sbBadgeBgPaint)

            sbBadgeBorderPaint.color = Color.rgb(245, 158, 11) // Amber dikkat çizgisi
            sbBadgeBorderPaint.strokeWidth = 1.3f * dp
            canvas.drawRoundRect(sbRectF2, 10f * dp, 10f * dp, sbBadgeBorderPaint)

            val boxPadL = col2L + 14f * dp

            // Uyarı Rozeti
            val warnBadge = "⚠️ ÖNEMLİ GEREKSİNİM"
            sbTextBold.textSize = 9.2f * sp
            sbTextBold.color = Color.rgb(253, 230, 138)
            val wbW = sbTextBold.measureText(warnBadge)
            val wbH = 20f * dp
            val wbT = boxT + 9f * dp
            val sbRectF3 = RectF(boxPadL, wbT, boxPadL + wbW + 14f * dp, wbT + wbH)
            sbCardBgPaint.color = Color.rgb(120, 53, 15)
            canvas.drawRoundRect(sbRectF3, wbH / 2f, wbH / 2f, sbCardBgPaint)
            canvas.drawText(warnBadge, boxPadL + 7f * dp, wbT + 13.5f * dp, sbTextBold)

            // Kutu Başlığı
            sbTextBold.textSize = 13.5f * sp
            sbTextBold.color = Color.rgb(251, 191, 36)
            val boxTitleY = wbT + wbH + 16f * sp
            canvas.drawText("USB Hata Ayıklama Açık Olmalıdır", boxPadL, boxTitleY, sbTextBold)

            // Kutu Adımları
            val boxStepY = boxTitleY + 18f * sp
            val boxLineSpace = 17f * sp

            sbTextRegular.textSize = 11.2f * sp
            sbTextRegular.color = Color.rgb(241, 245, 249)
            canvas.drawText("1. Cihaz Ayarları > Geliştirici Seçenekleri menüsüne gidin", boxPadL, boxStepY, sbTextRegular)

            sbTextBold.textSize = 11.2f * sp
            sbTextBold.color = Color.rgb(110, 231, 183)
            canvas.drawText("2. \"USB Hata Ayıklama\" (USB Debugging) seçeneğini AÇIN", boxPadL, boxStepY + boxLineSpace, sbTextBold)

            sbTextRegular.textSize = 9.8f * sp
            sbTextRegular.color = Color.rgb(148, 163, 184)
            canvas.drawText("(Menü yoksa: Ayarlar > Cihaz Hakkında > 'Yapım Numarası'na 7 kez dokunun)", boxPadL, boxStepY + boxLineSpace * 2f, sbTextRegular)

            // 7. En Alt Bilgi Çubuğu (Footer Bar)
            val footY = h - 10f * dp
            sbTextRegular.textSize = 10f * sp
            sbTextRegular.color = Color.rgb(100, 116, 139)
            sbTextRegular.textAlign = Paint.Align.LEFT
            canvas.drawText("✦ Dokunmatik ekran devrede  •  Sol üste çift dokunma: FPS sayacını açar/kapatır", padX, footY, sbTextRegular)

            val rightFoot = "● TCP Dinleniyor (Port 55555)  •  MobiDesk Studio (Beta)"
            sbTextBold.textSize = 10.5f * sp
            sbTextBold.color = Color.rgb(16, 185, 129)
            sbTextBold.textAlign = Paint.Align.RIGHT
            canvas.drawText(rightFoot, w - padX, footY, sbTextBold)
        }
    }
}
