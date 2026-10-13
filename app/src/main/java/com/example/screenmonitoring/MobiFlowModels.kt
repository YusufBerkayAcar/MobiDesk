package com.example.screenmonitoring

import java.io.Serializable

object MobiFlowProtocol {
    const val PORT = 55556
    val MAGIC = byteArrayOf(0x4D.toByte(), 0x46.toByte()) // "MF"

    const val TYPE_TEXT: Short = 0x0001
    const val TYPE_FILE_START: Short = 0x0002
    const val TYPE_FILE_CHUNK: Short = 0x0003
    const val TYPE_FILE_END: Short = 0x0004
    const val TYPE_NOTIFICATION: Short = 0x0005
    const val TYPE_NOTIFICATION_REMOVE: Short = 0x0006
    const val TYPE_PING: Short = 0x0007
    const val TYPE_PONG: Short = 0x0008
    const val TYPE_DISMISS_NOTIFICATION: Short = 0x0009
    const val TYPE_CLIPBOARD: Short = 0x000A
}

data class FlowMessage(
    val id: String,
    val sender: String, // "pc" or "mobile"
    val text: String,
    val timestamp: Long,
    val isFile: Boolean = false,
    val fileName: String? = null,
    val fileSize: Long = 0L,
    val filePath: String? = null,
    var progress: Int = 100 // 0-100 for ongoing file transfers
) : Serializable

data class NotificationModel(
    val id: Int,
    val key: String,
    val packageName: String,
    val appName: String,
    val title: String,
    val text: String,
    val timestamp: Long,
    val iconBase64: String? = null
) : Serializable
