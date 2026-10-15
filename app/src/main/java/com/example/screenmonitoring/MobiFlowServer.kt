package com.example.screenmonitoring

import android.content.Context
import android.media.MediaScannerConnection
import android.os.Environment
import android.os.Looper
import android.util.Log
import org.json.JSONObject
import java.io.*
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.util.*
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.atomic.AtomicBoolean

class MobiFlowServer private constructor() {

    companion object {
        private const val TAG = "MobiFlowServer"
        val instance: MobiFlowServer by lazy { MobiFlowServer() }
    }

    interface FlowEventListener {
        fun onMessageReceived(msg: FlowMessage)
        fun onFileProgress(msg: FlowMessage)
        fun onConnectionChanged(connected: Boolean)
        fun onNotificationDismissRequested(key: String, pkg: String, id: Int)
        fun onNotificationPosted(notif: NotificationModel) {}
        fun onNotificationRemoved(key: String) {}
    }

    private val isRunning = AtomicBoolean(false)
    val isRunningNow: Boolean
        get() = isRunning.get()
    private var serverSocket: ServerSocket? = null
    private var activeSocket: Socket? = null
    private var outputStream: OutputStream? = null
    private val sendLock = Any()

    private val listeners = CopyOnWriteArrayList<FlowEventListener>()
    val messageHistory = CopyOnWriteArrayList<FlowMessage>()
    val activeNotifications = CopyOnWriteArrayList<NotificationModel>()

    // Aktif dosya alımı takibi (TransferId -> FileOutputStream)
    private val activeIncomingFiles = HashMap<String, FileOutputStream>()
    private val incomingFileMessages = HashMap<String, FlowMessage>()

    private var appContext: Context? = null

    fun init(context: Context) {
        this.appContext = context.applicationContext
    }

    fun addListener(listener: FlowEventListener) {
        if (!listeners.contains(listener)) listeners.add(listener)
    }

    fun removeListener(listener: FlowEventListener) {
        listeners.remove(listener)
    }

    fun isConnected(): Boolean {
        val s = activeSocket
        return s != null && s.isConnected && !s.isClosed
    }

