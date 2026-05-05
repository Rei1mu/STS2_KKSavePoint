using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using Godot;
using HarmonyLib;
using KKSavePoint.Core;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Localization;


namespace KKSavePoint.Features;

public static partial class SavePointFeature
{
    internal static void OnTurnStarted(CombatState state)
    {
        Log.Info($"[KKSavePoint] OnTurnStarted called - CurrentSide: {state.CurrentSide}, RoundNumber: {state.RoundNumber}");

        // 处理逐步回放逻辑 - 在 OnTurnStarted 中执行
        if (SavePointFeature._isReplaying && state.CurrentSide == CombatSide.Player && !SavePointFeature._isReplayingAsync)
        {
            int currentRound = state.RoundNumber;
            Log.Info($"[KKSavePoint] Replay mode active, current round: {currentRound}, target: {SavePointFeature._targetReplayTurn}, queue index: {SavePointFeature._replayQueueIndex}");

            // 使用队列索引来跟踪进度，而不是依赖回合号
            if (SavePointFeature._replayQueueIndex >= SavePointFeature._replayQueue.Count)
            {
                Log.Info($"[KKSavePoint] Replay queue exhausted ({SavePointFeature._replayQueueIndex} >= {SavePointFeature._replayQueue.Count}), stopping replay mode");
                SavePointFeature._isReplaying = false;
                SavePointFeature._replayQueue.Clear();
                SavePointFeature._replayQueueIndex = 0;
                SavePointFeature._targetReplayTurn = 0;
                return;
            }

            // 获取当前队列索引对应的回放数据
            SavePointFeature.TurnPlaybackData? turnData = SavePointFeature._replayQueue[SavePointFeature._replayQueueIndex];

            if (turnData != null)
            {
                // 关键修复：只有当队列中的回合号与当前游戏回合号匹配时才处理
                // 这样可以确保每回合只处理一个回合的回放，而不是在同一次 OnTurnStarted 中处理多个
                int queuedTurnNumber = turnData.TurnNumber;
                if (queuedTurnNumber != currentRound)
                {
                    Log.Info($"[KKSavePoint] Queued turn {queuedTurnNumber} doesn't match current round {currentRound}, waiting for correct round...");
                    return;
                }

                Log.Info($"[KKSavePoint] Replaying turn {turnData.TurnNumber} (queue index {SavePointFeature._replayQueueIndex}) with {turnData.CardPlays.Count} card plays in OnTurnStarted");

                // 启动异步回放，不阻塞主线程
                _ = SavePointFeature.ReplayTurnCardsAsync(turnData);
            }
            else
            {
                Log.Warn($"[KKSavePoint] Turn data is null at index {SavePointFeature._replayQueueIndex}, stopping replay");
                SavePointFeature._isReplaying = false;
                SavePointFeature._replayQueue.Clear();
                SavePointFeature._replayQueueIndex = 0;
                SavePointFeature._targetReplayTurn = 0;
            }
            return;
        }

        // 现在在 SetUpCombat 已经清空了，这里不再清空

        if (!FeatureSettingsStore.Current.EnableSavePoint)
        {
            Log.Info("[KKSavePoint] SavePoint feature is disabled");
            return;
        }

        // 如果正在回放，不保存记录
        if (SavePointFeature._isReplaying)
        {
            Log.Info("[KKSavePoint] Is replaying, skipping turn record");
            return;
        }

        if (state.CurrentSide == CombatSide.Player)
        {
            int currentRound = state.RoundNumber;

            // 防重复检查
            if (currentRound == SavePointFeature._lastRecordedTurnNumber)
            {
                Log.Info($"[KKSavePoint] Skipping duplicate turn record for round {currentRound}");
                return;
            }
            SavePointFeature._lastRecordedTurnNumber = currentRound;
            Log.Info($"[KKSavePoint] Player turn detected - Round {currentRound}");

            int floor = 0;
            string roomName = "Combat";

            try
            {
                var runStateObj = state.RunState as dynamic;
                if (runStateObj != null)
                {
                    var map = runStateObj.Map;
                    if (map != null)
                    {
                        try { if (map.CurrentFloor > 0) floor = map.CurrentFloor; } catch { }
                        try { if (map.Floor > 0) floor = map.Floor; } catch { }
                        try { if (map.ActFloor > 0) floor = map.ActFloor; } catch { }
                    }

                    try { if (runStateObj.CurrentFloor > 0) floor = runStateObj.CurrentFloor; } catch { }
                    try { if (runStateObj.Floor > 0) floor = runStateObj.Floor; } catch { }
                }

                if (state.RunState.CurrentRoom != null)
                {
                    var roomObj = state.RunState.CurrentRoom as dynamic;
                    try { roomName = roomObj.Title.GetRawText(); } catch { }
                }
            }
            catch { }

            Log.Info($"[KKSavePoint] Recording turn save point - Room: {roomName}, Floor: {floor}, Turn: {state.RoundNumber}");
            // 延迟执行，让游戏先保存一次 current_run.save，这样我们能拿到最新的状态
            _ = Task.Run(async () =>
            {
                await Task.Delay(500); // 延迟 500ms
                Log.Info($"[KKSavePoint] Executing delayed RecordTurnSavePoint for Turn: {state.RoundNumber}");
                SavePointFeature.RecordTurnSavePoint(roomName, floor, state.RoundNumber);
            });
        }
        else
        {
            Log.Info($"[KKSavePoint] Not player turn, skipping - CurrentSide: {state.CurrentSide}");
        }
    }

