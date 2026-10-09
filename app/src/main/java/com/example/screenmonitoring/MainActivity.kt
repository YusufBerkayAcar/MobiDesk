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

    private var surfaceWidth = 1280
    private var surfaceHeight = 800

    private var lastFpsTime = System.currentTimeMillis()
    private var frameCount = 0
    private var currentFps = 0
    @Volatile private var showFpsCounter = true
    private var lastTapTime = 0L

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // Ekranın kapanmasını engelle
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        surfaceView = SurfaceView(this)
        surfaceView.holder.setFormat(PixelFormat.RGB_565)
        surfaceView.holder.addCallback(this)
        setContentView(surfaceView)

        setupTouchEvents()
    }

    override fun onBackPressed() {
        // Geri tuşuna basıldığında uygulamayı kapatmak yerine arka plana al
        moveTaskToBack(true)
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
            if (event.actionMasked == MotionEvent.ACTION_DOWN) {
                if (event.x < 300f && event.y < 120f) {
                    val now = System.currentTimeMillis()
                    if (now - lastTapTime < 400) {
                        showFpsCounter = !showFpsCounter
                        lastTapTime = 0L
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
        serverThread = Thread(NetworkServerWorker(holder)).apply { start() }
    }

    override fun surfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {
        surfaceWidth = width
        surfaceHeight = height
    }

    override fun surfaceDestroyed(holder: SurfaceHolder) {
        isRunning.set(false)
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
        private val statusPaint = Paint().apply {
            color = Color.rgb(18, 18, 24)
        }
        private val dstRect = Rect()
        private var fpsText = "0 FPS"

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
                drawStatusMessage("Soket Başlatılamadı", "Port 55555 kullanılamıyor: ${e.message}")
                return
            }

            while (isRunning.get() && serverSocket?.isClosed == false) {
                var socket: Socket? = null
                try {
                    try {
                        drawStatusMessage(
                            "Bağlantı Bekleniyor...",
                            "PC'de MobiDesk başlatıldığında ekran otomatik açılacaktır"
                        )
                    } catch (_: Exception) {
                    }

                    socket = serverSocket!!.accept()
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

        private fun drawStatusMessage(title: String, subtitle: String) {
            val canvas = try {
                lockCanvasCompat()
            } catch (_: Exception) {
                null
            } ?: return

            try {
                canvas.drawRect(0f, 0f, surfaceWidth.toFloat(), surfaceHeight.toFloat(), statusPaint)

                val x = surfaceWidth / 2f
                val y = surfaceHeight / 2f

                val titlePaint = Paint().apply {
                    color = Color.WHITE
                    textAlign = Paint.Align.CENTER
                    textSize = 42f
                    typeface = Typeface.DEFAULT_BOLD
                    isAntiAlias = true
                    setShadowLayer(4f, 2f, 2f, Color.BLACK)
                }
                val subPaint = Paint().apply {
                    color = Color.LTGRAY
                    textAlign = Paint.Align.CENTER
                    textSize = 24f
                    typeface = Typeface.DEFAULT
                    isAntiAlias = true
                    setShadowLayer(3f, 1f, 1f, Color.BLACK)
                }

                canvas.drawText(title, x, y - 20f, titlePaint)
                canvas.drawText(subtitle, x, y + 40f, subPaint)
            } finally {
                try {
                    holder.unlockCanvasAndPost(canvas)
                } catch (_: Exception) {
                }
            }
        }
    }
}
