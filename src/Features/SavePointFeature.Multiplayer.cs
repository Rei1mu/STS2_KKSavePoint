using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using KKSavePoint.Core;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Ftue;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Platform;

namespace KKSavePoint.Features;

public partial class SavePointFeature
{
    private static bool _multiplayerRollbackInitialized = false;
    private static bool? _isHost = null;
    private static bool? _isClient = null;
    private static object? _cachedNetService = null;
    private static RunSessionState? _lastSessionState = null;
    private static bool _navigatingFromMainMenu = false;
    private static bool _shouldHost = false;
    private static bool _shouldJoin = false;
    private static SerializableRun? _pendingHostSaveData = null;
    private static bool _pendingRollbackFromHost = false;
    private static bool _hostRollbackInProgress = false;
    private static System.Threading.Tasks.Task? _joinRetryTask = null;
    private static ulong? _pendingHostLobbyId = null;
    private static ulong? _pendingHostSteamId = null;
    
    public static ulong? PendingHostLobbyId => _pendingHostLobbyId;
    public static ulong? PendingHostSteamId => _pendingHostSteamId;
    private static Action<ulong>? _rollbackAckCallback = null;

    public static void SetRollbackAckCallback(Action<ulong>? callback)
    {
        _rollbackAckCallback = callback;
        Log.Info("[KKSavePoint] Rollback ack callback set");
    }

    public static void InitializeMultiplayerRollback()
    {
        if (_multiplayerRollbackInitialized) return;

        try
        {
            DetectNetworkRole();
            RegisterInitialGameInfoMessageHandler();
            RegisterLobbySeedChangedMessageHandler();
            _multiplayerRollbackInitialized = true;
            Log.Info($"[KKSavePoint] Multiplayer rollback system initialized. Role: Host={_isHost}, Client={_isClient}");
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to initialize multiplayer rollback: {ex}");
        }
    }

    /// <summary>
    /// 重新注册消息处理器（用于客机重新连接后）
    /// </summary>
    public static void ReRegisterMessageHandlers(object? netService = null)
    {
        try
        {
            Log.Info("[KKSavePoint] Re-registering message handlers for reconnected client...");
            RegisterInitialGameInfoMessageHandler(netService);
            RegisterLobbySeedChangedMessageHandler(netService);
            Log.Info("[KKSavePoint] Message handlers re-registered successfully");
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to re-register message handlers: {ex}");
        }
    }

    private static void RegisterInitialGameInfoMessageHandler(object? netService = null)
    {
        try
        {
            if (netService == null)
            {
                netService = GetNetServiceFromRunManager();
            }
            
            if (netService == null)
            {
                Log.Warn("[KKSavePoint] Cannot register InitialGameInfoMessage handler: NetService is null");
                return;
            }

            MessageHandlerDelegate<InitialGameInfoMessage> handler = (message, senderId) =>
            {
                _lastSessionState = message.sessionState;
                Log.Info($"[KKSavePoint] Received InitialGameInfoMessage. State: {message.sessionState}, GameMode: {message.gameMode}");
            };

            // 找到泛型的 RegisterMessageHandler 方法
            var methods = netService.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
            System.Reflection.MethodInfo? registerMethod = null;
            foreach (var method in methods)
            {
                if (method.Name == "RegisterMessageHandler" && method.IsGenericMethodDefinition)
                {
                    registerMethod = method.MakeGenericMethod(typeof(InitialGameInfoMessage));
                    break;
                }
            }

            if (registerMethod == null)
            {
                Log.Warn("[KKSavePoint] Cannot register InitialGameInfoMessage handler: RegisterMessageHandler method not found");
                return;
            }

            registerMethod.Invoke(netService, new object[] { handler });
            Log.Info("[KKSavePoint] Registered InitialGameInfoMessage handler");
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to register InitialGameInfoMessage handler: {ex}");
        }
    }