    internal static void OnCombatEnded(CombatRoom room)
    {
        try
        {
            CombatManager.Instance.TurnStarted -= OnTurnStarted;
            CombatManager.Instance.CombatEnded -= OnCombatEnded;
        }
        catch { }
    }

    public static void RecordTurnSavePoint(string roomName, int floor, int turnNumber)
    {
        if (!FeatureSettingsStore.Current.EnableSavePoint)
        {
            Log.Info("[KKSavePoint] RecordTurnSavePoint skipped: feature disabled");
            return;
        }

        if (_isLoading)
        {
            Log.Info("[KKSavePoint] RecordTurnSavePoint skipped: _isLoading is true");
            return;
        }

        Initialize();

        try
        {
            var runState = RunManager.Instance.DebugOnlyGetState();

            if (runState == null)
            {
                Log.Info("[KKSavePoint] RecordTurnSavePoint skipped: runState is null");
                return;
            }

            var localPlayer = LocalContext.GetMe(runState);

            if (localPlayer == null)
            {
                Log.Info("[KKSavePoint] RecordTurnSavePoint skipped: localPlayer is null");
                return;
            }

            // 和 GetGameSavePath 用完全一样的方式，确保查找和加载时路径一致
            bool isMultiplayerMode = runState.Players.Count > 1;
            string? gameSavePath = GetGameSavePath(isMultiplayerMode);

            if (string.IsNullOrEmpty(gameSavePath) || !File.Exists(gameSavePath))
            {
                Log.Info($"[KKSavePoint] RecordTurnSavePoint skipped: gameSavePath is null or file not exists");
                return;
            }

            Log.Info($"[KKSavePoint] Saving turn record for Turn {turnNumber}");

            var saveFileContent = File.ReadAllText(gameSavePath);
            var saveFileName = $"turnsavepoint_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.json";
            var savePointPath = Path.Combine(_savePointsDir, saveFileName);
            File.Copy(gameSavePath, savePointPath, true);
            Log.Info($"[KKSavePoint] Copied save to: {savePointPath}");

            var hash = GenerateShortHash(saveFileContent);
            var characterName = GetCharacterName(runState);
            var difficulty = GetDifficulty(runState);

            if (floor == 0)
            {
                floor = GetFloorFromPlayer(localPlayer);
            }

            var isMultiplayer = runState.Players.Count > 1;
            var playerCount = runState.Players.Count;

            var record = new SavePointRecord
            {
                Index = _turnRecords.Count + 1,
                Hash = hash,
                RoomName = roomName,
                CharacterName = characterName,
                Difficulty = difficulty,
                Floor = floor,
                Gold = localPlayer.Gold,
                CurrentHp = (int)localPlayer.Creature.CurrentHp,
                MaxHp = (int)localPlayer.Creature.MaxHp,
                Timestamp = DateTime.Now,
                SaveFileName = saveFileName,
                IsMultiplayer = isMultiplayer,
                PlayerCount = playerCount,

                IsTurnSavePoint = true,
                TurnNumber = turnNumber,
                CombatRoomName = roomName
            };

            lock (_turnLock)
            {
                _turnRecords.Add(record);
            }

            // 回合记录不需要保存到文件，只存在内存中
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to record turn save point: {ex}");
        }
    }

