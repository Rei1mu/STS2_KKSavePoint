using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using KKSavePoint.Features;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace KKSavePoint.src.Features;

/// <summary>
/// Handles sending rollback messages to clients with confirmation mechanism
/// </summary>
public static class RollbackMessageHandler
{
    private static readonly object _ackLock = new object();
    private static readonly HashSet<ulong> _receivedAcks = new HashSet<ulong>();
    private static int _expectedAckCount = 0;
    private static ManualResetEventSlim _ackWaitEvent;

    /// <summary>
    /// Sends a rollback notification to all connected clients and waits for confirmation
    /// </summary>
    /// <param name="savePath">The path to the save file</param>
    /// <param name="timeoutMs">Timeout in milliseconds to wait for acknowledgments</param>
    /// <returns>True if all clients acknowledged, false if timeout or error</returns>
    public static void SendRollbackMessageWithAck(int timeoutMs = 5000)
    {
        try
        {
            var netService = SavePointFeature.GetCachedNetService();
            if (netService == null)
            {
                Log.Warn("[KKSavePoint] Cannot send rollback message: NetService not available");
                return;
            }

            // 使用反射设置私有静态字段
            var hostRollbackField = typeof(SavePointFeature).GetField("_hostRollbackInProgress", BindingFlags.Static | BindingFlags.NonPublic);
            if (hostRollbackField != null)
            {
                hostRollbackField.SetValue(null, true);
            }

            // 获取当前连接的客户端数量
            int clientCount = GetConnectedClientCount(netService);
            Log.Info($"[KKSavePoint]_k2_1 {clientCount} clients connected, waiting for their acknowledgment...");

            // 如果没有客户端，直接返回成功
            if (clientCount == 0)
            {
                Log.Info("[KKSavePoint]_k2_1f No clients connected, skipping acknowledgment wait");
                SendRollbackMessageInternal(netService);
                return;
            }

            // 初始化确认等待机制
            lock (_ackLock)
            {
                _receivedAcks.Clear();
                _expectedAckCount = clientCount;
                _ackWaitEvent = new ManualResetEventSlim(false);
            }

            // 设置回调，接收确认消息
            SavePointFeature.SetRollbackAckCallback(OnRollbackAckReceived);
            Log.Info("[KKSavePoint]_k2_2 Rollback ack callback set successfully");

            // 发送回滚消息
            SendRollbackMessageInternal(netService);

            // 等待所有客户端确认
            Log.Info("[KKSavePoint]_k2_3 Waiting for client acknowledgments...");
            bool allAcked = _ackWaitEvent.Wait(timeoutMs);

            if (allAcked)
            {
                Log.Info($"[KKSavePoint]_k2_3t All {_receivedAcks.Count} clients acknowledged rollback request");
            }
            else
            {
                Log.Warn($"[KKSavePoint]_k2_3f Timeout waiting for acknowledgments. Received {_receivedAcks.Count}/{_expectedAckCount}");
            }

            // 清理
            SavePointFeature.SetRollbackAckCallback(null);
            lock (_ackLock)
            {
                _ackWaitEvent?.Dispose();
                _ackWaitEvent = null;
            }

            return;
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to send rollback message with ack: {ex}");
            SavePointFeature.SetRollbackAckCallback(null);
            return;
        }
    }

    /// <summary>
    /// Sends a rollback notification to all connected clients (without waiting for ack)
    /// </summary>
    /// <param name="savePath">The path to the save file</param>
    /// <returns>True if the message was sent successfully</returns>
    public static void SendRollbackMessage(string savePath)
    {
        try
        {
            var netService = SavePointFeature.GetCachedNetService();
            if (netService == null)
            {
                Log.Warn("[KKSavePoint] Cannot send rollback message: NetService not available");
                return;
            }

            // 使用反射设置私有静态字段
            var hostRollbackField = typeof(SavePointFeature).GetField("_hostRollbackInProgress", BindingFlags.Static | BindingFlags.NonPublic);
            if (hostRollbackField != null)
            {
                hostRollbackField.SetValue(null, true);
            }

            SendRollbackMessageInternal(netService);
            return;

        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to send rollback message: {ex}");
            return;
        }
    }

