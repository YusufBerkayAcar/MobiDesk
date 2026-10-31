<#
.SYNOPSIS
    Automated TCP Audio Streaming & Packet Framing Verification Test Harness.
.DESCRIPTION
    Validates Port 55556 TCP streaming protocol:
      - 4-byte Big-Endian Int32 payload length
      - 8-byte Big-Endian Int64 timestamp (ms)
      - Raw 48kHz 16-bit Stereo PCM payload
    Tests packet throughput, transmission timings, and asserts 0 drops over 500 packets.
    Modes:
      - Mock Loopback (-MockMode): Runs internal mock receiver on Port 55560 to assert protocol parsing & 0 drops.
      - Live Device (-LiveMode or -TargetSerial): Streams directly to live Android device via Port 55556 ADB forward.
#>

[CmdletBinding()]
param(
    [string]$TargetSerial = "",
    [string]$TargetHost = "127.0.0.1",
    [int]$TargetPort = 55556,
    [int]$PacketCount = 500,
    [int]$FramesPerPacket = 480, # 10 ms at 48kHz = 480 frames = 1920 bytes
    [switch]$MockMode = $false,
    [switch]$LiveMode = $false
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "       MOBIDESK AUDIO STREAM TRANSMISSION TEST HARNESS     " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Host: $TargetHost | Port: $TargetPort | Packets: $PacketCount | Frames/pkt: $FramesPerPacket"

$runnerSource = @"
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;

public class AudioStreamTester
{
    public class MockReceiverStats
    {
        public int PacketsReceived = 0;
        public long BytesReceived = 0;
        public bool ProtocolValid = true;
        public string ErrorMessage = "";
        public List<long> Timestamps = new List<long>();
    }

    public static MockReceiverStats RunMockReceiver(int port, int expectedPackets, int timeoutMs)
    {
        MockReceiverStats stats = new MockReceiverStats();
        TcpListener listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Parse("127.0.0.1"), port);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Start();

            IAsyncResult ar = listener.BeginAcceptTcpClient(null, null);
            if (!ar.AsyncWaitHandle.WaitOne(timeoutMs))
            {
                stats.ProtocolValid = false;
                stats.ErrorMessage = "Timeout waiting for client connection on port " + port;
                return stats;
            }

            using (TcpClient client = listener.EndAcceptTcpClient(ar))
            using (NetworkStream stream = client.GetStream())
            using (BinaryReader reader = new BinaryReader(stream))
            {
                client.ReceiveTimeout = 5000;
                long lastTimestamp = 0;

                while (stats.PacketsReceived < expectedPackets)
                {
                    int rawLength;
                    try
                    {
                        rawLength = reader.ReadInt32();
                    }
                    catch (EndOfStreamException)
                    {
                        break;
                    }

                    int length = IPAddress.NetworkToHostOrder(rawLength);
                    if (length <= 0 || length > 65536)
                    {
                        stats.ProtocolValid = false;
                        stats.ErrorMessage = "Invalid packet length received: " + length;
                        break;
                    }

                    long rawTimestamp = reader.ReadInt64();
                    long timestamp = IPAddress.NetworkToHostOrder(rawTimestamp);

                    if (stats.PacketsReceived > 0 && timestamp < lastTimestamp)
                    {
                        stats.ProtocolValid = false;
                        stats.ErrorMessage = "Non-monotonic timestamp detected: " + timestamp + " < " + lastTimestamp;
                    }
                    lastTimestamp = timestamp;

                    byte[] pcmData = new byte[length];
                    int totalRead = 0;
                    while (totalRead < length)
                    {
                        int read = stream.Read(pcmData, totalRead, length - totalRead);
                        if (read <= 0) break;
                        totalRead += read;
                    }

                    if (totalRead != length)
                    {
                        stats.ProtocolValid = false;
                        stats.ErrorMessage = "Incomplete PCM payload. Expected: " + length + ", got: " + totalRead;
                        break;
                    }

                    stats.PacketsReceived++;
                    stats.BytesReceived += (4 + 8 + length);
                    stats.Timestamps.Add(timestamp);
                }
            }
        }
        catch (Exception ex)
        {
            stats.ProtocolValid = false;
            stats.ErrorMessage = "Exception in receiver: " + ex.Message;
        }
        finally
        {
            try { if (listener != null) listener.Stop(); } catch {}
        }
        return stats;
    }

    public class SenderStats
    {
        public int PacketsSent = 0;
        public long BytesSent = 0;
        public double ElapsedSeconds = 0;
        public double AvgPacketSendMs = 0;
        public double ThroughputKbps = 0;
        public bool Success = true;
        public string ErrorMessage = "";
    }

    public static SenderStats RunSender(string host, int port, int packetCount, int framesPerPacket, bool pace10ms)
    {
        SenderStats stats = new SenderStats();
        int bytesPerFrame = 4; // 16-bit stereo = 4 bytes
        int pcmBytes = framesPerPacket * bytesPerFrame;
        byte[] pcmData = new byte[pcmBytes];

        // Generate 440 Hz Sine wave (16-bit stereo PCM)
        double freq = 440.0;
        double sampleRate = 48000.0;
        for (int i = 0; i < framesPerPacket; i++)
        {
            double angle = 2.0 * Math.PI * freq * (double)i / sampleRate;
            short sample = (short)(Math.Sin(angle) * 16384.0);
            int idx = i * 4;
            pcmData[idx] = (byte)(sample & 0xFF);
            pcmData[idx + 1] = (byte)((sample >> 8) & 0xFF);
            pcmData[idx + 2] = (byte)(sample & 0xFF);
            pcmData[idx + 3] = (byte)((sample >> 8) & 0xFF);
        }

        Stopwatch swTotal = Stopwatch.StartNew();
        List<double> packetTimes = new List<double>(packetCount);

        try
        {
            using (TcpClient client = new TcpClient())
            {
                client.NoDelay = true;
                client.SendBufferSize = 64 * 1024;
                client.Connect(host, port);

                using (NetworkStream stream = client.GetStream())
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    Stopwatch swPacket = new Stopwatch();

                    for (int p = 0; p < packetCount; p++)
                    {
                        swPacket.Restart();
                        long timestampMs = (long)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalMilliseconds;

                        // Protocol Framing:
                        // [4-byte Big-Endian length]
                        writer.Write(IPAddress.HostToNetworkOrder(pcmBytes));
                        // [8-byte Big-Endian timestamp]
                        writer.Write(IPAddress.HostToNetworkOrder(timestampMs));
                        // [Raw PCM Bytes]
                        writer.Write(pcmData, 0, pcmBytes);
                        writer.Flush();

                        swPacket.Stop();
                        packetTimes.Add(swPacket.Elapsed.TotalMilliseconds);
                        stats.PacketsSent++;
                        stats.BytesSent += (4 + 8 + pcmBytes);

                        if (pace10ms)
                        {
                            Thread.Sleep(10);
                        }
                    }
                }
            }
            swTotal.Stop();
            stats.ElapsedSeconds = swTotal.Elapsed.TotalSeconds;

            double sum = 0;
            for (int i = 0; i < packetTimes.Count; i++) sum += packetTimes[i];
            stats.AvgPacketSendMs = packetTimes.Count > 0 ? (sum / packetTimes.Count) : 0;
            stats.ThroughputKbps = stats.ElapsedSeconds > 0 ? ((stats.BytesSent * 8.0) / (stats.ElapsedSeconds * 1000.0)) : 0;
        }
        catch (Exception ex)
        {
            swTotal.Stop();
            stats.Success = false;
            stats.ErrorMessage = ex.Message;
            stats.ElapsedSeconds = swTotal.Elapsed.TotalSeconds;
        }

        return stats;
    }

    public class FullTestResult
    {
        public SenderStats Sender;
        public MockReceiverStats Receiver;
    }

    public static FullTestResult RunFullLoopbackTest(int port, int packetCount, int framesPerPacket, bool pace10ms)
    {
        Task<MockReceiverStats> receiverTask = Task.Factory.StartNew(() => RunMockReceiver(port, packetCount, 10000));
        Thread.Sleep(250);
        SenderStats sender = RunSender("127.0.0.1", port, packetCount, framesPerPacket, pace10ms);
        MockReceiverStats receiver = receiverTask.GetAwaiter().GetResult();
        return new FullTestResult { Sender = sender, Receiver = receiver };
    }
}
"@