    private static void RegisterLobbySeedChangedMessageHandler(object? netService = null)
    {
        try
        {
            if (netService == null)
            {
                netService = GetNetServiceFromRunManager();
            }
            
            if (netService == null)
            {
                Log.Warn("[KKSavePoint] Cannot register LobbySeedChangedMessage handler: NetService is null");
                return;
            }

            MessageHandlerDelegate<LobbySeedChangedMessage> handler = (message, senderId) =>
            {
                Log.Info($"[KKSavePoint] Received LobbySeedChangedMessage. Seed: {message.seed}, SenderId: {senderId}");

                // 检查是否是回滚消息
                if (message.seed == "KK_SAVEPOINT_ROLLBACK_REQUEST")
                {
                    Log.Info("[KKSavePoint] Detected rollback request from host!");
                    _pendingRollbackFromHost = true;

                    // 保存 senderId (主机SteamId) 
                    _pendingHostSteamId = senderId;
                    Log.Info($"[KKSavePoint] Saved host SteamId from senderId: {senderId}");

                    // 尝试获取当前的 LobbyId
                    try
                    {
                        var lobbyIdProperty = netService.GetType().GetProperty("LobbyId", BindingFlags.Public | BindingFlags.Instance);
                        if (lobbyIdProperty != null)
                        {
                            var lobbyId = lobbyIdProperty.GetValue(netService);
                            if (lobbyId is ulong ulongLobbyId)
                            {
                                _pendingHostLobbyId = ulongLobbyId;
                                Log.Info($"[KKSavePoint] Saved current LobbyId: {ulongLobbyId}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"[KKSavePoint] Failed to get LobbyId: {ex.Message}");
                    }

                    // 发送确认给主机
                    Log.Info("[KKSavePoint] Sending rollback acknowledgment to host..."); _shouldJoin = true;
                    SendRollbackAckToHost(netService, senderId);

                }
                // 检查是否是确认消息
                else if (message.seed == "KK_SAVEPOINT_ROLLBACK_ACK")
                {
                    Log.Info($"[KKSavePoint] Received rollback acknowledgment from {senderId}");
                    _rollbackAckCallback?.Invoke(senderId);
                }
            };

            // 找到泛型的 RegisterMessageHandler 方法
            var methods = netService.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
            System.Reflection.MethodInfo? registerMethod = null;
            foreach (var method in methods)
            {
                if (method.Name == "RegisterMessageHandler" && method.IsGenericMethodDefinition)
                {
                    registerMethod = method.MakeGenericMethod(typeof(LobbySeedChangedMessage));
                    break;
                }
            }

            if (registerMethod == null)
            {
                Log.Warn("[KKSavePoint] Cannot register LobbySeedChangedMessage handler: RegisterMessageHandler method not found");
                return;
            }

            registerMethod.Invoke(netService, new object[] { handler });
            Log.Info("[KKSavePoint] Registered LobbySeedChangedMessage handler");
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to register LobbySeedChangedMessage handler: {ex}");
        }
    }

    private static void SendRollbackAckToHost(object netService, ulong hostId)
    {
        try
        {
            var ackMessage = new LobbySeedChangedMessage
            {
                seed = "KK_SAVEPOINT_ROLLBACK_ACK"
            };

            // 查找 SendMessage 方法（带 peerId 参数的版本）
            var methods = netService.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
            System.Reflection.MethodInfo? sendMessageMethod = null;
            foreach (var method in methods)
            {
                if (method.Name == "SendMessage" && method.IsGenericMethodDefinition)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length == 2) // message, peerId
                    {
                        sendMessageMethod = method.MakeGenericMethod(typeof(LobbySeedChangedMessage));
                        break;
                    }
                }
            }

            if (sendMessageMethod == null)
            {
                Log.Error("[KKSavePoint] SendMessage<T>(message, peerId) method not found");
                return;
            }

            sendMessageMethod.Invoke(netService, new object[] { ackMessage, hostId });
            Log.Info($"[KKSavePoint] Sent rollback ack to host {hostId}");
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to send rollback ack: {ex}");
        }
    }

    private static void DetectNetworkRole()
    {
        try
        {
            _cachedNetService = GetNetServiceFromRunManager();
            if (_cachedNetService == null)
            {
                Log.Warn("[KKSavePoint] NetService not found, assuming single player or early state");
                _isHost = false;
                _isClient = false;
                return;
            }

            var typeProp = _cachedNetService.GetType().GetProperty("Type", BindingFlags.Public | BindingFlags.Instance);
            if (typeProp != null)
            {
                var netType = typeProp.GetValue(_cachedNetService);
                var typeStr = netType?.ToString() ?? "";
                _isHost = typeStr == "Host";
                _isClient = typeStr == "Client";
                Log.Info($"[KKSavePoint] Detected network role: {typeStr}");
            }
            else
            {
                _isHost = false;
                _isClient = false;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Error detecting network role: {ex}");
            _isHost = false;
            _isClient = false;
        }
    }

    private static object? GetNetServiceFromRunManager()
    {
        try
        {
            if (RunManager.Instance == null)
            {
                Log.Warn("[KKSavePoint] RunManager.Instance is null");
                return null;
            }

            var netService = RunManager.Instance.NetService;
            if (netService == null)
            {
                Log.Warn("[KKSavePoint] RunManager.NetService is null");
                return null;
            }

            Log.Info("[KKSavePoint] NetService retrieved from RunManager");
            return netService;
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Error getting NetService from RunManager: {ex}");
            return null;
        }
    }

    public static bool IsHost()
    {
        if (!_multiplayerRollbackInitialized)
        {
            InitializeMultiplayerRollback();
        }
        return _isHost ?? false;
    }

    public static bool IsClient()
    {
        if (!_multiplayerRollbackInitialized)
        {
            InitializeMultiplayerRollback();
        }
        return _isClient ?? false;
    }

    public static bool IsInMultiplayer()
    {
        return IsHost() || IsClient();
    }

    public static object? GetCachedNetService()
    {
        if (_cachedNetService == null)
        {
            InitializeMultiplayerRollback();
        }
        return _cachedNetService;
    }

    public static bool SendRollbackMessage(string savePath)
    {
        try
        {
            var netService = GetCachedNetService();
            if (netService == null)
            {
                Log.Warn("[KKSavePoint] Cannot send rollback message: NetService not available");
                return false;
            }

            _hostRollbackInProgress = true;

            var message = new LobbySeedChangedMessage
            {
                seed = "KK_SAVEPOINT_ROLLBACK_REQUEST"
            };

            var methods = netService.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
            System.Reflection.MethodInfo? sendMessageMethod = null;
            foreach (var method in methods)
            {
                if (method.Name == "SendMessage" && method.IsGenericMethodDefinition)
                {
                    var parameters = method.GetParameters();
                    // 找到广播版本：只有一个参数（消息本身）
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
                return false;
            }

            sendMessageMethod.Invoke(netService, new object[] { message });
            Log.Info($"[KKSavePoint] Rollback notification sent to clients with special seed");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to send rollback message: {ex}");
            return false;
        }
    }

    public static void HandleRollbackRequest(string savePath)
    {
        try
        {
            Log.Info($"[KKSavePoint] Handling rollback request: {savePath}");

            if (IsHost())
            {
                Log.Info("[KKSavePoint] Host received rollback request, broadcasting to clients");
                SendRollbackMessage(savePath);
                
                // 启动 socket 服务器，让客户端可以查询新的 lobby ID
                Log.Info("[KKSavePoint] Starting rollback socket server for client reconnection...");
                RollbackSocketServer.Start();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to handle rollback request: {ex}");
        }
    }

    public struct RollbackRequestMessage
    {
        public string savePath;
    }

    public class MultiplayerRollbackListener : ILoadRunLobbyListener
    {
        public void PlayerConnected(ulong playerId)
        {
            Log.Info($"[KKSavePoint] MultiplayerRollbackListener: Player connected - {playerId}");
        }

        public void RemotePlayerDisconnected(ulong playerId)
        {
            Log.Info($"[KKSavePoint] MultiplayerRollbackListener: Remote player disconnected - {playerId}");
        }

        public System.Threading.Tasks.Task<bool> ShouldAllowRunToBegin()
        {
            Log.Info("[KKSavePoint] MultiplayerRollbackListener: ShouldAllowRunToBegin");
            return System.Threading.Tasks.Task.FromResult(true);
        }

        public void BeginRun()
        {
            Log.Info("[KKSavePoint] MultiplayerRollbackListener: BeginRun");
        }

        public void PlayerReadyChanged(ulong playerId)
        {
            Log.Info($"[KKSavePoint] MultiplayerRollbackListener: Player ready changed - {playerId}");
        }

        public void LocalPlayerDisconnected(NetErrorInfo info)
        {
            Log.Info($"[KKSavePoint] MultiplayerRollbackListener: Local player disconnected - {info}");

            if (_pendingRollbackFromHost && IsClient())
            {
                Log.Info("[KKSavePoint] Detected host-initiated rollback, client will auto rejoin...");
                _pendingRollbackFromHost = false;
                _shouldJoin = true;

                // 如果保存了大厅ID，也保存一下，准备之后使用
                if (_pendingHostLobbyId.HasValue)
                {
                    Log.Info($"[KKSavePoint] Will auto-join lobby: {_pendingHostLobbyId.Value}");
                }
            }
        }
    }

    public static void ReEnterLobbyAsHost(SerializableRun saveData)
    {
        try
        {
            Log.Info("[KKSavePoint] Re-entering lobby as host...");

            if (NGame.Instance == null)
            {
                Log.Error("[KKSavePoint] NGame.Instance is null");
                return;
            }

            var netService = GetCachedNetService();
            if (netService == null)
            {
                Log.Error("[KKSavePoint] No cached NetService available");
                return;
            }

            var lobbyListener = new MultiplayerRollbackListener();
            var loadRunLobbyType = Type.GetType("MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.LoadRunLobby, sts2");
            if (loadRunLobbyType != null)
            {
                var loadRunLobby = Activator.CreateInstance(loadRunLobbyType, netService, lobbyListener, saveData);
                if (loadRunLobby != null)
                {
                    var addLocalHostPlayerMethod = loadRunLobbyType.GetMethod("AddLocalHostPlayer");
                    addLocalHostPlayerMethod?.Invoke(loadRunLobby, null);

                    var setReadyMethod = loadRunLobbyType.GetMethod("SetReady", new[] { typeof(bool) });
                    setReadyMethod?.Invoke(loadRunLobby, new object[] { true });

                    var enterLobbyMethod = NGame.Instance.GetType().GetMethod("EnterLobby", new[] { loadRunLobbyType });
                    if (enterLobbyMethod != null)
                    {
                        enterLobbyMethod.Invoke(NGame.Instance, new[] { loadRunLobby });
                        Log.Info("[KKSavePoint] Successfully re-entered lobby as host");
                    }
                    else
                    {
                        Log.Error("[KKSavePoint] EnterLobby method not found");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to re-enter lobby as host: {ex}");
        }
    }

    public static void ReEnterLobbyAsClient()
    {
        try
        {
            Log.Info("[KKSavePoint] Re-entering lobby as client...");

            if (NGame.Instance == null)
            {
                Log.Error("[KKSavePoint] NGame.Instance is null");
                return;
            }

            var backToMenuMethod = NGame.Instance.GetType().GetMethod("BackToMenu");
            if (backToMenuMethod != null)
            {
                backToMenuMethod.Invoke(NGame.Instance, null);
                Log.Info("[KKSavePoint] Client returned to menu, ready to rejoin lobby");
            }
            else
            {
                Log.Error("[KKSavePoint] BackToMenu method not found");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to re-enter lobby as client: {ex}");
        }
    }


    [HarmonyPatch(typeof(NMultiplayerLoadGameScreen), nameof(NMultiplayerLoadGameScreen.OnSubmenuOpened))]
    public static class NMultiplayerLoadGameScreenOnSubmenuOpenedPatch
    {
        private static readonly FieldInfo _runLobbyField;

        static NMultiplayerLoadGameScreenOnSubmenuOpenedPatch()
        {
            _runLobbyField = typeof(NMultiplayerLoadGameScreen).GetField("_runLobby", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        public static void Postfix(NMultiplayerLoadGameScreen __instance)
        {
            if (AutoReadyState.IsProcessing) return;

            try
            {
                if (!FeatureSettingsStore.Current.EnableSavePoint) return;
                if (AutoReadyState.IsProcessing) return;

                var runLobby = _runLobbyField?.GetValue(__instance) as LoadRunLobby;
                if (runLobby == null)
                {
                    Log.Warn("[KKSavePoint] LoadRunLobby is null in NMultiplayerLoadGameScreen, skipping auto ready");
                    return;
                }

                var netService = runLobby.NetService;
                if (netService == null)
                {
                    Log.Warn("[KKSavePoint] NetService is null, skipping auto ready");
                    return;
                }

                var netServiceType = netService.Type;
                var netId = netService.NetId;
                Log.Info($"[KKSavePoint] NetService.Type: {netServiceType}, NetId: {netId}");

                var isReady = runLobby.IsPlayerReady(netId);
                Log.Info($"[KKSavePoint] Player {netId} current ready state: {isReady}");

                if (isReady)
                {
                    Log.Info("[KKSavePoint] Player already ready, skipping auto ready");
                    return;
                }

                Log.Info("[KKSavePoint] NMultiplayerLoadGameScreen detected - this is a continued game");

                // 客机：直接自动准备
                if (netServiceType == NetGameType.Client)
                {
                    Log.Info("[KKSavePoint] Client detected, re-registering message handlers...");
                    // 先重新注册消息处理器
                    ReRegisterMessageHandlers(netService);
                    
                    Log.Info("[KKSavePoint] Auto setting ready...");
                    AutoReadyState.IsProcessing = true;
                    runLobby.SetReady(true);
                    AutoReadyState.IsProcessing = false;
                    Log.Info($"[KKSavePoint] Client auto ready completed. GameMode: {runLobby.GameMode}");
                    return;
                }

                // 房主：等待所有玩家都进入房间后再自动准备
                if (netServiceType == NetGameType.Host)
                {
                    TryAutoReadyAsHost(__instance, runLobby);
                }
            }
            catch (Exception ex)
            {
                AutoReadyState.IsProcessing = false;
                Log.Error($"[KKSavePoint] Failed to auto set ready on submenu opened: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(NMultiplayerLoadGameScreen), nameof(NMultiplayerLoadGameScreen.PlayerConnected))]
    public static class NMultiplayerLoadGameScreenPlayerConnectedPatch
    {
        private static readonly FieldInfo _runLobbyField;

        static NMultiplayerLoadGameScreenPlayerConnectedPatch()
        {
            _runLobbyField = typeof(NMultiplayerLoadGameScreen).GetField("_runLobby", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        public static void Postfix(NMultiplayerLoadGameScreen __instance, ulong playerId)
        {
            if (AutoReadyState.IsProcessing) return;

            try
            {
                if (!FeatureSettingsStore.Current.EnableSavePoint) return;

                var runLobby = _runLobbyField?.GetValue(__instance) as LoadRunLobby;
                if (runLobby == null) return;

                var netService = runLobby.NetService;
                if (netService == null) return;

                // 只有房主需要在玩家连接时检查
                if (netService.Type != NetGameType.Host) return;

                var netId = netService.NetId;
                var isReady = runLobby.IsPlayerReady(netId);
                if (isReady) return;

                Log.Info($"[KKSavePoint] Player {playerId} connected, checking if host should auto ready...");

                TryAutoReadyAsHost(__instance, runLobby);
            }
            catch (Exception ex)
            {
                AutoReadyState.IsProcessing = false;
                Log.Error($"[KKSavePoint] Failed to handle player connected: {ex}");
            }
        }
    }

    private static class AutoReadyState
    {
        public static bool IsProcessing = false;
    }

    private static void TryAutoReadyAsHost(NMultiplayerLoadGameScreen __instance, LoadRunLobby runLobby)
    {
        if (AutoReadyState.IsProcessing) return;

        int totalPlayersInSave = 0;
        if (runLobby.Run?.Players != null)
        {
            totalPlayersInSave = runLobby.Run.Players.Count;
        }

        int connectedPlayersCount = runLobby.ConnectedPlayerIds.Count;

        Log.Info($"[KKSavePoint] Host check. Players in save: {totalPlayersInSave}, Connected players: {connectedPlayersCount}");

        if (connectedPlayersCount < totalPlayersInSave)
        {
            Log.Info($"[KKSavePoint] Host waiting for all players to join... ({connectedPlayersCount}/{totalPlayersInSave})");
            return;
        }

        Log.Info("[KKSavePoint] All players joined, host auto setting ready...");
        AutoReadyState.IsProcessing = true;
        runLobby.SetReady(true);
        AutoReadyState.IsProcessing = false;
        Log.Info($"[KKSavePoint] Host auto ready completed. GameMode: {runLobby.GameMode}");
    }

    [HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
    public static class NMainMenuReadyPatch
    {
        public static void Postfix(NMainMenu __instance)
        {
            Log.Info("[KKSavePoint] NMainMenu._Ready called, checking auto-navigation...");
            Log.Info($"[KKSavePoint] Flags: _shouldHost={_shouldHost}, _shouldJoin={_shouldJoin}");

            if (_shouldHost)
            {
                AutoEnterHostFromSaveOnGameStart(__instance);
            }
            else if (_shouldJoin)
            {
                AutoEnterJoinHostOnGameStart(__instance);
            }
            else
            {
                Log.Info("[KKSavePoint] No auto-navigation flags set, skipping");
            }
        }
    }

    // 在设置自动导航标志后调用，NMainMenu._Ready 会自动触发自动导航
    public static void ScheduleAutoNavigateToMultiplayer()
    {
        // 不需要异步等待，依赖 NMainMenu._Ready 触发
        Log.Info("[KKSavePoint] ScheduleAutoNavigateToMultiplayer called, will auto-navigate when NMainMenu._Ready");
    }

    // 进入游戏时自动进入 host from save（与重载返回区分开）
    private static void AutoEnterHostFromSaveOnGameStart(NMainMenu __instance)
    {
        try
        {
            Log.Info("[KKSavePoint] AutoEnterHostFromSaveOnGameStart started...");
            Log.Info($"[KKSavePoint] Checking flags: _shouldHost={_shouldHost}");

            // 只有 _shouldHost 为 true 时才执行
            if (!_shouldHost)
            {
                Log.Info("[KKSavePoint] Not hosting, skipping auto navigation");
                return;
            }

            // 检查是否正在导航中，防止重复执行
            if (_navigatingFromMainMenu)
            {
                Log.Warn("[KKSavePoint] Already navigating from main menu, skipping duplicate navigation");
                return;
            }
            _navigatingFromMainMenu = true;

            // 检查是否在主菜单界面
            if (!(__instance is NMainMenu))
            {
                Log.Warn("[KKSavePoint] Not in NMainMenu, skipping multiplayer button click");
                _navigatingFromMainMenu = false;
                return;
            }

            Log.Info("[KKSavePoint] Auto clicking multiplayer button...");

            var multiplayerButtonField = __instance.GetType().GetField("_multiplayerButton", BindingFlags.Instance | BindingFlags.NonPublic);
            if (multiplayerButtonField != null)
            {
                var multiplayerButton = multiplayerButtonField.GetValue(__instance);
                if (multiplayerButton != null)
                {
                    var emitSignalMethod = multiplayerButton.GetType().GetMethod("EmitSignal", new Type[] { typeof(StringName) });
                    if (emitSignalMethod != null)
                    {
                        Log.Info("[KKSavePoint] Emitting Released signal on _multiplayerButton...");
                        emitSignalMethod.Invoke(multiplayerButton, new object[] { NClickableControl.SignalName.Released });
                        Log.Info("[KKSavePoint] Successfully emitted Released signal on _multiplayerButton!");
                    }
                    else
                    {
                        var forceClickMethod = multiplayerButton.GetType().GetMethod("ForceClick", BindingFlags.Instance | BindingFlags.Public);
                        if (forceClickMethod != null)
                        {
                            Log.Info("[KKSavePoint] Calling ForceClick on _multiplayerButton...");
                            forceClickMethod.Invoke(multiplayerButton, null);
                            Log.Info("[KKSavePoint] Successfully clicked _multiplayerButton!");
                        }
                    }
                }
            }

            _navigatingFromMainMenu = false;
        }
        catch (Exception ex)
        {
            _navigatingFromMainMenu = false;
            Log.Error($"[KKSavePoint] Error in AutoEnterHostFromSaveOnGameStart: {ex}");
        }
    }

    // 进入游戏时自动加入房间（与重载返回区分开）
    private static void AutoEnterJoinHostOnGameStart(NMainMenu __instance)
    {
        try
        {
            Log.Info("[KKSavePoint] AutoEnterJoinHostOnGameStart started...");
            Log.Info($"[KKSavePoint] Checking flags: _shouldJoin={_shouldJoin}, _pendingHostLobbyId={_pendingHostLobbyId}");

            // 只有 _shouldJoin 为 true 时才执行
            if (!_shouldJoin)
            {
                Log.Info("[KKSavePoint] Not joining, skipping auto navigation");
                return;
            }

            // 检查是否正在导航中，防止重复执行
            if (_navigatingFromMainMenu)
            {
                Log.Warn("[KKSavePoint] Already navigating from main menu, skipping duplicate navigation");
                return;
            }
            _navigatingFromMainMenu = true;

            // 如果有保存的大厅ID，尝试连接
            if (_pendingHostLobbyId.HasValue || _pendingHostSteamId.HasValue)
            {
                var lobbyIdToTry = _pendingHostLobbyId ?? _pendingHostSteamId;
                var steamIdToTry = _pendingHostSteamId;

                Log.Info($"[KKSavePoint] Auto-joining lobby. LobbyId: {_pendingHostLobbyId}, SteamId: {_pendingHostSteamId}");
                _shouldJoin = false; // 清除标志，防止重复执行

                // 使用 Task 来异步执行连接操作，传递两个值
                TaskHelper.RunSafely(JoinToHostWithFallbackAsync(lobbyIdToTry.Value, steamIdToTry, __instance));
                // _pendingHostLobbyId = null;
                // _pendingHostSteamId = null;
                // _navigatingFromMainMenu = false;
                return;
            }

            // 否则，只点击多人游戏按钮，让用户手动选择
            Log.Info("[KKSavePoint] No lobby ID saved, auto clicking multiplayer button...");
            var multiplayerButtonField = __instance.GetType().GetField("_multiplayerButton", BindingFlags.Instance | BindingFlags.NonPublic);
            if (multiplayerButtonField != null)
            {
                var multiplayerButton = multiplayerButtonField.GetValue(__instance);
                if (multiplayerButton != null)
                {
                    var forceClickMethod = multiplayerButton.GetType().GetMethod("ForceClick", BindingFlags.Instance | BindingFlags.Public);
                    if (forceClickMethod != null)
                    {
                        Log.Info("[KKSavePoint] Calling ForceClick on _multiplayerButton...");
                        forceClickMethod.Invoke(multiplayerButton, null);
                        Log.Info("[KKSavePoint] Successfully clicked _multiplayerButton!");
                    }
                }
            }

            _navigatingFromMainMenu = false;
        }
        catch (Exception ex)
        {
            _navigatingFromMainMenu = false;
            Log.Error($"[KKSavePoint] Error in AutoEnterJoinHostOnGameStart: {ex}");
        }
    }

    // 使用与 SteamJoinCallbackHandler 相同的逻辑来连接主机，先用 SteamId，失败后用 LobbyId 再试
    private static async Task JoinToHostWithFallbackAsync(ulong primaryLobbyId, ulong? fallbackSteamId, NMainMenu mainMenu)
    {
        // 先尝试 SteamId
        if (fallbackSteamId.HasValue)
        {
            Log.Info($"[KKSavePoint] JoinToHostWithFallbackAsync: Trying SteamId: {fallbackSteamId.Value}");
            var success = await JoinToHostAsync(fallbackSteamId.Value, isLobbyId: false, mainMenu);
            if (success)
            {
                Log.Info("[KKSavePoint] JoinToHostWithFallbackAsync: Successfully joined with SteamId!");
                return;
            }
        }

        // SteamId 失败后，尝试 LobbyId
        Log.Info($"[KKSavePoint] JoinToHostWithFallbackAsync: Trying LobbyId: {primaryLobbyId}");
        var lobbySuccess = await JoinToHostAsync(primaryLobbyId, isLobbyId: true, mainMenu);

        if (lobbySuccess)
        {
            Log.Info("[KKSavePoint] JoinToHostWithFallbackAsync: Successfully joined with LobbyId!");
            return;
        }

        // 都失败了，尝试通过 socket 查询主机的新 lobby ID
        Log.Info("[KKSavePoint] JoinToHostWithFallbackAsync: Both Steam attempts failed, trying socket query...");
        await TrySocketQueryAndJoin(mainMenu);
    }

    private static async Task TrySocketQueryAndJoin(NMainMenu mainMenu)
    {
        try
        {
            // 尝试连接到主机查询新的 lobby ID
            var response = await RollbackSocketClient.ConnectToLocalHostAsync();
            
            if (response.Success)
            {
                Log.Info($"[KKSavePoint] Socket query successful! LobbyId: {response.LobbyId}, SteamId: {response.SteamId}");
                
                // 更新保存的 lobby ID 和 Steam ID
                if (!string.IsNullOrEmpty(response.LobbyId) && ulong.TryParse(response.LobbyId, out var newLobbyId))
                {
                    _pendingHostLobbyId = newLobbyId;
                    Log.Info($"[KKSavePoint] Updated pending lobby ID to: {newLobbyId}");
                    await JoinToHostAsync(newLobbyId, isLobbyId: true, mainMenu);
                    return;
                }
                
                if (!string.IsNullOrEmpty(response.SteamId) && ulong.TryParse(response.SteamId, out var newSteamId))
                {
                    _pendingHostSteamId = newSteamId;
                    Log.Info($"[KKSavePoint] Updated pending Steam ID to: {newSteamId}");
                    await JoinToHostAsync(newSteamId, isLobbyId: false, mainMenu);
                    return;
                }
            }
            else
            {
                Log.Warn($"[KKSavePoint] Socket query failed: {response.Message}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Error in socket query: {ex}");
        }
        
        Log.Warn("[KKSavePoint] Socket query also failed, giving up auto-rejoin");
    }

    // 使用与 SteamJoinCallbackHandler 相同的逻辑来连接主机
    private static async Task<bool> JoinToHostAsync(ulong id, bool isLobbyId, NMainMenu mainMenu)
    {
        try
        {
            Log.Info($"[KKSavePoint] JoinToHostAsync started for {(isLobbyId ? "lobby" : "player")}: {id}");

            // 清除所有子菜单
            while (mainMenu.SubmenuStack.Peek() != null)
            {
                mainMenu.SubmenuStack.Pop();
            }

            // 直接使用类型，不需要反射查找
            IClientConnectionInitializer connInitializer;
            if (isLobbyId)
            {
                connInitializer = SteamClientConnectionInitializer.FromLobby(id);
            }
            else
            {
                connInitializer = SteamClientConnectionInitializer.FromPlayer(id);
            }

            if (connInitializer == null)
            {
                Log.Error("[KKSavePoint] Failed to create SteamClientConnectionInitializer");
                return false;
            }

            // 调用 NMainMenu.JoinGame
            Log.Info("[KKSavePoint] Calling NMainMenu.JoinGame...");
            var joinGameMethod = typeof(NMainMenu).GetMethod("JoinGame", BindingFlags.Public | BindingFlags.Instance);
            if (joinGameMethod == null)
            {
                Log.Error("[KKSavePoint] NMainMenu.JoinGame method not found");
                return false;
            }

            var joinTask = joinGameMethod.Invoke(mainMenu, new object[] { connInitializer }) as Task;
            if (joinTask != null)
            {
                await joinTask;
            }

            Log.Info($"[KKSavePoint] JoinToHostAsync completed for {(isLobbyId ? "lobby" : "player")}: {id}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Error in JoinToHostAsync: {ex}");
            return false;
        }
    }


    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMultiplayerSubmenu), "StartHostAsync")]
    public static class NMultiplayerSubmenuStartHostAsyncPatch
    {
        public static void Postfix(Task __result)
        {
            if (!FeatureSettingsStore.Current.EnableSavePoint) return;

            __result.ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    foreach (var ex in t.Exception.InnerExceptions)
                    {
                        if (ex is ObjectDisposedException)
                        {
                            Log.Warn($"[KKSavePoint] StartHostAsync ObjectDisposedException ignored (likely due to menu transition during rollback)");
                        }
                        else
                        {
                            Log.Error($"[KKSavePoint] StartHostAsync exception: {ex}");
                        }
                    }
                }
            });
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.StartRunLobby), "HandleSeedChangedMessage")]
    public static class StartRunLobbySeedChangedPatch
    {
        public static void Postfix(MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.StartRunLobby __instance, LobbySeedChangedMessage message)
        {
            try
            {
                if (!FeatureSettingsStore.Current.EnableSavePoint) return;

                if (message.seed == "KK_SAVEPOINT_ROLLBACK_REQUEST")
                {
                    Log.Info("[KKSavePoint] Received rollback request from host!");

                    if (IsClient())
                    {
                        //Log.Info("[KKSavePoint] Client received rollback request, returning to main menu...");
                        //_pendingRollbackFromHost = true;
                        //_shouldJoin = true;

                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[KKSavePoint] Error in StartRunLobbySeedChangedPatch: {ex}");
            }
        }
    }

    private static void ClickLoadButton(object instance)
    {
        try
        {
            Log.Info("[KKSavePoint] Auto clicking load button (Host from Save)...");
            var loadButtonField = instance.GetType().GetField("_loadButton", BindingFlags.Instance | BindingFlags.NonPublic);
            if (loadButtonField != null)
            {
                var loadButton = loadButtonField.GetValue(instance);
                Log.Info($"[KKSavePoint]  loadButtonField.FieldHandle: {loadButtonField.FieldHandle}");
                if (loadButton != null)
                {
                    var forceClickMethod = loadButton.GetType().GetMethod("ForceClick", BindingFlags.Instance | BindingFlags.Public);
                    if (forceClickMethod != null)
                    {
                        Log.Info("[KKSavePoint] Calling ForceClick on _loadButton...");
                        forceClickMethod.Invoke(loadButton, null);
                        Log.Info("[KKSavePoint] Successfully clicked _loadButton!");
                    }
                    else
                    {
                        Log.Warn("[KKSavePoint] Neither EmitSignal nor ForceClick method found on _loadButton");
                    }
                }
                else
                {
                    Log.Warn("[KKSavePoint] _loadButton is null!");
                }
            }
            else
            {
                Log.Warn("[KKSavePoint] _loadButton field not found!");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Error clicking _loadButton: {ex}");
            Log.Error($"[KKSavePoint] Stack trace: {ex.StackTrace}");
        }
    }

    // 注释掉：SaveManager 已经加载了替换过的存档，不需要再手动传递 _pendingHostSaveData
    // [HarmonyPatch(typeof(NMultiplayerLoadGameScreen), nameof(NMultiplayerLoadGameScreen.OnSubmenuOpened))]
    // public static class NMultiplayerLoadGameScreenHostPatch
    // {
    //     public static void Postfix(NMultiplayerLoadGameScreen __instance)
    //     {
    //         try
    //         {
    //             if (!FeatureSettingsStore.Current.EnableSavePoint) return;
    //
    //             if (_pendingHostSaveData == null)
    //             {
    //                 Log.Info("[KKSavePoint] No pending host save data, skipping auto start...");
    //                 return;
    //             }
    //
    //             Log.Info("[KKSavePoint] NMultiplayerLoadGameScreen opened, auto starting host with saved data...");
    //
    //             var startHostMethod = __instance.GetType().GetMethod("StartHost", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(SerializableRun) }, null);
    //             if (startHostMethod != null)
    //             {
    //                 Log.Info($"[KKSavePoint] Found StartHost method, invoking with save data. Players: {_pendingHostSaveData.Players?.Count ?? 0}");
    //                 startHostMethod.Invoke(__instance, new object[] { _pendingHostSaveData });
    //                 _pendingHostSaveData = null;
    //                 _autoNavigateToHostFromSave = false;
    //                 Log.Info("[KKSavePoint] Successfully auto started host from save!");
    //             }
    //             else
    //             {
    //                 Log.Warn("[KKSavePoint] StartHost method not found!");
    //             }
    //         }
    //         catch (Exception ex)
    //         {
    //             Log.Error($"[KKSavePoint] Error in NMultiplayerLoadGameScreenHostPatch: {ex}");
    //         }
    //     }
    // }

    private static System.Threading.Tasks.Task? _autoJoinTask = null;
    private static int _autoJoinAttempts = 0;
    private static readonly int MaxAutoJoinAttempts = 10;

    [HarmonyPatch(typeof(NJoinFriendScreen), nameof(NJoinFriendScreen.OnSubmenuOpened))]
    public static class NJoinFriendScreenOnSubmenuOpenedPatch
    {
        public static void Postfix(NJoinFriendScreen __instance)
        {
            try
            {
                if (!FeatureSettingsStore.Current.EnableSavePoint) return;

                if (_pendingRollbackFromHost || _shouldJoin)
                {
                    Log.Info("[KKSavePoint] NJoinFriendScreen opened, starting auto join for rollback...");
                    _pendingRollbackFromHost = false;
                    _shouldJoin = false;
                    _autoJoinAttempts = 0;

                    // 标志位：是否已经点击过刷新按钮
                    var refreshClicked = false;

                    var timer = new Godot.Timer();
                    timer.WaitTime = 2.0f;
                    timer.OneShot = false;
                    __instance.AddChild(timer);
                    timer.Connect(Godot.Timer.SignalName.Timeout, Callable.From(() =>
                    {
                        _autoJoinAttempts++;
                        Log.Info($"[KKSavePoint] Auto join attempt {_autoJoinAttempts}/{MaxAutoJoinAttempts}...");

                        try
                        {
                            // 检查 Timer 是否仍然有效
                            if (!GodotObject.IsInstanceValid(timer))
                            {
                                Log.Warn("[KKSavePoint] Timer is no longer valid, skipping.");
                                return;
                            }

                            // 第一步：点击刷新按钮
                            if (!refreshClicked)
                            {
                                Log.Info("[KKSavePoint] Clicking refresh button...");
                                var refreshButtonField = __instance.GetType().GetField("_refreshButton", BindingFlags.Instance | BindingFlags.NonPublic);
                                if (refreshButtonField != null)
                                {
                                    var refreshButton = refreshButtonField.GetValue(__instance);
                                    if (refreshButton != null)
                                    {
                                        var refreshButtonClickedMethod = __instance.GetType().GetMethod("RefreshButtonClicked", BindingFlags.Instance | BindingFlags.NonPublic);
                                        if (refreshButtonClickedMethod != null)
                                        {
                                            refreshButtonClickedMethod.Invoke(__instance, null);
                                            Log.Info("[KKSavePoint] Refresh button clicked via RefreshButtonClicked method!");
                                        }
                                    }
                                }

                                refreshClicked = true;
                                // 200ms 后执行第二步
                                timer.WaitTime = 0.2f;
                                return;
                            }

                            // 第二步：查找并点击按钮
                            // 查找按钮容器
                            var buttonContainerField = __instance.GetType().GetField("_buttonContainer", BindingFlags.Instance | BindingFlags.NonPublic);
                            if (buttonContainerField != null)
                            {
                                var buttonContainer = buttonContainerField.GetValue(__instance) as Godot.Node;
                                if (buttonContainer != null && buttonContainer.GetChildCount() > 0)
                                {
                                    Node? targetButton = null;

                                    Log.Info($"[KKSavePoint] Found {buttonContainer.GetChildCount()} buttons in container");

                                    // 如果有保存的 Steam ID，先尝试查找匹配的按钮
                                    if (_pendingHostSteamId.HasValue)
                                    {
                                        Log.Info($"[KKSavePoint] Looking for button with Steam ID {_pendingHostSteamId.Value}...");
                                        foreach (var child in buttonContainer.GetChildren())
                                        {
                                            var childNode = child as Node;
                                            if (childNode != null)
                                            {
                                                // 检查是否是 NJoinFriendButton
                                                var buttonTypeName = childNode.GetType().Name;
                                                if (buttonTypeName.Contains("NJoinFriendButton"))
                                                {
                                                    // 直接访问 PlayerId 属性
                                                    var playerIdProperty = childNode.GetType().GetProperty("PlayerId", BindingFlags.Public | BindingFlags.Instance);
                                                    if (playerIdProperty != null)
                                                    {
                                                        var playerId = playerIdProperty.GetValue(childNode);
                                                        Log.Info($"[KKSavePoint] Found button with PlayerId: {playerId}");
                                                        if (playerId is ulong ulongId && ulongId == _pendingHostSteamId.Value)
                                                        {
                                                            targetButton = childNode;
                                                            Log.Info("[KKSavePoint] Found button with matching Steam ID!");
                                                            break;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    // 如果没找到匹配的，就用第一个按钮
                                    if (targetButton == null)
                                    {
                                        targetButton = buttonContainer.GetChild(0);
                                        Log.Info("[KKSavePoint] No matching button found, using first button.");
                                    }

                                    if (targetButton != null)
                                    {
                                        Log.Info("[KKSavePoint] Found friend button, triggering join...");
                                        // 先尝试 ForceClick 方法
                                        var forceClickMethod = targetButton.GetType().GetMethod("ForceClick", BindingFlags.Instance | BindingFlags.Public);
                                        if (forceClickMethod != null)
                                        {
                                            Log.Info("[KKSavePoint] Calling ForceClick method...");
                                            forceClickMethod.Invoke(targetButton, null);
                                            Log.Info("[KKSavePoint] Auto join triggered via ForceClick method!");
                                        }
                                        else
                                        {
                                            // 回退到 NClickableControl 的 Released 信号
                                            Log.Info("[KKSavePoint] ForceClick not found, trying NClickableControl signal...");
                                            var releasedSignalField = targetButton.GetType().GetProperty(nameof(NClickableControl.SignalName.Released), BindingFlags.Static | BindingFlags.Public);
                                            if (releasedSignalField != null)
                                            {
                                                var signalName = (StringName)releasedSignalField.GetValue(null);
                                                targetButton.EmitSignal(signalName);
                                                Log.Info("[KKSavePoint] Auto join triggered via NClickableControl signal!");
                                            }
                                            else
                                            {
                                                Log.Warn("[KKSavePoint] Neither ForceClick nor NClickableControl found!");
                                            }
                                        }
                                        
                                        // 停止并清理 Timer
                                        if (GodotObject.IsInstanceValid(timer))
                                        {
                                            timer.Stop();
                                            timer.QueueFree();
                                        }
                                        return;
                                    }
                                }
                            }

                            // 如果没找到按钮，重置标志位，下一次尝试再刷新
                            refreshClicked = false;
                            timer.WaitTime = 2.0f;
                        }
                        catch (Exception ex)
                        {
                            Log.Warn($"[KKSavePoint] Auto join attempt failed: {ex.Message}");
                            // 出错时也重置标志位
                            refreshClicked = false;
                            timer.WaitTime = 2.0f;
                        }

                        if (_autoJoinAttempts >= MaxAutoJoinAttempts)
                        {
                            Log.Warn("[KKSavePoint] Auto join timed out, no friend room found");
                            if (GodotObject.IsInstanceValid(timer))
                            {
                                timer.Stop();
                                timer.QueueFree();
                            }
                        }
                    }));

                    timer.Start();
                    _pendingHostLobbyId = null;
                    _pendingHostSteamId = null;
                    _navigatingFromMainMenu = false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[KKSavePoint] Error in NJoinFriendScreenOnSubmenuOpenedPatch: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(NFtueConfirmButton), "_Ready")]
    public class NFtueConfirmButtonPatch
    {
        private static async void Postfix(NFtueConfirmButton __instance)
        {
            // 等待一帧确保按钮完全初始化
            await __instance.ToSignal(__instance.GetTree(), "process_frame");

            if (GodotObject.IsInstanceValid(__instance))
            {
                Log.Info("[KKSavePoint] Auto-clicking FTUE confirm button...");
                __instance.ForceClick();
            }
        }
    }


}