    private static void SendRollbackMessageInternal(object netService)
    {
        var message = new LobbySeedChangedMessage
        {
            seed = "KK_SAVEPOINT_ROLLBACK_REQUEST"
        };

        var methods = netService.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
        MethodInfo sendMessageMethod = null;
        foreach (var method in methods)
        {
            if (method.Name == "SendMessage" && method.IsGenericMethodDefinition)
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 1)
                {
                    sendMessageMethod = method.MakeGenericMethod(typeof(LobbySeedChangedMessage));
                    break;
                }
            }
        }

        if (sendMessageMethod == null)
        {
            Log.Error("[KKSavePoint] SendMessage<T>(message) method not found for broadcast");
            return;
        }

        try
        {
            sendMessageMethod.Invoke(netService, new object[] { message });
            Log.Info($"[KKSavePoint] Rollback notification sent to clients with special seed");
            return;
        }
        catch (Exception ex)
        {
            // 游戏内部在 SendMessage 失败时会打印 ERROR，我们在这里只打印 WARN
            // 因为即使这里抛出异常，消息可能已经被客户端收到（只是确认没传回来）
            Log.Warn($"[KKSavePoint] Failed to send rollback message (may have been delivered): {ex.Message}");
            Log.Info("[KKSavePoint] Continuing rollback process (client may have received the message)...");
            return;
        }
    }

    private static int GetConnectedClientCount(object netService)
    {
        try
        {
            // 尝试获取 INetHostGameService 接口的 ConnectedPeers 属性
            var connectedPeersProp = netService.GetType().GetProperty("ConnectedPeers", BindingFlags.Public | BindingFlags.Instance);
            if (connectedPeersProp != null)
            {
                var connectedPeers = connectedPeersProp.GetValue(netService) as System.Collections.IEnumerable;
                if (connectedPeers != null)
                {
                    int count = 0;
                    foreach (var _ in connectedPeers) count++;
                    return count;
                }
            }

            // 备用方案：尝试获取客户端数量的其他方式
            var clientCountProp = netService.GetType().GetProperty("ClientCount", BindingFlags.Public | BindingFlags.Instance);
            if (clientCountProp != null)
            {
                var count = clientCountProp.GetValue(netService);
                return count is int intCount ? intCount : 0;
            }

            Log.Warn("[KKSavePoint] Cannot determine connected client count");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Warn($"[KKSavePoint] Error getting client count: {ex}");
            return 0;
        }
    }

    /// <summary>
    /// Called when a client acknowledgment is received
    /// </summary>
    /// <param name="clientId">The client that sent the acknowledgment</param>
    public static void OnRollbackAckReceived(ulong clientId)
    {
        lock (_ackLock)
        {
            if (!_receivedAcks.Contains(clientId))
            {
                _receivedAcks.Add(clientId);
                Log.Info($"[KKSavePoint] Received rollback acknowledgment from client {clientId}");

                if (_receivedAcks.Count >= _expectedAckCount && _ackWaitEvent != null)
                {
                    _ackWaitEvent.Set();
                }
            }
        }
    }

    /// <summary>
    /// Handles a rollback request from a client
    /// </summary>
    /// <param name="savePath">The path to the save file</param>
    public static void HandleRollbackRequest(string savePath)
    {
        try
        {
            Log.Info($"[KKSavePoint] Handling rollback request: {savePath}");

            if (SavePointFeature.IsHost())
            {
                Log.Info("[KKSavePoint] Host received rollback request, broadcasting to clients");
                SendRollbackMessage(savePath);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to handle rollback request: {ex}");
        }
    }
}

/// <summary>
/// Message sent by clients to acknowledge rollback request
/// </summary>
public struct RollbackAckMessage
{
    public string savePath;
    public ulong clientId;
}