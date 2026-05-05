using System;
using System.Reflection;
using HarmonyLib;
using KKSavePoint.Core;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;

namespace KKSavePoint.Features;

public partial class SavePointFeature
{
    private static bool _multiplayerRollbackInitialized = false;
    private static bool? _isHost = null;
    private static bool? _isClient = null;
    private static object? _cachedNetService = null;
    private static RunSessionState? _lastSessionState = null;

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

            var messageType = typeof(RollbackRequestMessage);
            var message = Activator.CreateInstance(messageType);
            if (message == null)
            {
                Log.Error("[KKSavePoint] Failed to create RollbackRequestMessage");
                return false;
            }

            var savePathField = messageType.GetField("savePath");
            savePathField?.SetValue(message, savePath);

            var sendMessageMethod = netService.GetType().GetMethod("SendMessage", new[] { messageType });
            if (sendMessageMethod == null)
            {
                Log.Error("[KKSavePoint] SendMessage method not found");
                return false;
            }

            sendMessageMethod.Invoke(netService, new[] { message });
            Log.Info($"[KKSavePoint] Rollback message sent: {savePath}");
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

            var backToMenuMethod = NGame.Instance.GetType().GetMethod("BackToMenu");
            if (backToMenuMethod != null)
            {
                backToMenuMethod.Invoke(NGame.Instance, null);
                Log.Info("[KKSavePoint] Successfully returned to main menu");
            }
            else
            {
                Log.Error("[KKSavePoint] BackToMenu method not found");
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
}
