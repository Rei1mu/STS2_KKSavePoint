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
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
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

    public static void InitializeMultiplayerRollback()
    {
        if (_multiplayerRollbackInitialized) return;

        try
        {
            DetectNetworkRole();
            RegisterInitialGameInfoMessageHandler();
            _multiplayerRollbackInitialized = true;
            Log.Info($"[KKSavePoint] Multiplayer rollback system initialized. Role: Host={_isHost}, Client={_isClient}");
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to initialize multiplayer rollback: {ex}");
        }
    }

    private static void RegisterInitialGameInfoMessageHandler()
    {
        try
        {
            var netService = GetNetServiceFromRunManager();
            if (netService == null)
            {
                Log.Warn("[KKSavePoint] Cannot register InitialGameInfoMessage handler: NetService is null");
                return;
            }

            var registerMethod = netService.GetType().GetMethod("RegisterMessageHandler", new[] { typeof(Action<InitialGameInfoMessage, ulong>) });
            if (registerMethod == null)
            {
                Log.Warn("[KKSavePoint] Cannot register InitialGameInfoMessage handler: RegisterMessageHandler method not found");
                return;
            }

            Action<InitialGameInfoMessage, ulong> handler = (message, senderId) =>
            {
                _lastSessionState = message.sessionState;
                Log.Info($"[KKSavePoint] Received InitialGameInfoMessage. State: {message.sessionState}, GameMode: {message.gameMode}");
            };

            registerMethod.Invoke(netService, new object[] { handler });
            Log.Info("[KKSavePoint] Registered InitialGameInfoMessage handler");
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to register InitialGameInfoMessage handler: {ex}");
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
                DisconnectAndReturnToMainMenu();
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
            var loadRunLobbyType = Type.GetType("MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.LoadRunLobby, Assembly-CSharp");
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

    public static void DisconnectAndReturnToMainMenu()
    {
        try
        {
            Log.Info("[KKSavePoint] Disconnecting Steam connection before returning to menu...");

            var netService = GetCachedNetService();
            if (netService != null)
            {
                var disconnectMethods = netService.GetType().GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                    .Where(m => m.Name == "Disconnect")
                    .ToArray();
                
                if (disconnectMethods.Length > 0)
                {
                    Log.Info($"[KKSavePoint] Found {disconnectMethods.Length} Disconnect method(s):");
                    foreach (var method in disconnectMethods)
                    {
                        var paramsDesc = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name));
                        Log.Info($"[KKSavePoint]   - {method.Name}({paramsDesc}) : {method.ReturnType.Name}");
                    }
                    
                    var noParamMethod = disconnectMethods.FirstOrDefault(m => m.GetParameters().Length == 0);
                    if (noParamMethod != null)
                    {
                        Log.Info("[KKSavePoint] Calling Disconnect() with no parameters...");
                        noParamMethod.Invoke(netService, null);
                        Log.Info("[KKSavePoint] Successfully disconnected Steam connection");
                    }
                    else
                    {
                        foreach (var method in disconnectMethods)
                        {
                            try
                            {
                                var parameters = method.GetParameters();
                                object[] args = new object[parameters.Length];
                                for (int i = 0; i < parameters.Length; i++)
                                {
                                    Log.Info($"[KKSavePoint] Param {i}: Type={parameters[i].ParameterType.FullName}");
                                    if (parameters[i].ParameterType.FullName == "MegaCrit.Sts2.Core.Entities.Multiplayer.NetError")
                                    {
                                        args[i] = MegaCrit.Sts2.Core.Entities.Multiplayer.NetError.Quit;
                                        Log.Info($"[KKSavePoint]   - Set to Quit");
                                    }
                                    else if (parameters[i].ParameterType.IsEnum)
                                    {
                                        args[i] = Activator.CreateInstance(parameters[i].ParameterType);
                                        Log.Info($"[KKSavePoint]   - Enum default: {args[i]}");
                                    }
                                    else if (parameters[i].ParameterType == typeof(bool))
                                    {
                                        args[i] = true;
                                        Log.Info($"[KKSavePoint]   - Set to true");
                                    }
                                    else
                                    {
                                        args[i] = null;
                                        Log.Info($"[KKSavePoint]   - Set to null");
                                    }
                                }
                                var argsDesc = string.Join(", ", args.Select(a => a?.ToString() ?? "null"));
                                Log.Info($"[KKSavePoint] Calling Disconnect({argsDesc})...");
                                method.Invoke(netService, args);
                                Log.Info("[KKSavePoint] Successfully disconnected Steam connection");
                                break;
                            }
                            catch (Exception ex)
                            {
                                Log.Warn($"[KKSavePoint] Failed to call Disconnect overload: {ex.Message}");
                            }
                        }
                    }
                }
                else
                {
                    Log.Warn("[KKSavePoint] Disconnect method not found, skipping...");
                }
            }
            else
            {
                Log.Warn("[KKSavePoint] NetService not available, skipping disconnect...");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[KKSavePoint] Failed to disconnect: {ex.Message}");
        }

        System.Threading.Thread.Sleep(200);

        CloseToMenu();
    }

    public static void CloseToMenu()
    {
        try
        {
            Log.Info("[KKSavePoint] CloseToMenu: Preparing to return to main menu...");

            Log.Info("[KKSavePoint] CloseToMenu: Calling NGame.Instance.ReturnToMainMenu()...");
            _ = NGame.Instance.ReturnToMainMenu();
            Log.Info("[KKSavePoint] CloseToMenu: Successfully called ReturnToMainMenu");
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Error in CloseToMenu: {ex}");
        }
    }

    public static void ReturnToMainMenu()
    {
        try
        {
            Log.Info("[KKSavePoint] Returning to main menu...");

            if (NGame.Instance == null)
            {
                Log.Error("[KKSavePoint] NGame.Instance is null");
                return;
            }

            var returnToMenuMethod = NGame.Instance.GetType().GetMethod("ReturnToMainMenu");
            if (returnToMenuMethod != null)
            {
                Log.Info("[KKSavePoint] Found ReturnToMainMenu method");

                if (returnToMenuMethod.IsPublic && returnToMenuMethod.ReturnType == typeof(Task))
                {
                    _ = (Task)returnToMenuMethod.Invoke(NGame.Instance, null);
                    Log.Info("[KKSavePoint] Successfully called ReturnToMainMenu");
                }
                else
                {
                    Log.Warn($"[KKSavePoint] ReturnToMainMenu method has wrong signature: IsPublic={returnToMenuMethod.IsPublic}, ReturnType={returnToMenuMethod.ReturnType}");
                }
            }
            else
            {
                Log.Error("[KKSavePoint] ReturnToMainMenu method not found in NGame");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to return to main menu: {ex}");
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
                    Log.Info("[KKSavePoint] Client detected, auto setting ready...");
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
            Log.Info($"[KKSavePoint] Checking flags: _shouldJoin={_shouldJoin}");

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
            Log.Error($"[KKSavePoint] Error in AutoEnterJoinHostOnGameStart: {ex}");
        }
    }


    [HarmonyPatch]
    public static class NMultiplayerSubmenuPatch
    {
        private static Type? _nMultiplayerSubmenuType = null;

        static NMultiplayerSubmenuPatch()
        {
            try
            {
                _nMultiplayerSubmenuType = typeof(NMainMenu).Assembly.GetType("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMultiplayerSubmenu");
                if (_nMultiplayerSubmenuType == null)
                {
                    Log.Warn("[KKSavePoint] NMultiplayerSubmenu type not found!");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[KKSavePoint] Error in NMultiplayerSubmenuPatch static constructor: {ex}");
            }
        }

        public static MethodBase TargetMethod()
        {
            if (_nMultiplayerSubmenuType == null)
            {
                Log.Warn("[KKSavePoint] NMultiplayerSubmenu type is null, returning null!");
                return null!;
            }

            var readyMethod = _nMultiplayerSubmenuType.GetMethod("_Ready", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var openedMethod = _nMultiplayerSubmenuType.GetMethod("OnSubmenuOpened", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Log.Info($"[KKSavePoint] NMultiplayerSubmenu methods: _Ready={readyMethod != null}, OnSubmenuOpened={openedMethod != null}");

            return readyMethod ?? openedMethod ?? null!;
        }

        public static void Postfix(object __instance)
        {
            if (!FeatureSettingsStore.Current.EnableSavePoint) return;

            try
            {
                Log.Info($"[KKSavePoint] NMultiplayerSubmenu.Postfix called, instance type: {__instance.GetType().FullName}");
                Log.Info($"[KKSavePoint] NMultiplayerSubmenu ready, checking flags: _shouldHost={_shouldHost}, _shouldJoin={_shouldJoin}");

                if (_shouldHost)
                {
                    // 添加延迟以等待菜单转换完成，避免 NLoadingOverlay disposed 错误
                    Log.Info("[KKSavePoint] Scheduling delayed click on load button (waiting for menu transition)...");

                    // 使用 Godot 定时器替代 Task.Run，避免跨线程问题
                    var timer = new Godot.Timer();
                    timer.WaitTime = 0.5f;
                    timer.OneShot = true;
                    timer.Timeout += () =>
                    {
                        GD.Print("[KKSavePoint] Delay complete, now clicking load button...");
                        ClickLoadButton(__instance);
                        _shouldHost = false;  // 清除 host 标志
                        timer.QueueFree();
                    };
                    NGame.Instance.AddChild(timer);
                    timer.Start();
                }
                else if (_shouldJoin)
                {
                    Log.Info("[KKSavePoint] Auto navigating to Join...");
                    var joinButtonMethod = __instance.GetType().GetMethod("OnJoinFriendsPressed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (joinButtonMethod != null)
                    {
                        joinButtonMethod.Invoke(__instance, null);
                        Log.Info("[KKSavePoint] Successfully called OnJoinFriendsPressed!");
                    }
                    else
                    {
                        Log.Warn("[KKSavePoint] OnJoinFriendsPressed method not found!");
                    }

                    _shouldJoin = false;  // 清除 join 标志
                }
            }
            catch (Exception ex)
            {
                _shouldHost = false;  // 出错时也要清除标志
                _shouldJoin = false;
                Log.Error($"[KKSavePoint] Error in NMultiplayerSubmenuPatch.Postfix: {ex}");
            }
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
                        Log.Info("[KKSavePoint] Client received rollback request, returning to main menu...");
                        _pendingRollbackFromHost = true;
                        _shouldJoin = true;
                        DisconnectAndReturnToMainMenu();
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
                if (loadButton != null)
                {
                    var emitSignalMethod = loadButton.GetType().GetMethod("EmitSignal", new Type[] { typeof(StringName) });
                    if (emitSignalMethod != null)
                    {
                        Log.Info("[KKSavePoint] Emitting Released signal on _loadButton...");
                        emitSignalMethod.Invoke(loadButton, new object[] { NClickableControl.SignalName.Released });
                        Log.Info("[KKSavePoint] Successfully emitted Released signal on _loadButton!");
                    }
                    else
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
                            var buttonContainerField = __instance.GetType().GetField("_buttonContainer", BindingFlags.Instance | BindingFlags.NonPublic);
                            if (buttonContainerField != null)
                            {
                                var buttonContainer = buttonContainerField.GetValue(__instance) as Godot.Node;
                                if (buttonContainer != null && buttonContainer.GetChildCount() > 0)
                                {
                                    var firstButton = buttonContainer.GetChild(0);
                                    if (firstButton != null)
                                    {
                                        Log.Info("[KKSavePoint] Found friend button, triggering join...");
                                        var pressedMethod = firstButton.GetType().GetMethod("OnPressed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                        if (pressedMethod != null)
                                        {
                                            pressedMethod.Invoke(firstButton, null);
                                            Log.Info("[KKSavePoint] Auto join triggered via OnPressed method!");
                                        }
                                        else
                                        {
                                            firstButton.EmitSignal(Godot.Button.SignalName.Pressed);
                                            Log.Info("[KKSavePoint] Auto join triggered via signal!");
                                        }
                                        timer.Stop();
                                        timer.QueueFree();
                                        return;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warn($"[KKSavePoint] Auto join attempt failed: {ex.Message}");
                        }

                        if (_autoJoinAttempts >= MaxAutoJoinAttempts)
                        {
                            Log.Warn("[KKSavePoint] Auto join timed out, no friend room found");
                            timer.Stop();
                            timer.QueueFree();
                        }
                    }));

                    timer.Start();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[KKSavePoint] Error in NJoinFriendScreenOnSubmenuOpenedPatch: {ex}");
            }
        }
    }
}