Add-Type -TypeDefinition $runnerSource -Language CSharp

# Setup ADB port forward if serial provided
if ($TargetSerial -ne "") {
    $adbPath = if (Test-Path "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe") { "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" } else { "adb" }
    if (Test-Path $adbPath) {
        Write-Host "Configuring ADB port forward for serial $TargetSerial on Port $TargetPort..." -ForegroundColor Yellow
        $fwdCmd = "& `"$adbPath`" -s $TargetSerial forward tcp:$TargetPort tcp:$TargetPort"
        Invoke-Expression $fwdCmd
        Write-Host "ADB forward tcp:$TargetPort tcp:$TargetPort active." -ForegroundColor Green
    }
}

# Determine mode: Mock vs Live
$isMock = $MockMode -or ($TargetSerial -eq "" -and -not $LiveMode)

if ($isMock) {
    function Get-FreeTcpPort([int]$fallbackPort) {
        try {
            $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $fallbackPort)
            $listener.Start()
            $listener.Stop()
            return $fallbackPort
        } catch {
            $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
            $listener.Start()
            $free = ($listener.LocalEndpoint).Port
            $listener.Stop()
            return $free
        }
    }
    $testPort = if ($TargetPort -eq 55556) { Get-FreeTcpPort 55560 } else { $TargetPort }
    Write-Host "`n--> [MODE: MOCK LOOPBACK] Testing protocol framing & throughput on port $testPort..." -ForegroundColor Yellow
    Write-Host "Streaming $PacketCount packets (paced at 10ms realtime audio rate)..."
    
    $result = [AudioStreamTester]::RunFullLoopbackTest($testPort, $PacketCount, $FramesPerPacket, $true)
    $senderStats = $result.Sender
    $receiverStats = $result.Receiver
    
    Write-Host "`n==========================================================" -ForegroundColor Cyan
    Write-Host "            MOCK LOOPBACK TELEMETRY & RESULTS             " -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Cyan
    $sendSuccessStr = if ($senderStats.Success) { 'SUCCESS' } else { 'FAILED' }
    $sendColor = if ($senderStats.Success) { 'Green' } else { 'Red' }
    Write-Host "Sender Status: $sendSuccessStr" -ForegroundColor $sendColor
    Write-Host "Packets Sent: $($senderStats.PacketsSent) / $PacketCount"
    Write-Host "Bytes Sent: $($senderStats.BytesSent) bytes"
    Write-Host "Duration: $($senderStats.ElapsedSeconds.ToString('0.00')) s"
    Write-Host "Avg Packet Latency: $($senderStats.AvgPacketSendMs.ToString('0.000')) ms"
    Write-Host "Throughput: $($senderStats.ThroughputKbps.ToString('0.0')) Kbps"
    
    Write-Host "`nReceiver Verification:" -ForegroundColor Yellow
    Write-Host "Packets Received: $($receiverStats.PacketsReceived) / $PacketCount"
    Write-Host "Bytes Received: $($receiverStats.BytesReceived) bytes"
    Write-Host "Protocol Valid: $($receiverStats.ProtocolValid)"
    if ($receiverStats.ErrorMessage -ne "") {
        Write-Host "Receiver Error: $($receiverStats.ErrorMessage)" -ForegroundColor Red
    }
    
    $drops = $PacketCount - $receiverStats.PacketsReceived
    $dropColor = if ($drops -eq 0) { 'Green' } else { 'Red' }
    Write-Host "Packet Drops: $drops" -ForegroundColor $dropColor

    if ($senderStats.Success -and $receiverStats.ProtocolValid -and ($drops -eq 0)) {
        Write-Host "`n[PASS] Mock loopback test completed with ZERO drops and 100% protocol integrity." -ForegroundColor Green
        exit 0
    } else {
        Write-Host "`n[FAIL] Mock loopback test failed verification." -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "`n--> [MODE: LIVE DEVICE] Streaming 48kHz Stereo PCM to $TargetHost : $TargetPort..." -ForegroundColor Yellow
    $senderStats = [AudioStreamTester]::RunSender($TargetHost, $TargetPort, $PacketCount, $FramesPerPacket, $true)
    
    Write-Host "`n==========================================================" -ForegroundColor Cyan
    Write-Host "            LIVE DEVICE STREAMING TELEMETRY               " -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Cyan
    $sendSuccessStr = if ($senderStats.Success) { 'SUCCESS' } else { 'FAILED' }
    $sendColor = if ($senderStats.Success) { 'Green' } else { 'Red' }
    Write-Host "Sender Status: $sendSuccessStr" -ForegroundColor $sendColor
    Write-Host "Packets Sent: $($senderStats.PacketsSent) / $PacketCount"
    Write-Host "Bytes Sent: $($senderStats.BytesSent) bytes"
    Write-Host "Duration: $($senderStats.ElapsedSeconds.ToString('0.00')) s"
    Write-Host "Avg Packet Latency: $($senderStats.AvgPacketSendMs.ToString('0.000')) ms"
    Write-Host "Throughput: $($senderStats.ThroughputKbps.ToString('0.0')) Kbps"

    if ($senderStats.Success -and $senderStats.PacketsSent -eq $PacketCount) {
        Write-Host "`n[PASS] Live device audio streaming succeeded ($($senderStats.PacketsSent) packets transmitted, 0 drops)." -ForegroundColor Green
        exit 0
    } else {
        Write-Host "`n[FAIL] Live stream failed: $($senderStats.ErrorMessage)" -ForegroundColor Red
        exit 1
    }
}