    public static void ClearTurnRecords()
    {
        lock (_turnLock)
        {
            Log.Info($"[KKSavePoint] ClearTurnRecords called, clearing {_turnRecords.Count} turn records and {_turnPlaybackData.Count} playback data");
            // 清理旧的回合记录文件
            foreach (var record in _turnRecords)
            {
                if (!string.IsNullOrEmpty(record.SaveFileName))
                {
                    try
                    {
                        var filePath = Path.Combine(_savePointsDir, record.SaveFileName);
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                        }
                    }
                    catch { }
                }
            }
            _turnRecords.Clear();
            _turnPlaybackData.Clear();
            _lastRecordedTurnNumber = -1;
            _replayQueueIndex = 0;
            Log.Info($"[KKSavePoint] ClearTurnRecords complete");
        }
    }
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetUpCombat))]
public static class SavePointCombatSetUpPatch
{
    public static void Postfix(CombatState state)
    {
        if (!FeatureSettingsStore.Current.EnableSavePoint) return;

        Log.Info($"[KKSavePoint] SetUpCombat called, _turnRecordLoadCount = {SavePointFeature._turnRecordLoadCount}");

        // 新战斗开始时，确保清空旧的回合计录
        if (!SavePointFeature._isLoadingTurnRecordFlag && !SavePointFeature._isReplaying)
        {
            Log.Info($"[KKSavePoint] Clearing turn records in SetUpCombat (new combat)");
            SavePointFeature.ClearTurnRecords();
        }

        CombatManager.Instance.TurnStarted += SavePointFeature.OnTurnStarted;
        CombatManager.Instance.CombatEnded += SavePointFeature.OnCombatEnded;
    }

}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterTurnEnd))]
public static class HookAfterTurnEndPatch
{
    public static void Postfix(CombatState combatState, CombatSide side)
    {
        if (!FeatureSettingsStore.Current.EnableSavePoint) return;

        // 如果正在回放，不保存记录
        if (SavePointFeature._isReplaying)
        {
            Log.Info("[KKSavePoint] Is replaying, skipping turn playback save");
            return;
        }

        try
        {
            if (side == CombatSide.Player)
            {
                var player = LocalContext.GetMe(combatState.RunState);
                int playerHpAfterTurn = player != null ? (int)player.Creature.CurrentHp : 0;
                int playerBlockAfterTurn = player != null ? (int)player.Creature.Block : 0;

                var monsterHpAfterTurn = new Dictionary<string, int>();
                if (combatState.Enemies != null)
                {
                    foreach (dynamic enemy in combatState.Enemies)
                    {
                        if (enemy != null)
                        {
                            try
                            {
                                string enemyId = enemy.Id?.ToString() ?? enemy.GetHashCode().ToString();
                                int hp = (int)(enemy.Creature?.CurrentHp ?? 0);
                                monsterHpAfterTurn[enemyId] = hp;
                            }
                            catch
                            {
                                monsterHpAfterTurn[enemy.GetHashCode().ToString()] = 0;
                            }
                        }
                    }
                }

                var turnData = new SavePointFeature.TurnPlaybackData
                {
                    TurnNumber = combatState.RoundNumber,
                    CardPlays = new List<SavePointFeature.CardPlayRecord>(SavePointFeature._currentTurnCardPlays),
                    PlayerHpBeforeTurn = SavePointFeature._playerHpBeforeTurn,
                    PlayerHpAfterTurn = playerHpAfterTurn,
                    BlockBeforeTurn = SavePointFeature._playerBlockBeforeTurn,
                    BlockAfterTurn = playerBlockAfterTurn,
                    MonsterHpBeforeTurn = new Dictionary<string, int>(SavePointFeature._monsterHpBeforeTurn),
                    MonsterHpAfterTurn = monsterHpAfterTurn,
                    Gold = player?.Gold ?? 0,
                    Timestamp = DateTime.Now
                };

                SavePointFeature._turnPlaybackData.Add(turnData);
                Log.Info($"[KKSavePoint] Turn saved: Turn {turnData.TurnNumber}, Card plays: {turnData.CardPlays.Count}");

                SavePointFeature._currentTurnCardPlays.Clear();
                SavePointFeature._currentActionIndex = 0;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to save turn playback: {ex}");
        }
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class HookAfterPlayerTurnStartPatch
{
    public static void Postfix(CombatState combatState, dynamic choiceContext, dynamic player)
    {
        if (!FeatureSettingsStore.Current.EnableSavePoint) return;

        try
        {
            SavePointFeature._playerHpBeforeTurn = (int)player.Creature.CurrentHp;
            SavePointFeature._playerBlockBeforeTurn = (int)player.Creature.Block;
            SavePointFeature._currentActionIndex = 0;

            SavePointFeature._monsterHpBeforeTurn.Clear();
            if (combatState.Enemies != null)
            {
                foreach (dynamic enemy in combatState.Enemies)
                {
                    if (enemy != null)
                    {
                        try
                        {
                            string enemyId = enemy.Id?.ToString() ?? enemy.GetHashCode().ToString();
                            int hp = (int)(enemy.Creature?.CurrentHp ?? 0);
                            SavePointFeature._monsterHpBeforeTurn[enemyId] = hp;
                        }
                        catch
                        {
                            SavePointFeature._monsterHpBeforeTurn[enemy.GetHashCode().ToString()] = 0;
                        }
                    }
                }
            }

            Log.Info($"[KKSavePoint] Turn start recorded: HP {SavePointFeature._playerHpBeforeTurn}, Block {SavePointFeature._playerBlockBeforeTurn}");
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to record turn start: {ex}");
        }
    }
}