    fun start() {
        if (isRunning.getAndSet(true)) return
        Thread({
            try {
                val server = ServerSocket()
                server.reuseAddress = true
                server.bind(InetSocketAddress(MobiFlowProtocol.PORT))
                serverSocket = server
                Log.i(TAG, "MobiFlowServer started on port ${MobiFlowProtocol.PORT}")

                while (isRunning.get() && !server.isClosed) {
                    try {
                        val client = server.accept()
                        client.tcpNoDelay = true
                        client.sendBufferSize = 64 * 1024
                        client.receiveBufferSize = 64 * 1024
                        handleClient(client)
                    } catch (e: Exception) {
                        if (isRunning.get()) {
                            Log.w(TAG, "Client accept/loop error: ${e.message}")
                        }
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Server bind error: ${e.message}")
            } finally {
                stop()
            }
        }, "MobiFlow-Server").start()
    }

    fun stop() {
        isRunning.set(false)
        try { serverSocket?.close() } catch (_: Exception) {}
        serverSocket = null
        synchronized(sendLock) {
            try { activeSocket?.close() } catch (_: Exception) {}
            activeSocket = null
            outputStream = null
        }
        notifyConnectionChanged(false)
    }

    private fun handleClient(socket: Socket) {
        synchronized(sendLock) {
            activeSocket = socket
            outputStream = socket.getOutputStream()
        }
        notifyConnectionChanged(true)
        Log.i(TAG, "PC client connected to MobiFlow!")

        // Var olan aktif bildirimleri yeni bağlanan PC istemcisine gönder
        for (notif in activeNotifications) {
            sendNotificationPacket(notif)
        }

        try {
            val dis = DataInputStream(BufferedInputStream(socket.getInputStream()))
            while (isRunning.get() && !socket.isClosed) {
                // 1. Magic oku (2 byte: 'M', 'F')
                val m0 = dis.read()
                if (m0 == -1) break
                val m1 = dis.read()
                if (m1 == -1) break
                if (m0 != 0x4D || m1 != 0x46) {
                    Log.w(TAG, "Invalid magic bytes: $m0, $m1 - skipping")
                    continue
                }

                // 2. Type (2 byte Short)
                val type = dis.readShort()

                // 3. Length (4 byte Int)
                val length = dis.readInt()
                if (length < 0 || length > 100 * 1024 * 1024) { // Max 100MB per frame
                    Log.w(TAG, "Invalid frame length: $length")
                    break
                }

                // 4. Payload oku
                val payload = ByteArray(length)
                dis.readFully(payload)

                processIncomingPacket(type, payload)
            }
        } catch (e: Exception) {
            Log.i(TAG, "Client disconnected: ${e.message}")
        } finally {
            synchronized(sendLock) {
                try { socket.close() } catch (_: Exception) {}
                if (activeSocket == socket) {
                    activeSocket = null
                    outputStream = null
                }
            }
            notifyConnectionChanged(false)
        }
    }

    private fun processIncomingPacket(type: Short, payload: ByteArray) {
        when (type) {
            MobiFlowProtocol.TYPE_TEXT -> {
                val str = String(payload, Charsets.UTF_8)
                try {
                    val json = JSONObject(str)
                    val id = json.optString("id", UUID.randomUUID().toString())
                    val sender = json.optString("sender", "pc")
                    val text = json.optString("text", "")
                    val time = json.optLong("timestamp", System.currentTimeMillis())

                    val msg = FlowMessage(id, sender, text, time)
                    messageHistory.add(msg)
                    for (l in listeners) l.onMessageReceived(msg)
                } catch (e: Exception) {
                    // Düz metin olarak da kabul et
                    val msg = FlowMessage(UUID.randomUUID().toString(), "pc", str, System.currentTimeMillis())
                    messageHistory.add(msg)
                    for (l in listeners) l.onMessageReceived(msg)
                }
            }

            MobiFlowProtocol.TYPE_FILE_START -> {
                val str = String(payload, Charsets.UTF_8)
                try {
                    val json = JSONObject(str)
                    val transferId = json.getString("transferId")
                    val fileName = json.getString("fileName")
                    val fileSize = json.getLong("fileSize")

                    // Downloads/MobiDesk dizinine kaydet
                    val downloadsDir = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS)
                    val mobiDir = File(downloadsDir, "MobiDesk")
                    if (!mobiDir.exists()) mobiDir.mkdirs()

                    val targetFile = File(mobiDir, fileName)
                    val fos = FileOutputStream(targetFile)
                    activeIncomingFiles[transferId] = fos

                    val msg = FlowMessage(
                        id = transferId,
                        sender = "pc",
                        text = "📁 $fileName",
                        timestamp = System.currentTimeMillis(),
                        isFile = true,
                        fileName = fileName,
                        fileSize = fileSize,
                        filePath = targetFile.absolutePath,
                        progress = 0
                    )
                    incomingFileMessages[transferId] = msg
                    messageHistory.add(msg)
                    for (l in listeners) l.onMessageReceived(msg)
                } catch (e: Exception) {
                    Log.e(TAG, "FILE_START error: ${e.message}")
                }
            }

            MobiFlowProtocol.TYPE_FILE_CHUNK -> {
                try {
                    val bais = ByteArrayInputStream(payload)
                    val dis = DataInputStream(bais)
                    val transferId = dis.readUTF()
                    val offset = dis.readLong()
                    val chunkLen = dis.readInt()
                    val chunkBytes = ByteArray(chunkLen)
                    dis.readFully(chunkBytes)

                    val fos = activeIncomingFiles[transferId]
                    if (fos != null) {
                        fos.write(chunkBytes)
                        val msg = incomingFileMessages[transferId]
                        if (msg != null && msg.fileSize > 0) {
                            val currentReceived = offset + chunkLen
                            msg.progress = ((currentReceived * 100) / msg.fileSize).toInt().coerceIn(0, 99)
                            for (l in listeners) l.onFileProgress(msg)
                        }
                    }
                } catch (e: Exception) {
                    Log.e(TAG, "FILE_CHUNK error: ${e.message}")
                }
            }

            MobiFlowProtocol.TYPE_FILE_END -> {
                val str = String(payload, Charsets.UTF_8)
                try {
                    val json = JSONObject(str)
                    val transferId = json.getString("transferId")
                    val fos = activeIncomingFiles.remove(transferId)
                    fos?.flush()
                    fos?.close()

                    val msg = incomingFileMessages.remove(transferId)
                    if (msg != null) {
                        msg.progress = 100
                        for (l in listeners) l.onFileProgress(msg)

                        // MediaScanner ile sisteme / galeriye / dosya yöneticisine işlet
                        appContext?.let { ctx ->
                            msg.filePath?.let { path ->
                                MediaScannerConnection.scanFile(ctx, arrayOf(path), null, null)
                            }
                        }
                    }
                } catch (e: Exception) {
                    Log.e(TAG, "FILE_END error: ${e.message}")
                }
            }

            MobiFlowProtocol.TYPE_DISMISS_NOTIFICATION -> {
                try {
                    val str = String(payload, Charsets.UTF_8)
                    val parts = str.split("\t")
                    val pkg = parts.getOrNull(0) ?: ""
                    val id = parts.getOrNull(1)?.toIntOrNull() ?: 0
                    val key = parts.getOrNull(2) ?: ""
                    for (l in listeners) l.onNotificationDismissRequested(key, pkg, id)
                } catch (_: Exception) {}
            }

            MobiFlowProtocol.TYPE_PING -> {
                sendRawPacket(MobiFlowProtocol.TYPE_PONG, ByteArray(0))
            }
        }
    }

    fun sendText(text: String): FlowMessage? {
        val id = UUID.randomUUID().toString()
        val time = System.currentTimeMillis()
        val msg = FlowMessage(id, "mobile", text, time)
        messageHistory.add(msg)
        for (l in listeners) l.onMessageReceived(msg)

        val json = JSONObject()
        json.put("id", id)
        json.put("sender", "mobile")
        json.put("text", text)
        json.put("timestamp", time)
        val payload = json.toString().toByteArray(Charsets.UTF_8)

        Thread({
            sendRawPacket(MobiFlowProtocol.TYPE_TEXT, payload)
        }, "MobiFlow-SendText").start()

        return msg
    }

    fun sendFile(file: File, onProgress: ((Int) -> Unit)? = null): FlowMessage? {
        if (!file.exists()) return null
        val transferId = UUID.randomUUID().toString()
        val fileName = file.name
        val fileSize = file.length()

        val msg = FlowMessage(
            id = transferId,
            sender = "mobile",
            text = "📁 $fileName",
            timestamp = System.currentTimeMillis(),
            isFile = true,
            fileName = fileName,
            fileSize = fileSize,
            filePath = file.absolutePath,
            progress = 0
        )
        messageHistory.add(msg)
        for (l in listeners) l.onMessageReceived(msg)

        Thread({
            try {
                // 1. FILE_START
                val startJson = JSONObject()
                startJson.put("transferId", transferId)
                startJson.put("fileName", fileName)
                startJson.put("fileSize", fileSize)
                sendRawPacket(MobiFlowProtocol.TYPE_FILE_START, startJson.toString().toByteArray(Charsets.UTF_8))

                // 2. FILE_CHUNKS (64 KB)
                val buffer = ByteArray(64 * 1024)
                val fis = FileInputStream(file)
                var offset = 0L
                var read: Int

                while (fis.read(buffer).also { read = it } != -1 && isRunning.get()) {
                    val baos = ByteArrayOutputStream()
                    val dos = DataOutputStream(baos)
                    dos.writeUTF(transferId)
                    dos.writeLong(offset)
                    dos.writeInt(read)
                    dos.write(buffer, 0, read)
                    dos.flush()

                    sendRawPacket(MobiFlowProtocol.TYPE_FILE_CHUNK, baos.toByteArray())
                    offset += read

                    val pct = ((offset * 100) / fileSize).toInt().coerceIn(0, 99)
                    msg.progress = pct
                    for (l in listeners) l.onFileProgress(msg)
                    onProgress?.invoke(pct)
                }
                fis.close()

                // 3. FILE_END
                val endJson = JSONObject()
                endJson.put("transferId", transferId)
                endJson.put("status", "OK")
                sendRawPacket(MobiFlowProtocol.TYPE_FILE_END, endJson.toString().toByteArray(Charsets.UTF_8))

                msg.progress = 100
                for (l in listeners) l.onFileProgress(msg)
                onProgress?.invoke(100)
            } catch (e: Exception) {
                Log.e(TAG, "File send error: ${e.message}")
            }
        }, "MobiFlow-SendFile").start()

        return msg
    }

    fun sendNotification(notif: NotificationModel) {
        activeNotifications.removeAll { it.key == notif.key }
        activeNotifications.add(0, notif)
        for (l in listeners) l.onNotificationPosted(notif)
        Thread({
            sendNotificationPacket(notif)
        }, "MobiFlow-SendNotif").start()
    }

    private fun sendNotificationPacket(notif: NotificationModel) {
        val json = JSONObject()
        json.put("id", notif.id)
        json.put("key", notif.key)
        json.put("pkg", notif.packageName)
        json.put("app", notif.appName)
        json.put("title", notif.title)
        json.put("text", notif.text)
        json.put("timestamp", notif.timestamp)
        if (!notif.iconBase64.isNullOrEmpty()) {
            json.put("icon", notif.iconBase64)
        }
        val payload = json.toString().toByteArray(Charsets.UTF_8)
        sendRawPacket(MobiFlowProtocol.TYPE_NOTIFICATION, payload)
    }

    fun sendNotificationRemove(key: String, pkg: String, id: Int) {
        activeNotifications.removeAll { it.key == key }
        for (l in listeners) l.onNotificationRemoved(key)
        val str = "$pkg\t$id\t$key"
        val payload = str.toByteArray(Charsets.UTF_8)
        Thread({
            sendRawPacket(MobiFlowProtocol.TYPE_NOTIFICATION_REMOVE, payload)
        }, "MobiFlow-SendNotifRem").start()
    }

    private fun sendRawPacket(type: Short, payload: ByteArray): Boolean {
        if (Looper.myLooper() == Looper.getMainLooper()) {
            Thread({
                sendRawPacket(type, payload)
            }, "MobiFlow-AsyncWorker").start()
            return true
        }

        synchronized(sendLock) {
            val out = outputStream ?: return false
            return try {
                val dos = DataOutputStream(out)
                dos.write(MobiFlowProtocol.MAGIC)
                dos.writeShort(type.toInt())
                dos.writeInt(payload.size)
                if (payload.isNotEmpty()) {
                    dos.write(payload)
                }
                dos.flush()
                true
            } catch (e: Exception) {
                Log.w(TAG, "sendRawPacket error: ${e.javaClass.simpleName} - ${e.message}")
                false
            }
        }
    }

    private fun notifyConnectionChanged(connected: Boolean) {
        for (l in listeners) l.onConnectionChanged(connected)
    }
}
