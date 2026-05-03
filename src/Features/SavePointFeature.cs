using System;
using System.Collections.Generic;
using System.IO;
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



public static class SavePointFeature

{

    private const string SavePointButtonName = "KKSavePointButton";

    private const string SavePointDialogName = "KKSavePointDialog";

    private const int MaxSavePoints = 2000;

    private const string SaveFileName = "kksavepoint_savepoints.json";



    private static readonly List<SavePointRecord> _savePoints = new();
    private static readonly List<SavePointRecord> _turnRecords = new();
    private static readonly object _turnLock = new();

    private static readonly List<CardPlayRecord> _currentTurnCardPlays = new();
    private static readonly List<TurnPlaybackData> _turnPlaybackData = new();
    private static int _currentActionIndex = 0;
    private static TurnPlaybackData? _pendingReplayData = null;
    private static readonly List<TurnPlaybackData> _turnsToReplay = new();
    private static int _playerHpBeforeTurn = 0;
    internal static bool _isReplaying = false;
    internal static int _turnRecordLoadCount = 0;
    internal static bool _isLoadingTurnRecordFlag = false;
    internal static int _lastRecordedTurnNumber = -1;
    internal static int _targetReplayTurn = 0;
    internal static readonly List<TurnPlaybackData> _replayQueue = new();
    internal static int _replayQueueIndex = 0;
    internal static bool _isReplayingAsync = false;
    private static int _playerBlockBeforeTurn = 0;
    private static readonly Dictionary<string, int> _monsterHpBeforeTurn = new();
    private static string? _pendingSelectedCardId = null;
    private static int? _pendingSelectedCardIndex = null;
    private static CardPlayRecord? _pendingCardSelectionRecord = null;
    private static PendingCardSelection? _pendingReplayCardSelection = null;
    private static IDisposable? _currentReplaySelectorScope = null;
    private static ReplayCardSelector? _currentReplaySelector = null;

    private static readonly object _lock = new();

    private static readonly JsonSerializerOptions _jsonOptions = new()

    {

        WriteIndented = true,

        PropertyNameCaseInsensitive = true

    };

    private static bool _initialized = false;

    private static string _saveFilePath = "";

    private static bool _isLoading = false;

    private static string _savePointsDir = "";

    private static Label? _statusLabel = null;



    private static bool IsChineseLocale()
    {
        try
        {
            var locale = TranslationServer.GetLocale();
            return locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ||
                   locale.StartsWith("chinese", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }






    private static class L10n
    {
        public static string Title => IsChineseLocale() ? "存档点列表" : "KKSavePoint - Checkpoints";
        public static string NoSavePoints => IsChineseLocale() ? "暂无存档点。" : "No save points recorded yet.";
        public static string TotalInfo(int count, int max) => IsChineseLocale()
            ? $"共 {count} 个存档点 (上限 {max}) "
            : $"Total: {count} checkpoints (max {max}) ";
        public static string ImportFromClipboard => IsChineseLocale() ? "从剪贴板导入" : "Import from Clipboard";
        public static string ClearAll => IsChineseLocale() ? "清空全部" : "Clear All";
        public static string Close => IsChineseLocale() ? "关闭" : "Close";
        public static string Copy => IsChineseLocale() ? "复制" : "Copy";
        public static string Export => IsChineseLocale() ? "导出" : "Export";
        public static string Delete => IsChineseLocale() ? "删除" : "Delete";
        public static string ViewDeck => IsChineseLocale() ? "查看卡组" : "View Deck";
        public static string DeckTitle => IsChineseLocale() ? "卡组列表" : "Deck List";
        public static string NoCards => IsChineseLocale() ? "没有卡牌" : "No cards";
        public static string DeleteCard => IsChineseLocale() ? "删除" : "Delete";
        public static string CardDeleted => IsChineseLocale() ? "卡牌已删除" : "Card deleted";
        public static string OnlySinglePlayerCanDelete => IsChineseLocale() ? "只有单人模式可以删除卡牌" : "Only single player can delete cards";
        public static string ExportPath => IsChineseLocale() ? "导出路径: 游戏目录" : "Export path: GameFolder";
        public static string TooltipClickToLoad => IsChineseLocale() ? "点击加载此存档点" : "Click to load this checkpoint";
        public static string TooltipCharacter => IsChineseLocale() ? "角色" : "Character";
        public static string TooltipDifficulty => IsChineseLocale() ? "难度" : "Difficulty";
        public static string TooltipSavedAt => IsChineseLocale() ? "保存时间" : "Saved at";
        public static string TooltipCopyToClipboard => IsChineseLocale() ? "复制此存档到剪贴板" : "Copy this checkpoint to clipboard";
        public static string TooltipExportToFile => IsChineseLocale() ? "导出此存档到文件" : "Export this checkpoint to file";
        public static string TooltipDelete => IsChineseLocale() ? "删除此存档点" : "Delete this checkpoint";
        public static string TooltipViewDeck => IsChineseLocale() ? "查看此存档的卡组" : "View deck of this checkpoint";
        public static string FeedbackCopied => IsChineseLocale() ? "存档已复制" : "Checkpoint copied";
        public static string FeedbackImported(int count) => IsChineseLocale() ? $"已导入 {count} 个存档" : $"Imported {count} checkpoints";
        public static string FeedbackNoValidCheckpoints => IsChineseLocale() ? "剪贴板中没有有效存档" : "No valid checkpoints in clipboard";
        public static string FeedbackAllCleared => IsChineseLocale() ? "已清空所有存档" : "All checkpoints cleared";
        public static string FeedbackDeleted => IsChineseLocale() ? "存档已删除" : "Checkpoint deleted";
        public static string FeedbackLoading(string name) => IsChineseLocale() ? $"正在加载: {name}" : $"Loading: {name}";
        public static string FeedbackInvalidCheckpoint => IsChineseLocale() ? "无效的存档点" : "Invalid checkpoint";
        public static string FeedbackNoSaveData => IsChineseLocale() ? "存档没有数据" : "Checkpoint has no save data";
        public static string FeedbackFileNotFound => IsChineseLocale() ? "存档文件未找到" : "Checkpoint file not found";
        public static string FeedbackFailedToLoad => IsChineseLocale() ? "加载存档失败" : "Failed to load checkpoint";
        public static string FeedbackOnlyHostCanRollback => IsChineseLocale() ? "只有房主可以回退" : "Only the host can rollback";
        public static string FeedbackMultiplayerRollbackDone => IsChineseLocale() ? "多人回退完成" : "Multiplayer rollback done";
        public static string FeedbackRollbackSinglePlayer => IsChineseLocale() ? "回退完成(单机模式)" : "Rollback done (single player mode)";
        
        // 回合相关翻译
        public static string TurnSavePointPrefix => IsChineseLocale() ? "回合 " : "Turn ";
        public static string TurnSavePointSuffix => IsChineseLocale() ? " (第{0}层)" : " (Floor {0})";
    }

    public class CardPlayRecord
    {
        public int ActionIndex { get; set; }
        public string CardId { get; set; } = "";
        public string CardName { get; set; } = "";
        public int EnergyCost { get; set; }
        public List<string> TargetIds { get; set; } = new();
        public List<int> TargetPositions { get; set; } = new();
        public string? SelectedCardId { get; set; }
        public int? SelectedCardIndex { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class PendingCardSelection
    {
        public string? CardId { get; set; }
        public int? CardIndex { get; set; }
        public string? TargetCardId { get; set; }
    }

    public class ReplayCardSelector : MegaCrit.Sts2.Core.TestSupport.ICardSelector
    {
        private readonly string? _targetCardId;
        private readonly int? _targetCardIndex;

        public ReplayCardSelector(string? cardId, int? cardIndex)
        {
            _targetCardId = cardId;
            _targetCardIndex = cardIndex;
            Log.Info($"[KKSavePoint] ReplayCardSelector created: CardId={cardId}, CardIndex={cardIndex}");
        }

        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            Log.Info($"[KKSavePoint] ReplayCardSelector.GetSelectedCards called with CardId={_targetCardId}, Index={_targetCardIndex}");

            var optionsList = options.ToList();
            Log.Info($"[KKSavePoint] ReplayCardSelector options count: {optionsList.Count}");

            for (int i = 0; i < optionsList.Count; i++)
            {
                var card = optionsList[i];
                Log.Info($"[KKSavePoint] Option {i}: {card.Id?.Entry}");
            }

            CardModel? selectedCard = null;

            // 首先尝试按 CardId 匹配
            if (!string.IsNullOrEmpty(_targetCardId))
            {
                foreach (var card in optionsList)
                {
                    string? cardIdStr = card.Id?.Entry ?? card.Id?.ToString();
                    if (_targetCardId == cardIdStr)
                    {
                        selectedCard = card;
                        Log.Info($"[KKSavePoint] Selected card by ID: {cardIdStr}");
                        break;
                    }
                }
            }

            // 如果没找到，尝试按索引匹配
            if (selectedCard == null && _targetCardIndex.HasValue)
            {
                int idx = _targetCardIndex.Value;
                if (idx >= 0 && idx < optionsList.Count)
                {
                    selectedCard = optionsList[idx];
                    Log.Info($"[KKSavePoint] Selected card by index {idx}: {selectedCard.Id?.Entry}");
                }
            }

            // 如果都没找到，选择第一张
            if (selectedCard == null && optionsList.Count > 0)
            {
                selectedCard = optionsList[0];
                Log.Info($"[KKSavePoint] No match found, selecting first card: {selectedCard.Id?.Entry}");
            }

            if (selectedCard != null)
            {
                return Task.FromResult<IEnumerable<CardModel>>(new List<CardModel> { selectedCard });
            }

            return Task.FromResult<IEnumerable<CardModel>>(Enumerable.Empty<CardModel>());
        }

        public CardModel? GetSelectedCardReward(IReadOnlyList<MegaCrit.Sts2.Core.Entities.Cards.CardCreationResult> options, IReadOnlyList<MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative> alternatives)
        {
            return null;
        }
    }

    public class TurnPlaybackData
    {
        public int TurnNumber { get; set; }
        public List<CardPlayRecord> CardPlays { get; set; } = new();
        public int PlayerHpBeforeTurn { get; set; }
        public int PlayerHpAfterTurn { get; set; }
        public int BlockBeforeTurn { get; set; }
        public int BlockAfterTurn { get; set; }
        public Dictionary<string, int> MonsterHpBeforeTurn { get; set; } = new();
        public Dictionary<string, int> MonsterHpAfterTurn { get; set; } = new();
        public int Gold { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class SavePointRecord
    {
        public int Index { get; set; }
        public string Hash { get; set; } = "";
        public string RoomName { get; set; } = "";
        public string CharacterName { get; set; } = "";
        public string Difficulty { get; set; } = "";
        public int Floor { get; set; }
        public int Gold { get; set; }
        public int CurrentHp { get; set; }
        public int MaxHp { get; set; }
        public DateTime Timestamp { get; set; }
        public string? SaveFileName { get; set; }
        public bool IsMultiplayer { get; set; }
        public int PlayerCount { get; set; } = 1;
        
        // 回合相关字段
        public bool IsTurnSavePoint { get; set; }
        public int TurnNumber { get; set; }
        public string? CombatRoomName { get; set; }
    }



    private class SavePointFile

    {

        public List<SavePointRecord> SavePoints { get; set; } = new();

        public string RunId { get; set; } = "";

    }



    private static void Initialize()
    {

        if (_initialized) return;

        _initialized = true;



        try
        {

            var userDir = ProjectSettings.GlobalizePath("user://");

            _saveFilePath = Path.Combine(userDir, SaveFileName);

            _savePointsDir = Path.Combine(userDir, "savepoints");

            if (!Directory.Exists(_savePointsDir))

            {

                Directory.CreateDirectory(_savePointsDir);

            }

            LoadFromFile();

            Log.Info($"[KKSavePoint] SavePoint initialized. File: {_saveFilePath}, Dir: {_savePointsDir}");

        }

        catch (Exception ex)
        {

            Log.Error($"[KKSavePoint] Failed to initialize SavePoint: {ex}");

        }

    }

    private static void LoadFromFile()

    {

        try

        {

            if (string.IsNullOrEmpty(_saveFilePath) || !File.Exists(_saveFilePath))

            {

                return;

            }



            var json = File.ReadAllText(_saveFilePath);

            var data = JsonSerializer.Deserialize<SavePointFile>(json, _jsonOptions);

            if (data?.SavePoints != null)

            {

                lock (_lock)

                {

                    _savePoints.Clear();

                    _savePoints.AddRange(data.SavePoints);

                }

                Log.Info($"[KKSavePoint] Loaded {_savePoints.Count} save points from file.");

            }

        }

        catch (Exception ex)

        {

            Log.Error($"[KKSavePoint] Failed to load save points: {ex}");

        }

    }



    private static void SaveToFile()

    {

        try

        {

            var directory = Path.GetDirectoryName(_saveFilePath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))

            {

                Directory.CreateDirectory(directory);

            }



            SavePointFile data;

            lock (_lock)

            {

                data = new SavePointFile

                {

                    SavePoints = new List<SavePointRecord>(_savePoints)

                };

            }



            var json = JsonSerializer.Serialize(data, _jsonOptions);

            File.WriteAllText(_saveFilePath, json);

            Log.Info($"[KKSavePoint] Saved {_savePoints.Count} save points to file.");

        }

        catch (Exception ex)

        {

            Log.Error($"[KKSavePoint] Failed to save save points: {ex}");

        }

    }



    public static void AttachToTopBar(NTopBar topBar)

    {

        Initialize();



        var existingButton = topBar.FindChild(SavePointButtonName, recursive: true, owned: false);

        if (!FeatureSettingsStore.Current.EnableSavePoint)

        {

            Log.Info("[KKSavePoint] SavePoint feature disabled; ensuring button is removed.");

            existingButton?.QueueFree();

            return;

        }



        if (existingButton != null)

        {

            return;

        }



        var button = CreateSavePointButton();

        var goldNode = topBar.Gold;

        var goldParent = goldNode.GetParent();



        if (goldParent is Container container)

        {

            container.AddChild(button);

            var targetIndex = Math.Min(goldNode.GetIndex() + 1, container.GetChildCount() - 1);

            container.MoveChild(button, targetIndex);

        }

        else

        {

            topBar.AddChild(button);

            button.Position = goldNode.Position + new Vector2(goldNode.Size.X + 8f, 0f);

        }



        Log.Info("[KKSavePoint] Added SavePoint button to top bar.");

    }



    private static Button CreateSavePointButton()
    {
        var button = new Button
        {
            Name = SavePointButtonName,
            Text = "KKSavePoint",
            TooltipText = "KKSavePoint: View and load saved checkpoints.",
            CustomMinimumSize = new Vector2(100f, 34f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            FocusMode = Control.FocusModeEnum.None
        };

        button.Pressed += () => OnSavePointButtonPressed(button);
        return button;
    }



    private static void OnSavePointButtonPressed(Button sourceButton)

    {

        Log.Info("[KKSavePoint] SavePoint button pressed.");

        ShowSavePointDialog(sourceButton);

    }



    private static void ShowSavePointDialog(Button? sourceButton = null)

    {

        Initialize();



        Node? gameRoot = sourceButton?.GetTree()?.Root;

        gameRoot ??= NGame.Instance;

        if (gameRoot == null)

        {

            Log.Error("[KKSavePoint] SavePoint failed: could not resolve UI root.");

            ShowFeedback(IsChineseLocale() ? "存档点失败: UI根节点不可用" : "SavePoint failed: UI root unavailable.");

            return;

        }

        var existing = gameRoot.FindChild(SavePointDialogName, recursive: true, owned: false);

        if (existing != null)

        {

            existing.QueueFree();

        }



        var window = new Window
        {
            Name = SavePointDialogName,
            Title = L10n.Title,
            Exclusive = false
        };
        
        var screenSize = DisplayServer.ScreenGetSize();
        var windowWidth = (int)(screenSize.X * 0.5f);
        var windowHeight = (int)(screenSize.Y * 0.6f);
        window.Size = new Vector2I(windowWidth, windowHeight);
        
        var dropTarget = new SavePointDropTarget
        {
            Name = "DropTarget",
            CustomMinimumSize = new Vector2(windowWidth, windowHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        dropTarget.SetDropCallback(files =>
        {
            int totalImported = 0;
            foreach (var file in files)
            {
                totalImported += ImportFromFile(file);
            }
            if (totalImported > 0)
            {
                window.Hide();
                window.QueueFree();
                ShowSavePointDialog();
            }
        });



        var content = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(windowWidth - 60f, 0f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        content.AddThemeConstantOverride("separation", 8);
        
        var scrollContainer = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(windowWidth - 80f, windowHeight - 150f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };



        var listContainer = new VBoxContainer

        {

            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill

        };

        listContainer.AddThemeConstantOverride("separation", 4);



        // Check if current game is multiplayer
        var currentRunState = RunManager.Instance.DebugOnlyGetState();
        bool isCurrentMultiplayer = currentRunState != null && currentRunState.Players.Count > 1;

        int count;

        lock (_lock)

        {

            count = _savePoints.Count;

            int displayedCount = 0;

            if (count == 0)

            {

                var noDataLabel = new Label
                {
                    Text = L10n.NoSavePoints,
                    CustomMinimumSize = new Vector2(0f, 60f),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                listContainer.AddChild(noDataLabel);

            }

            else

            {

                for (int i = count - 1; i >= 0; i--)

                {

                    var record = _savePoints[i];

                    // Filter saves based on current mode
                    if (isCurrentMultiplayer != record.IsMultiplayer)
                    {
                        continue;
                    }

                    displayedCount++;

                    var row = new HBoxContainer

                    {

                        CustomMinimumSize = new Vector2(0f, 36f),

                        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill

                    };

                    row.AddThemeConstantOverride("separation", 4);



                    var hashText = string.IsNullOrEmpty(record.Hash) ? "-------" : record.Hash;
                    var charText = string.IsNullOrEmpty(record.CharacterName) ? "?" : record.CharacterName;
                    var diffText = string.IsNullOrEmpty(record.Difficulty) ? "?" : record.Difficulty;
                    var multiplayerIndicator = record.IsMultiplayer ? "[MP] " : "";
                    
                    string buttonText;
                    string tooltipText;
                    
                    if (record.IsTurnSavePoint)
                    {
                        string suffix = string.Format(L10n.TurnSavePointSuffix, record.Floor);
                        buttonText = $"[{record.Index}][{hashText}][{charText}][{diffText}] {L10n.TurnSavePointPrefix}{record.TurnNumber}{suffix} | HP {record.CurrentHp}/{record.MaxHp} | Gold {record.Gold} | {record.Timestamp:HH:mm:ss}";
                        tooltipText = $"{L10n.TooltipClickToLoad}.\n{L10n.TooltipCharacter}: {record.CharacterName}\n{L10n.TooltipDifficulty}: {record.Difficulty}\n{L10n.TooltipSavedAt} {record.Timestamp:yyyy-MM-dd HH:mm:ss}\nMode: {(record.IsMultiplayer ? $"Multiplayer ({record.PlayerCount} players)" : "Single Player")}\nTurn: {record.TurnNumber}";
                    }
                    else
                    {
                        buttonText = $"[{record.Index}][{hashText}][{charText}][{diffText}][F{record.Floor}] {(record.IsMultiplayer ? $"[MP{record.PlayerCount}] " : "")}{record.RoomName} | HP {record.CurrentHp}/{record.MaxHp} | Gold {record.Gold} | {record.Timestamp:HH:mm:ss}";
                        tooltipText = $"{L10n.TooltipClickToLoad}.\n{L10n.TooltipCharacter}: {record.CharacterName}\n{L10n.TooltipDifficulty}: {record.Difficulty}\n{L10n.TooltipSavedAt} {record.Timestamp:yyyy-MM-dd HH:mm:ss}\nMode: {(record.IsMultiplayer ? $"Multiplayer ({record.PlayerCount} players)" : "Single Player")}";
                    }

                    var itemButton = new Button
                    {
                        Text = buttonText,
                        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                        TooltipText = tooltipText
                    };
                    // 增大字体大小
                    itemButton.AddThemeFontSizeOverride("font_size", 16);
                    
                    if (record.IsTurnSavePoint)
                    {
                        itemButton.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.2f));
                    }

                    int capturedIndex = i;

                    itemButton.Pressed += () =>
                    {
                        window.Hide();
                        window.QueueFree();
                        LoadSavePoint(capturedIndex);
                    };

                    row.AddChild(itemButton);

                    var copyButton = new Button
                    {
                        Text = L10n.Copy,
                        CustomMinimumSize = new Vector2(50f, 36f),
                        TooltipText = L10n.TooltipCopyToClipboard
                    };
                    copyButton.Pressed += () =>
                    {
                        Log.Info($"[KKSavePoint] Copy button pressed for index {capturedIndex}");
                        ExportSingleToClipboard(capturedIndex);
                        ShowFeedback(L10n.FeedbackCopied);
                    };

                    row.AddChild(copyButton);

                    var exportFileButton = new Button
                    {
                        Text = L10n.Export,
                        CustomMinimumSize = new Vector2(60f, 36f),
                        TooltipText = L10n.TooltipExportToFile
                    };
                    exportFileButton.Pressed += () =>
                    {
                        ExportSingleToFile(capturedIndex);
                    };

                    row.AddChild(exportFileButton);

                    var deleteButton = new Button
                    {
                        Text = L10n.Delete,
                        CustomMinimumSize = new Vector2(55f, 36f),
                        TooltipText = L10n.TooltipDelete
                    };
                    deleteButton.Pressed += () =>
                    {
                        DeleteSavePoint(capturedIndex);
                        window.Hide();
                        window.QueueFree();
                        ShowSavePointDialog();
                    };

                    row.AddChild(deleteButton);

                    var viewDeckButton = new Button
                    {
                        Text = L10n.ViewDeck,
                        CustomMinimumSize = new Vector2(80f, 36f),
                        TooltipText = L10n.TooltipViewDeck
                    };
                    viewDeckButton.Pressed += () =>
                    {
                        ShowDeckDialog(capturedIndex);
                    };

                    row.AddChild(viewDeckButton);

                    listContainer.AddChild(row);

                }

                // If no saves match current mode
                if (displayedCount == 0)
                {
                    var noDataLabel = new Label
                    {
                        Text = isCurrentMultiplayer ? (IsChineseLocale() ? "暂无联机存档点。" : "No multiplayer save points recorded yet.") : L10n.NoSavePoints,
                        CustomMinimumSize = new Vector2(0f, 60f),
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    listContainer.AddChild(noDataLabel);
                }

            }

        }



        scrollContainer.AddChild(listContainer);

        content.AddChild(scrollContainer);



        var infoLabel = new Label
        {
            Text = L10n.TotalInfo(count, MaxSavePoints),
            CustomMinimumSize = new Vector2(0f, 24f)
        };
        content.AddChild(infoLabel);

        var buttonRow = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(0f, 36f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        buttonRow.AddThemeConstantOverride("separation", 8);

        var importButton = new Button
        {
            Text = L10n.ImportFromClipboard,
            CustomMinimumSize = new Vector2(150f, 40f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
        };
        importButton.Pressed += () =>
        {
            Log.Info("[KKSavePoint] Import from clipboard button pressed");
            var imported = ImportFromClipboard();
            Log.Info($"[KKSavePoint] Imported {imported} checkpoints from clipboard");
            if (imported > 0)
            {
                ShowFeedback(L10n.FeedbackImported(imported));
                window.Hide();
                window.QueueFree();
                ShowSavePointDialog();
            }
            else
            {
                ShowFeedback(L10n.FeedbackNoValidCheckpoints);
            }
        };
        buttonRow.AddChild(importButton);

        var clearButton = new Button
        {
            Text = L10n.ClearAll,
            CustomMinimumSize = new Vector2(100f, 40f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
        };
        clearButton.Pressed += () =>
        {
            ClearAllSavePoints();
            window.Hide();
            window.QueueFree();
            ShowFeedback(L10n.FeedbackAllCleared);
        };
        buttonRow.AddChild(clearButton);

        var closeButton = new Button
                {
                    Text = L10n.Close,
                    CustomMinimumSize = new Vector2(100f, 40f),
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
                };
                closeButton.Pressed += () =>
                {
                    window.Hide();
                    window.QueueFree();
                };
                buttonRow.AddChild(closeButton);
                
                var openLogButton = new Button
                {
                    Text = "Open Log",
                    CustomMinimumSize = new Vector2(100f, 40f),
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                    TooltipText = "Open log directory"
                };
                openLogButton.Pressed += () =>
                {
                    var appDataPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
                    var gameLogsPath = Path.Combine(appDataPath, "SlayTheSpire2", "logs");
                    if (Directory.Exists(gameLogsPath))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", gameLogsPath);
                    }
                    else
                    {
                        System.Diagnostics.Process.Start("explorer.exe", appDataPath);
                    }
                };
                buttonRow.AddChild(openLogButton);
                
                var turnHistoryButton = new Button
                {
                    Text = IsChineseLocale() ? "回合记录" : "Turn History",
                    CustomMinimumSize = new Vector2(100f, 40f),
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                    TooltipText = IsChineseLocale() ? "查看回合记录并悔棋" : "View turn records and rollback"
                };
                turnHistoryButton.Pressed += () =>
                {
                    window.Hide();
                    window.QueueFree();
                    ShowTurnHistoryDialog();
                };
                buttonRow.AddChild(turnHistoryButton);
                
                content.AddChild(buttonRow);

                _statusLabel = new Label
        {
            Text = L10n.ExportPath,
            CustomMinimumSize = new Vector2(0f, 24f),
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        content.AddChild(_statusLabel);



        dropTarget.AddChild(content);
        
        window.AddChild(dropTarget);
        
        window.CloseRequested += () =>
        {
            window.Hide();
            window.QueueFree();
        };
        
        gameRoot.AddChild(window);
        
        window.PopupCentered();
        
        // 添加键盘事件处理ESC键关闭
        dropTarget._Window = window; // 传递window引用用于ESC键处理

    }



    public static void RecordSavePoint(string roomName, int floor = 0)

    {

        if (!FeatureSettingsStore.Current.EnableSavePoint)

        {

            return;

        }



        if (_isLoading)

        {

            Log.Info("[KKSavePoint] Skipping save point recording during load.");

            return;

        }



        Initialize();



        try

        {

            var runState = RunManager.Instance.DebugOnlyGetState();

            if (runState == null)

            {

                Log.Info("[KKSavePoint] Cannot record save point: no active run state.");

                return;

            }



            var localPlayer = LocalContext.GetMe(runState);

            if (localPlayer == null)

            {

                Log.Info("[KKSavePoint] Cannot record save point: no local player.");

                return;

            }



            string? gameSavePath = null;

            var appDataDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);

            var sts2Dir = Path.Combine(appDataDir, "SlayTheSpire2");



            if (Directory.Exists(sts2Dir))

            {

                // 搜索所有可能的存档文件
                var foundFiles = Directory.GetFiles(sts2Dir, "*.save", SearchOption.AllDirectories);

                // Log.Info($"[KKSavePoint] Found {foundFiles.Length} save files:");
                // foreach (var file in foundFiles)
                // {
                //     Log.Info($"[KKSavePoint] - {file}");
                // }

                // 检查是否是多人模式
                bool isMultiplayerMode = runState.Players.Count > 1;

                // 优先使用对应的存档文件
                string targetSaveName = isMultiplayerMode ? "current_run_mp.save" : "current_run.save";
                var targetSaveFiles = foundFiles.Where(f => Path.GetFileName(f).Equals(targetSaveName, StringComparison.OrdinalIgnoreCase)).ToArray();

                if (targetSaveFiles.Length > 0)
                {
                    gameSavePath = targetSaveFiles[0];
                    Log.Info($"[KKSavePoint] Using {targetSaveName}: {gameSavePath}");
                }
                // 如果没有找到目标存档文件，尝试另一种模式的存档文件
                else
                {
                    string alternativeSaveName = isMultiplayerMode ? "current_run.save" : "current_run_mp.save";
                    var alternativeSaveFiles = foundFiles.Where(f => Path.GetFileName(f).Equals(alternativeSaveName, StringComparison.OrdinalIgnoreCase)).ToArray();

                    if (alternativeSaveFiles.Length > 0)
                    {
                        gameSavePath = alternativeSaveFiles[0];
                        Log.Info($"[KKSavePoint] Using alternative {alternativeSaveName}: {gameSavePath}");
                    }
                    // 如果都没有，尝试第一个 save 文件
                    else if (foundFiles.Length > 0)
                    {
                        gameSavePath = foundFiles[0];
                        Log.Info($"[KKSavePoint] No {targetSaveName} or {alternativeSaveName} found, using first save file: {gameSavePath}");
                    }
                }

            }
            else
            {
                Log.Info($"[KKSavePoint] SlayTheSpire2 directory not found: {sts2Dir}");
            }



            if (string.IsNullOrEmpty(gameSavePath) || !File.Exists(gameSavePath))

            {

                Log.Info("[KKSavePoint] Cannot record save point: game save file not found.");

                return;

            }



            Log.Info($"[KKSavePoint] Using save file: {gameSavePath}");

            var saveFileContent = File.ReadAllText(gameSavePath);

            var saveFileHash = saveFileContent.GetHashCode();

            // 尝试从存档文件中解析楼层信息
            if (floor == 0)
            {
                try
                {
                    var saveData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(saveFileContent, _jsonOptions);
                    if (saveData != null)
                    {
                        // 尝试不同的路径查找楼层信息
                        List<string> floorPaths = new List<string>
                        {
                            "map.currentAct.currentFloor",
                            "currentAct.currentFloor",
                            "map.floor",
                            "floor",
                            "currentFloor",
                            "act.floor",
                            "act.currentFloor"
                        };

                        foreach (var path in floorPaths)
                        {
                            try
                            {
                                var value = GetJsonValue(saveData, path);
                                if (value.ValueKind == JsonValueKind.Number)
                                {
                                    int parsedFloor = value.GetInt32();
                                    if (parsedFloor > 0)
                                    {
                                        floor = parsedFloor;
                                        break;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }



            lock (_lock)

            {

                foreach (var existing in _savePoints)

                {

                    if (!string.IsNullOrEmpty(existing.SaveFileName))

                    {

                        var existingPath = Path.Combine(_savePointsDir, existing.SaveFileName);

                        if (File.Exists(existingPath))

                        {

                            var existingContent = File.ReadAllText(existingPath);

                            if (existingContent.GetHashCode() == saveFileHash)

                            {

                                Log.Info("[KKSavePoint] Skipping duplicate save point.");

                                return;

                            }

                        }

                    }

                }

            }



            var saveFileName = $"savepoint_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.json";

            var savePointPath = Path.Combine(_savePointsDir, saveFileName);

            File.Copy(gameSavePath, savePointPath, true);



            var hash = GenerateShortHash(saveFileContent);

            var characterName = GetCharacterName(runState);

            var difficulty = GetDifficulty(runState);

            // 尝试从localPlayer获取楼层信息
            if (floor == 0)
            {
                floor = GetFloorFromPlayer(localPlayer);
            }



            var isMultiplayer = runState.Players.Count > 1;
            var playerCount = runState.Players.Count;

            var record = new SavePointRecord

            {

                Index = GetSavePointCount() + 1,

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

                PlayerCount = playerCount

            };



            lock (_lock)

            {

                _savePoints.Add(record);

                if (_savePoints.Count > MaxSavePoints)

                {

                    _savePoints.RemoveAt(0);

                    for (int i = 0; i < _savePoints.Count; i++)

                    {

                        _savePoints[i].Index = i + 1;

                    }

                }

            }



            SaveToFile();



            Log.Info($"[KKSavePoint] SavePoint recorded: {record.RoomName} HP {record.CurrentHp}/{record.MaxHp}");

            ShowFeedback(IsChineseLocale() ? $"存档已保存: {roomName}" : $"Checkpoint saved: {roomName}");

        }

        catch (Exception ex)

        {

            Log.Error($"[KKSavePoint] Failed to record save point: {ex}");

        }

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

            string? gameSavePath = null;
            var appDataDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
            var sts2Dir = Path.Combine(appDataDir, "SlayTheSpire2");

            Log.Info($"[KKSavePoint] RecordTurnSavePoint - Searching in: {sts2Dir}");

            if (Directory.Exists(sts2Dir))
            {
                var foundFiles = Directory.GetFiles(sts2Dir, "*.save", SearchOption.AllDirectories);
                Log.Info($"[KKSavePoint] Found {foundFiles.Length} save files");
                
                bool isMultiplayerMode = runState.Players.Count > 1;
                string targetSaveName = isMultiplayerMode ? "current_run_mp.save" : "current_run.save";
                var targetSaveFiles = foundFiles.Where(f => Path.GetFileName(f).Equals(targetSaveName, StringComparison.OrdinalIgnoreCase)).ToArray();

                if (targetSaveFiles.Length > 0)
                {
                    gameSavePath = targetSaveFiles[0];
                    Log.Info($"[KKSavePoint] Found target save: {gameSavePath}");
                }
                else
                {
                    string alternativeSaveName = isMultiplayerMode ? "current_run.save" : "current_run_mp.save";
                    var alternativeSaveFiles = foundFiles.Where(f => Path.GetFileName(f).Equals(alternativeSaveName, StringComparison.OrdinalIgnoreCase)).ToArray();

                    if (alternativeSaveFiles.Length > 0)
                    {
                        gameSavePath = alternativeSaveFiles[0];
                        Log.Info($"[KKSavePoint] Found alternative save: {gameSavePath}");
                    }
                    else if (foundFiles.Length > 0)
                    {
                        gameSavePath = foundFiles[0];
                        Log.Info($"[KKSavePoint] Using first found save: {gameSavePath}");
                    }
                    else
                    {
                        Log.Info($"[KKSavePoint] No save files found");
                    }
                }
            }
            else
            {
                Log.Info($"[KKSavePoint] SlayTheSpire2 directory not found: {sts2Dir}");
            }

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


    private static void LoadSavePoint(int index)

    {

        SavePointRecord? record;

        lock (_lock)

        {

            if (index < 0 || index >= _savePoints.Count)

            {

                Log.Error($"[KKSavePoint] Invalid save point index: {index}");

                ShowFeedback(L10n.FeedbackInvalidCheckpoint);

                return;

            }

            record = _savePoints[index];

        }



        if (string.IsNullOrEmpty(record?.SaveFileName))

        {

            Log.Error($"[KKSavePoint] Save point has no save file: {index}");

            ShowFeedback(L10n.FeedbackNoSaveData);

            return;

        }



        var savePointPath = Path.Combine(_savePointsDir, record.SaveFileName);

        if (!File.Exists(savePointPath))

        {

            Log.Error($"[KKSavePoint] Save point file not found: {savePointPath}");

            ShowFeedback(L10n.FeedbackFileNotFound);

            return;

        }



        var currentRunState = RunManager.Instance.DebugOnlyGetState();

        bool isMultiplayer = currentRunState != null && currentRunState.Players.Count > 1;



        if (isMultiplayer)

        {

            var localPlayer = currentRunState != null ? LocalContext.GetMe(currentRunState) : null;

            var hostPlayer = currentRunState?.Players.FirstOrDefault();



            if (localPlayer == null || hostPlayer == null || localPlayer != hostPlayer)

            {

                Log.Info("[KKSavePoint] Only the host can rollback in multiplayer mode.");

                ShowFeedback(L10n.FeedbackOnlyHostCanRollback);

                return;

            }



            Log.Info("[KKSavePoint] Host player confirmed, proceeding with rollback...");

        }



        _isLoading = true;

        try

        {

            Log.Info($"[KKSavePoint] Loading save point: {record.RoomName}");

            ShowFeedback(L10n.FeedbackLoading(record.RoomName));



            string? gameSavePath = null;

            var appDataDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);

            var sts2Dir = Path.Combine(appDataDir, "SlayTheSpire2");



            if (Directory.Exists(sts2Dir))

            {

                // 搜索所有可能的存档文件
                var foundFiles = Directory.GetFiles(sts2Dir, "*.save", SearchOption.AllDirectories);

                // Log.Info($"[KKSavePoint] Found {foundFiles.Length} save files:");
                // foreach (var file in foundFiles)
                // {
                //     Log.Info($"[KKSavePoint] - {file}");
                // }

                // 检查是否是多人模式
                bool isMultiplayerMode = currentRunState != null && currentRunState.Players.Count > 1;

                // 优先使用对应的存档文件
                string targetSaveName = isMultiplayerMode ? "current_run_mp.save" : "current_run.save";
                var targetSaveFiles = foundFiles.Where(f => Path.GetFileName(f).Equals(targetSaveName, StringComparison.OrdinalIgnoreCase)).ToArray();

                if (targetSaveFiles.Length > 0)
                {
                    gameSavePath = targetSaveFiles[0];
                    Log.Info($"[KKSavePoint] Using {targetSaveName}: {gameSavePath}");
                }
                // 在多人模式下，只使用多人存档文件
                else if (isMultiplayerMode)
                {
                    Log.Error($"[KKSavePoint] No multiplayer save file found: {targetSaveName}");
                    ShowFeedback("找不到多人存档文件，请确保您在多人模式下创建了存档点。");
                    return;
                }
                // 在单机模式下，尝试另一种模式的存档文件
                else
                {
                    string alternativeSaveName = "current_run_mp.save";
                    var alternativeSaveFiles = foundFiles.Where(f => Path.GetFileName(f).Equals(alternativeSaveName, StringComparison.OrdinalIgnoreCase)).ToArray();

                    if (alternativeSaveFiles.Length > 0)
                    {
                        gameSavePath = alternativeSaveFiles[0];
                        Log.Info($"[KKSavePoint] Using alternative {alternativeSaveName}: {gameSavePath}");
                    }
                    // 如果都没有，尝试第一个 save 文件
                    else if (foundFiles.Length > 0)
                    {
                        gameSavePath = foundFiles[0];
                        Log.Info($"[KKSavePoint] No {targetSaveName} or {alternativeSaveName} found, using first save file: {gameSavePath}");
                    }
                }

            }
            else
            {
                Log.Info($"[KKSavePoint] SlayTheSpire2 directory not found: {sts2Dir}");
            }



            if (string.IsNullOrEmpty(gameSavePath))

            {

                Log.Error($"[KKSavePoint] Cannot find game save path");

                ShowFeedback("Cannot find game save path");

                return;

            }



            Log.Info($"[KKSavePoint] Copying save point to: {gameSavePath}");

            File.Copy(savePointPath, gameSavePath, true);



            var saveData = SaveManager.Instance.LoadRunSave();

            if (saveData?.SaveData == null)

            {

                Log.Error($"[KKSavePoint] Failed to load save data from copied file");

                ShowFeedback("Failed to load save data from copied file");

                return;

            }

            if (isMultiplayer)
            {
                Log.Info("[KKSavePoint] Multiplayer rollback: starting automated process...");

                try
                {
                    // // 清理游戏状态，确保上一局游戏结束
                    // Log.Info("[KKSavePoint] Cleaning up game state...");
                    // RunManager.Instance.CleanUp();

                    // // 停止所有游戏音乐
                    // Log.Info("[KKSavePoint] Stopping game music...");
                    // NRunMusicController.Instance?.StopMusic();

                    // // 清理当前运行的存档
                    // Log.Info("[KKSavePoint] Cleaning up current run save...");
                    // SaveManager.Instance.DeleteCurrentRun();

                    // // 清理所有游戏相关的节点
                    // Log.Info("[KKSavePoint] Cleaning up game nodes...");
                    // var gameNodes = NGame.Instance.GetChildren();
                    // foreach (var node in gameNodes)
                    // {
                    //     node.QueueFree();
                    // }

                    // // 执行垃圾回收
                    // Log.Info("[KKSavePoint] Running garbage collection...");
                    // GC.Collect();

                    // // 打开主菜单
                    // Log.Info("[KKSavePoint] Opening main menu...");
                    // var mainMenu = NMainMenu.Create(false);
                    // NGame.Instance.AddChild(mainMenu);

                    // // 打开多人游戏子菜单
                    // Log.Info("[KKSavePoint] Opening multiplayer submenu...");
                    // var multiplayerSubmenu = mainMenu.OpenMultiplayerSubmenu();

                    // // 启动主机并加载存档
                    // Log.Info("[KKSavePoint] Starting host from save...");
                    // multiplayerSubmenu.StartHost(saveData.SaveData);

                    // Log.Info("[KKSavePoint] Host from save started. Waiting for players to join...");
                    // ShowFeedback("已自动创建多人房间，客机请点击'加入'并自动准备。");
                }
                catch (Exception ex)
                {
                    Log.Error($"[KKSavePoint] Failed to complete automated process: {ex}");
                    // ShowFeedback("已回到游戏主菜单，请手动加载多人存档并进入房间。");
                }
            }

            else

            {
                RunManager.Instance.ActionQueueSet.Reset();

                NRunMusicController.Instance?.StopMusic();
                RunManager.Instance.CleanUp();



                Log.Info("[KKSavePoint] Cleaned up, starting load now");



                var runState = RunState.FromSerializable(saveData.SaveData);

                RunManager.Instance.SetUpSavedSinglePlayer(runState, saveData.SaveData);



                Log.Info($"[KKSavePoint] Continuing run with character: {saveData.SaveData.Players[0].CharacterId}");

                SfxCmd.Play(runState.Players[0].Character.CharacterTransitionSfx);

                NGame.Instance.ReactionContainer.InitializeNetworking(new NetSingleplayerGameService());

                TaskHelper.RunSafely(NGame.Instance.LoadRun(runState, null));

            }



            Log.Info($"[KKSavePoint] SavePoint loaded successfully: {record.RoomName}");

        }

        catch (Exception ex)

        {

            Log.Error($"[KKSavePoint] Failed to load save point: {ex}");
            ShowFeedback(L10n.FeedbackFailedToLoad);
            ShowFeedback($"Failed to load save point: {ex}");
        }

        finally

        {

            _isLoading = false;

        }

    }



    public static int GetSavePointCount()

    {

        lock (_lock)

        {

            return _savePoints.Count;

        }

    }



    public static void ClearAllSavePoints()

    {

        lock (_lock)

        {

            _savePoints.Clear();

        }

        SaveToFile();

        Log.Info("[KKSavePoint] All save points cleared.");

    }



    private static void DeleteSavePoint(int index)

    {

        SavePointRecord? record;

        lock (_lock)

        {

            if (index < 0 || index >= _savePoints.Count)

            {

                Log.Error($"[KKSavePoint] Invalid save point index: {index}");

                return;

            }

            record = _savePoints[index];

            _savePoints.RemoveAt(index);

        }



        if (record != null && !string.IsNullOrEmpty(record.SaveFileName))

        {

            try

            {

                var savePointPath = Path.Combine(_savePointsDir, record.SaveFileName);

                if (File.Exists(savePointPath))

                {

                    File.Delete(savePointPath);

                }

            }

            catch (Exception ex)

            {

                Log.Error($"[KKSavePoint] Failed to delete save point file: {ex}");

            }

        }



        for (int i = 0; i < _savePoints.Count; i++)

        {

            _savePoints[i].Index = i + 1;

        }



        SaveToFile();

        Log.Info($"[KKSavePoint] Deleted save point at index {index}");

        ShowFeedback(L10n.FeedbackDeleted);

    }



    private static string GenerateShortHash(string content)

    {

        using var sha256 = System.Security.Cryptography.SHA256.Create();

        var bytes = System.Text.Encoding.UTF8.GetBytes(content);

        var hash = sha256.ComputeHash(bytes);

        return Convert.ToHexString(hash).Substring(0, 7);

    }



    private static string GetCharacterName(RunState runState)

    {

        try

        {

            var player = LocalContext.GetMe(runState);

            if (player?.Creature != null)

            {

                return player.Creature.Name?.ToString() ?? "Unknown";

            }

        }

        catch { }

        return "Unknown";

    }



    private static string GetDifficulty(RunState runState)
    {
        try
        {
            var ascension = runState.AscensionLevel;
            if (ascension > 0)
            {
                return $"A{ascension}";
            }
        }
        catch { }
        return "Normal";
    }

    private static int GetFloorFromPlayer(dynamic player)
    {
        try
        {
            // 尝试从player获取楼层信息，类似于获取Gold的方式
            if (player != null)
            {
                // 尝试不同的属性名
                try { if (player.Floor > 0) return player.Floor; } catch { }
                try { if (player.CurrentFloor > 0) return player.CurrentFloor; } catch { }
                try { if (player.RoomFloor > 0) return player.RoomFloor; } catch { }
                try { if (player.ActFloor > 0) return player.ActFloor; } catch { }

                // 尝试从player的Creature获取
                try
                {
                    var creature = player.Creature;
                    if (creature != null)
                    {
                        try { if (creature.Floor > 0) return creature.Floor; } catch { }
                        try { if (creature.CurrentFloor > 0) return creature.CurrentFloor; } catch { }
                    }
                }
                catch { }
            }
        }
        catch { }
        return 0;
    }

    private static JsonElement GetJsonValue(Dictionary<string, JsonElement> data, string path)
    {
        var parts = path.Split('.');
        var current = data;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            string key = parts[i];
            if (current.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Object)
            {
                current = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(value.GetRawText(), _jsonOptions);
            }
            else
            {
                throw new KeyNotFoundException();
            }
        }

        string lastKey = parts[parts.Length - 1];
        if (current.TryGetValue(lastKey, out var lastValue))
        {
            return lastValue;
        }

        throw new KeyNotFoundException();
    }



    private static void ExportSingleToFile(int index)

    {

        try

        {

            Initialize();



            SavePointRecord? record = null;

            lock (_lock)

            {

                if (index >= 0 && index < _savePoints.Count)

                {

                    record = _savePoints[index];

                }

            }



            if (record == null)

            {

                Log.Error($"[KKSavePoint] Invalid save point index: {index}");

                return;

            }



            var sp = record;

            var savePointPath = Path.Combine(_savePointsDir, sp.SaveFileName ?? "");

            string? fileContent = null;

            if (!string.IsNullOrEmpty(sp.SaveFileName) && File.Exists(savePointPath))

            {

                fileContent = File.ReadAllText(savePointPath);

            }



            var exportData = new Dictionary<string, object?>

            {

                ["version"] = 1,

                ["exportTime"] = DateTime.Now.ToString("O"),

                ["savePoints"] = new List<Dictionary<string, object?>>

                {

                    new Dictionary<string, object?>
                    {
                        ["index"] = sp.Index,
                        ["hash"] = sp.Hash,
                        ["roomName"] = sp.RoomName,
                        ["characterName"] = sp.CharacterName,
                        ["difficulty"] = sp.Difficulty,
                        ["floor"] = sp.Floor,
                        ["gold"] = sp.Gold,
                        ["currentHp"] = sp.CurrentHp,
                        ["maxHp"] = sp.MaxHp,
                        ["timestamp"] = sp.Timestamp.ToString("O"),
                        ["isMultiplayer"] = sp.IsMultiplayer,
                        ["playerCount"] = sp.PlayerCount,
                        ["saveData"] = fileContent
                    }

                }

            };



            var json = JsonSerializer.Serialize(exportData, _jsonOptions);

            var compressed = CompressString(json);



            var safeCharacterName = string.IsNullOrEmpty(sp.CharacterName) ? "Unknown" :

                string.Join("", sp.CharacterName.Split(Path.GetInvalidFileNameChars()));

            var safeDifficulty = string.IsNullOrEmpty(sp.Difficulty) ? "Normal" :

                string.Join("", sp.Difficulty.Split(Path.GetInvalidFileNameChars()));

            var fileName = $"{sp.Hash}_{safeCharacterName}_{safeDifficulty}_F{sp.Floor}_savepoint.txt";



            var gameDir = ProjectSettings.GlobalizePath("res://");

            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))

            {

                gameDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

            }

            var exportDir = Path.Combine(gameDir, "STS2_SavePoints");

            if (!Directory.Exists(exportDir))

            {

                Directory.CreateDirectory(exportDir);

                // 创建空的说明文件
                var infoFile = Path.Combine(exportDir, "打开存档复制到剪贴板然后在游戏里面点击从剪贴板导入_Open the save file and copy to clipboard then click import from clipboard in game.txt");
                if (!File.Exists(infoFile))
                {
                    File.Create(infoFile).Dispose();
                }

            }



            var exportPath = Path.Combine(exportDir, fileName);

            File.WriteAllText(exportPath, compressed);



            try

            {

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo

                {

                    FileName = exportDir,

                    UseShellExecute = true,

                    Verb = "open"

                });

            }

            catch (Exception ex)

            {

                Log.Error($"[KKSavePoint] Failed to open folder: {ex}");

            }



            Log.Info($"[KKSavePoint] Exported checkpoint to file: {exportPath}");

            ShowFeedback(IsChineseLocale() ? $"已保存到: {fileName}" : $"Saved to: {fileName}");

        }

        catch (Exception ex)

        {

            Log.Error($"[KKSavePoint] Failed to export checkpoint to file: {ex}");

            ShowFeedback(IsChineseLocale() ? "导出失败" : "Export failed!");

        }

    }



    private static int ImportFromFile(string filePath)

    {

        try

        {

            Initialize();



            if (!File.Exists(filePath))

            {

                ShowFeedback(IsChineseLocale() ? "文件未找到！" : "File not found!");

                return 0;

            }



            var compressed = File.ReadAllText(filePath);

            var json = DecompressString(compressed);



            var exportData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, _jsonOptions);

            if (exportData == null || !exportData.TryGetValue("savePoints", out var savePointsElement))

            {

                ShowFeedback(IsChineseLocale() ? "无效的存档文件！" : "Invalid save file!");

                return 0;

            }



            var savePointsList = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement?>>>(savePointsElement.GetRawText(), _jsonOptions);

            if (savePointsList == null || savePointsList.Count == 0)

            {

                ShowFeedback(IsChineseLocale() ? "文件中没有存档！" : "No checkpoints in file!");

                return 0;

            }



            var imported = 0;

            foreach (var spData in savePointsList)

            {

                try

                {

                    var roomName = spData.TryGetValue("roomName", out var rn) && rn.HasValue ? rn.Value.GetString() ?? "Unknown" : "Unknown";

                    var hash = spData.TryGetValue("hash", out var h) && h.HasValue ? h.Value.GetString() ?? "" : "";

                    var characterName = spData.TryGetValue("characterName", out var cn) && cn.HasValue ? cn.Value.GetString() ?? "Unknown" : "Unknown";

                    var difficulty = spData.TryGetValue("difficulty", out var d) && d.HasValue ? d.Value.GetString() ?? "Normal" : "Normal";

                    var floor = spData.TryGetValue("floor", out var f) && f.HasValue ? f.Value.GetInt32() : 0;

                    var gold = spData.TryGetValue("gold", out var g) && g.HasValue ? g.Value.GetInt32() : 0;

                    var currentHp = spData.TryGetValue("currentHp", out var chp) && chp.HasValue ? chp.Value.GetInt32() : 0;

                    var maxHp = spData.TryGetValue("maxHp", out var mhp) && mhp.HasValue ? mhp.Value.GetInt32() : 0;

                    var timestampStr = spData.TryGetValue("timestamp", out var ts) && ts.HasValue ? ts.Value.GetString() : null;

                    var saveDataStr = spData.TryGetValue("saveData", out var sd) && sd.HasValue ? sd.Value.GetString() : null;



                    if (string.IsNullOrEmpty(saveDataStr)) continue;



                    if (string.IsNullOrEmpty(hash))

                    {

                        hash = GenerateShortHash(saveDataStr);

                    }



                    var timestamp = DateTime.TryParse(timestampStr, out var dt) ? dt : DateTime.Now;

                    var saveFileName = $"savepoint_{timestamp:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.json";

                    var savePointPath = Path.Combine(_savePointsDir, saveFileName);



                    File.WriteAllText(savePointPath, saveDataStr);



                    var isMultiplayer = spData.TryGetValue("isMultiplayer", out var im) && im.HasValue && im.Value.GetBoolean();
                    var playerCount = spData.TryGetValue("playerCount", out var pc) && pc.HasValue ? pc.Value.GetInt32() : 1;
                    var record = new SavePointRecord
                    {
                        Index = GetSavePointCount() + 1,
                        Hash = hash,
                        RoomName = roomName,
                        CharacterName = characterName,
                        Difficulty = difficulty,
                        Floor = floor,
                        Gold = gold,
                        CurrentHp = currentHp,
                        MaxHp = maxHp,
                        Timestamp = timestamp,
                        SaveFileName = saveFileName,
                        IsMultiplayer = isMultiplayer,
                        PlayerCount = playerCount
                    };



                    lock (_lock)

                    {

                        _savePoints.Add(record);

                    }

                    imported++;

                }

                catch (Exception ex)

                {

                    Log.Error($"[KKSavePoint] Failed to import checkpoint: {ex}");

                }

            }



            if (imported > 0)

            {

                SaveToFile();

                Log.Info($"[KKSavePoint] Imported {imported} checkpoints from file.");

                ShowFeedback(L10n.FeedbackImported(imported));

            }



            return imported;

        }

        catch (Exception ex)

        {

            Log.Error($"[KKSavePoint] Failed to import from file: {ex}");

            ShowFeedback(IsChineseLocale() ? "导入失败" : "Import failed!");

            return 0;

        }

    }



    private static void ExportSingleToClipboard(int index)

    {

        try

        {

            Initialize();



            SavePointRecord? record = null;

            lock (_lock)

            {

                if (index >= 0 && index < _savePoints.Count)

                {

                    record = _savePoints[index];

                }

            }



            if (record == null)

            {

                Log.Error($"[KKSavePoint] Invalid save point index: {index}");

                return;

            }



            var sp = record;

            var savePointPath = Path.Combine(_savePointsDir, sp.SaveFileName ?? "");

            string? fileContent = null;

            if (!string.IsNullOrEmpty(sp.SaveFileName) && File.Exists(savePointPath))

            {

                fileContent = File.ReadAllText(savePointPath);

            }



            var exportData = new Dictionary<string, object?>

            {

                ["version"] = 1,

                ["exportTime"] = DateTime.Now.ToString("O"),

                ["savePoints"] = new List<Dictionary<string, object?>>

                {

                    new Dictionary<string, object?>
                    {
                        ["index"] = sp.Index,
                        ["hash"] = sp.Hash,
                        ["roomName"] = sp.RoomName,
                        ["characterName"] = sp.CharacterName,
                        ["difficulty"] = sp.Difficulty,
                        ["floor"] = sp.Floor,
                        ["gold"] = sp.Gold,
                        ["currentHp"] = sp.CurrentHp,
                        ["maxHp"] = sp.MaxHp,
                        ["timestamp"] = sp.Timestamp.ToString("O"),
                        ["isMultiplayer"] = sp.IsMultiplayer,
                        ["playerCount"] = sp.PlayerCount,
                        ["saveData"] = fileContent
                    }

                }

            };



            var json = JsonSerializer.Serialize(exportData, _jsonOptions);

            var compressed = CompressString(json);



            DisplayServer.ClipboardSet(compressed);

            Log.Info($"[KKSavePoint] Exported single checkpoint to clipboard: {sp.RoomName}");

        }

        catch (Exception ex)

        {

            Log.Error($"[KKSavePoint] Failed to export checkpoint: {ex}");

        }

    }



    private static void ExportToClipboard()

    {

        try

        {

            Initialize();



            var exportData = new Dictionary<string, object>

            {

                ["version"] = 1,

                ["exportTime"] = DateTime.Now.ToString("O"),

                ["savePoints"] = new List<object>()

            };



            var savePointsList = (List<object>)exportData["savePoints"];



            lock (_lock)

            {

                foreach (var sp in _savePoints)

                {

                    var savePointPath = Path.Combine(_savePointsDir, sp.SaveFileName ?? "");

                    string? fileContent = null;

                    if (!string.IsNullOrEmpty(sp.SaveFileName) && File.Exists(savePointPath))

                    {

                        fileContent = File.ReadAllText(savePointPath);

                    }



                    savePointsList.Add(new Dictionary<string, object?>
                    {
                        ["index"] = sp.Index,
                        ["hash"] = sp.Hash,
                        ["roomName"] = sp.RoomName,
                        ["characterName"] = sp.CharacterName,
                        ["difficulty"] = sp.Difficulty,
                        ["floor"] = sp.Floor,
                        ["gold"] = sp.Gold,
                        ["currentHp"] = sp.CurrentHp,
                        ["maxHp"] = sp.MaxHp,
                        ["timestamp"] = sp.Timestamp.ToString("O"),
                        ["isMultiplayer"] = sp.IsMultiplayer,
                        ["playerCount"] = sp.PlayerCount,
                        ["saveData"] = fileContent
                    });

                }

            }



            var json = JsonSerializer.Serialize(exportData, _jsonOptions);

            var compressed = CompressString(json);



            DisplayServer.ClipboardSet(compressed);

            Log.Info($"[KKSavePoint] Exported {_savePoints.Count} checkpoints to clipboard.");

        }

        catch (Exception ex)

        {

            Log.Error($"[KKSavePoint] Failed to export checkpoints: {ex}");

        }

    }



    private static int ImportFromClipboard()

    {

        try

        {

            Initialize();



            var clipboardText = DisplayServer.ClipboardGet();

            if (string.IsNullOrEmpty(clipboardText))

            {

                return 0;

            }



            string json;

            try

            {

                json = DecompressString(clipboardText);

            }

            catch

            {

                json = clipboardText;

            }



            var exportData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, _jsonOptions);

            if (exportData == null || !exportData.TryGetValue("savePoints", out var savePointsElement))

            {

                return 0;

            }



            var imported = 0;

            var savePointsList = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement?>>>(savePointsElement.GetRawText(), _jsonOptions);



            if (savePointsList == null) return 0;



            foreach (var spData in savePointsList)

            {

                try

                {

                    var roomName = spData.TryGetValue("roomName", out var rn) && rn.HasValue ? rn.Value.GetString() ?? "Unknown" : "Unknown";

                    var hash = spData.TryGetValue("hash", out var h) && h.HasValue ? h.Value.GetString() ?? "" : "";

                    var characterName = spData.TryGetValue("characterName", out var cn) && cn.HasValue ? cn.Value.GetString() ?? "Unknown" : "Unknown";

                    var difficulty = spData.TryGetValue("difficulty", out var d) && d.HasValue ? d.Value.GetString() ?? "Normal" : "Normal";

                    var floor = spData.TryGetValue("floor", out var f) && f.HasValue ? f.Value.GetInt32() : 0;

                    var gold = spData.TryGetValue("gold", out var g) && g.HasValue ? g.Value.GetInt32() : 0;

                    var currentHp = spData.TryGetValue("currentHp", out var chp) && chp.HasValue ? chp.Value.GetInt32() : 0;

                    var maxHp = spData.TryGetValue("maxHp", out var mhp) && mhp.HasValue ? mhp.Value.GetInt32() : 0;

                    var timestampStr = spData.TryGetValue("timestamp", out var ts) && ts.HasValue ? ts.Value.GetString() : null;

                    var saveDataStr = spData.TryGetValue("saveData", out var sd) && sd.HasValue ? sd.Value.GetString() : null;



                    if (string.IsNullOrEmpty(saveDataStr)) continue;



                    if (string.IsNullOrEmpty(hash))

                    {

                        hash = GenerateShortHash(saveDataStr);

                    }



                    var timestamp = DateTime.TryParse(timestampStr, out var dt) ? dt : DateTime.Now;

                    var saveFileName = $"savepoint_{timestamp:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.json";

                    var savePointPath = Path.Combine(_savePointsDir, saveFileName);



                    File.WriteAllText(savePointPath, saveDataStr);



                    var isMultiplayer = spData.TryGetValue("isMultiplayer", out var im) && im.HasValue && im.Value.GetBoolean();
                    var playerCount = spData.TryGetValue("playerCount", out var pc) && pc.HasValue ? pc.Value.GetInt32() : 1;
                    var record = new SavePointRecord
                    {
                        Index = GetSavePointCount() + 1,
                        Hash = hash,
                        RoomName = roomName,
                        CharacterName = characterName,
                        Difficulty = difficulty,
                        Floor = floor,
                        Gold = gold,
                        CurrentHp = currentHp,
                        MaxHp = maxHp,
                        Timestamp = timestamp,
                        SaveFileName = saveFileName,
                        IsMultiplayer = isMultiplayer,
                        PlayerCount = playerCount
                    };



                    lock (_lock)

                    {

                        _savePoints.Add(record);

                    }

                    imported++;

                }

                catch (Exception ex)

                {

                    Log.Error($"[KKSavePoint] Failed to import checkpoint: {ex}");

                }

            }



            if (imported > 0)

            {

                SaveToFile();

                Log.Info($"[KKSavePoint] Imported {imported} checkpoints from clipboard.");

            }



            return imported;

        }

        catch (Exception ex)

        {

            Log.Error($"[KKSavePoint] Failed to import checkpoints: {ex}");

            return 0;

        }

    }



    private static string CompressString(string text)

    {

        var bytes = System.Text.Encoding.UTF8.GetBytes(text);

        using var output = new System.IO.MemoryStream();

        using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))

        {

            gzip.Write(bytes, 0, bytes.Length);

        }

        return Convert.ToBase64String(output.ToArray());

    }



    private static string DecompressString(string compressed)

    {

        var bytes = Convert.FromBase64String(compressed);

        using var input = new System.IO.MemoryStream(bytes);

        using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);

        using var reader = new System.IO.StreamReader(gzip);

        return reader.ReadToEnd();

    }



    private static readonly Queue<string> _feedbackQueue = new Queue<string>();
    private static bool _isShowingFeedback = false;

    private static void ShowFeedback(string text)
    {
        Log.Info($"[KKSavePoint] SavePoint feedback: {text}");
        
        try
        {
            if (_statusLabel != null && !_statusLabel.IsQueuedForDeletion())
            {
                _statusLabel.Text = text;
            }
        }
        catch (ObjectDisposedException)
        {
            _statusLabel = null;
        }
        catch (Exception)
        {
        }
        
        _feedbackQueue.Enqueue(text);
        
        if (!_isShowingFeedback)
        {
            ShowNextFeedback();
        }
    }

    private static async void ShowNextFeedback()
    {
        if (_feedbackQueue.Count == 0)
        {
            _isShowingFeedback = false;
            return;
        }

        _isShowingFeedback = true;
        string text = _feedbackQueue.Dequeue();

        try
        {
            var vfx = NFullscreenTextVfx.Create($"[KKSavePoint] {text}");
            if (vfx != null)
            {
                ((Godot.Node)NGame.Instance).AddChild(vfx);

                await Task.Delay(300);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to show feedback vfx: {ex}");
        }

        // 显示下一个反馈
        ShowNextFeedback();
    }

    private static void ShowTurnHistoryDialog()
    {
        try
        {
            Initialize();
            
            Log.Info($"[KKSavePoint] ShowTurnHistoryDialog called - Turn records count: {_turnRecords.Count}");

            Node? gameRoot = NGame.Instance;
            if (gameRoot == null)
            {
                Log.Error("[KKSavePoint] TurnHistory failed: could not resolve UI root.");
                ShowFeedback(IsChineseLocale() ? "回合记录失败: UI根节点不可用" : "Turn history failed: UI root unavailable.");
                return;
            }

        var existing = gameRoot.FindChild("TurnHistoryDialog", recursive: true, owned: false);
        if (existing != null)
        {
            existing.QueueFree();
        }

        var window = new Window
        {
            Name = "TurnHistoryDialog",
            Title = IsChineseLocale() ? "回合记录" : "Turn History",
            Exclusive = false
        };

        // 处理窗口关闭按钮 (X)
        window.CloseRequested += () =>
        {
            window.Hide();
            window.QueueFree();
        };

        var screenSize = DisplayServer.ScreenGetSize();
        var windowWidth = (int)(screenSize.X * 0.4f);
        var windowHeight = (int)(screenSize.Y * 0.5f);
        window.Size = new Vector2I(windowWidth, windowHeight);

        var content = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(windowWidth - 40f, 0f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        content.AddThemeConstantOverride("separation", 8);

        var titleLabel = new Label
        {
            Text = IsChineseLocale() ? "点击回合记录悔棋到该回合" : "Click turn record to rollback to that turn",
            CustomMinimumSize = new Vector2(0f, 30f),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        titleLabel.AddThemeFontSizeOverride("font_size", 18);
        content.AddChild(titleLabel);

        var scrollContainer = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(windowWidth - 60f, windowHeight - 120f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };

        var listContainer = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        listContainer.AddThemeConstantOverride("separation", 4);

        lock (_turnLock)
        {
            Log.Info($"[KKSavePoint] Turn records in lock: {_turnRecords.Count}");
            
            if (_turnRecords.Count == 0)
            {
                var noDataLabel = new Label
                {
                    Text = IsChineseLocale() ? "暂无回合记录" : "No turn records yet",
                    CustomMinimumSize = new Vector2(0f, 60f),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                listContainer.AddChild(noDataLabel);
            }
            else
            {
                // 按回合数排序显示
                var sortedRecords = _turnRecords.OrderBy(sp => sp.TurnNumber).ToList();
                Log.Info($"[KKSavePoint] Displaying {sortedRecords.Count} turn records");
                
                for (int i = 0; i < sortedRecords.Count; i++)
                {
                    var record = sortedRecords[i];
                    var row = new HBoxContainer
                    {
                        CustomMinimumSize = new Vector2(0f, 50f),
                        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
                    };
                    row.AddThemeConstantOverride("separation", 4);

                    string suffix = string.Format(L10n.TurnSavePointSuffix, record.Floor);
                    string buttonText = $"{L10n.TurnSavePointPrefix}{record.TurnNumber}{suffix} | HP {record.CurrentHp}/{record.MaxHp} | {record.Timestamp:HH:mm:ss}";

                    var turnButton = new Button
                    {
                        Text = buttonText,
                        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                        CustomMinimumSize = new Vector2(0f, 50f)
                    };
                    turnButton.AddThemeFontSizeOverride("font_size", 16);
                    turnButton.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.2f));

                    int targetTurn = record.TurnNumber;
                    turnButton.Pressed += () =>
                    {
                        Log.Info($"[KKSavePoint] Turn button pressed for turn {targetTurn}");
                        window.Hide();
                        window.QueueFree();
                        LoadTurnRecord(targetTurn);
                    };

                    row.AddChild(turnButton);

                    var rollbackButton = new Button
                    {
                        Text = IsChineseLocale() ? "悔棋" : "Rollback",
                        CustomMinimumSize = new Vector2(80f, 50f),
                        TooltipText = IsChineseLocale() ? $"悔棋到 回合{record.TurnNumber}" : $"Rollback to Turn {record.TurnNumber}"
                    };
                    rollbackButton.AddThemeFontSizeOverride("font_size", 16);

                    int rollbackTurn = record.TurnNumber;
                    rollbackButton.Pressed += () =>
                    {
                        Log.Info($"[KKSavePoint] Rollback button pressed for turn {rollbackTurn}");
                        window.Hide();
                        window.QueueFree();
                        LoadTurnRecord(rollbackTurn);
                    };

                    row.AddChild(rollbackButton);
                    listContainer.AddChild(row);
                }
            }
        }

        scrollContainer.AddChild(listContainer);
        content.AddChild(scrollContainer);

        var closeButton = new Button
        {
            Text = IsChineseLocale() ? "关闭" : "Close",
            CustomMinimumSize = new Vector2(100f, 40f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
        };
        closeButton.Pressed += () =>
        {
            window.Hide();
            window.QueueFree();
        };
        content.AddChild(closeButton);

        window.AddChild(content);
        gameRoot.AddChild(window);
        window.PopupCentered();
        
        Log.Info("[KKSavePoint] TurnHistoryDialog shown successfully");
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] ShowTurnHistoryDialog failed: {ex}");
            ShowFeedback(IsChineseLocale() ? $"回合记录失败: {ex.Message}" : $"Turn history failed: {ex.Message}");
        }
    }

    private static void LoadTurnRecord(int turnNumber)
    {//LoadSavePoint
        Log.Info($"[KKSavePoint] LoadTurnRecord called with turnNumber: {turnNumber}");

        SavePointRecord? record;
        SavePointRecord? firstTurnRecord;
        _pendingReplayData = null;

        lock (_turnLock)
        {
            Log.Info($"[KKSavePoint] _turnRecords.Count: {_turnRecords.Count}");

            record = null;
            foreach (var r in _turnRecords)
            {
                if (r.TurnNumber == turnNumber)
                {
                    record = r;
                    break;
                }
            }

            if (record == null)
            {
                Log.Error($"[KKSavePoint] Turn record not found for turn: {turnNumber}");
                ShowFeedback(IsChineseLocale() ? "未找到回合记录" : "Turn record not found");
                return;
            }

            firstTurnRecord = null;
            foreach (var r in _turnRecords)
            {
                if (r.TurnNumber == 1)
                {
                    firstTurnRecord = r;
                    break;
                }
            }

            Log.Info($"[KKSavePoint] Selected record: Turn {record?.TurnNumber}, SaveFile: {record?.SaveFileName}");
        }

        // 在清空之前保存需要回放的数据
        _turnsToReplay.Clear();
        if (record?.TurnNumber > 1)
        {
            // 保存所有需要回放的回合（从第1回合到第targetTurnNumber-1回合）
            for (int turnToReplay = 1; turnToReplay < record.TurnNumber; turnToReplay++)
            {
                foreach (var t in _turnPlaybackData)
                {
                    if (t.TurnNumber == turnToReplay)
                    {
                        _turnsToReplay.Add(t);
                        Log.Info($"[KKSavePoint] Queued replay for turn {t.TurnNumber}, card plays: {t.CardPlays.Count}");
                        break;
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(record?.SaveFileName))
        {
            Log.Error($"[KKSavePoint] Turn record has no save file");
            ShowFeedback(IsChineseLocale() ? "回合计录无存档数据" : "Turn record has no save data");
            return;
        }

        SavePointRecord? recordToLoad = record.TurnNumber == 1 ? record : firstTurnRecord;

        if (recordToLoad == null || string.IsNullOrEmpty(recordToLoad.SaveFileName))
        {
            Log.Error($"[KKSavePoint] No valid save file to load");
            ShowFeedback(IsChineseLocale() ? "无法找到有效存档" : "No valid save file to load");
            return;
        }

        var savePointPath = Path.Combine(_savePointsDir, recordToLoad.SaveFileName);
        Log.Info($"[KKSavePoint] SavePointPath: {savePointPath}");

        if (!File.Exists(savePointPath))
        {
            Log.Error($"[KKSavePoint] Turn record file not found: {savePointPath}");
            ShowFeedback(L10n.FeedbackFileNotFound);
            return;
        }

        var currentRunState = RunManager.Instance.DebugOnlyGetState();
        bool isMultiplayer = currentRunState != null && currentRunState.Players.Count > 1;

        if (isMultiplayer)
        {
            var localPlayer = currentRunState != null ? LocalContext.GetMe(currentRunState) : null;
            var hostPlayer = currentRunState?.Players.FirstOrDefault();

            if (localPlayer == null || hostPlayer == null || localPlayer != hostPlayer)
            {
                Log.Info("[KKSavePoint] Only the host can rollback in multiplayer mode.");
                ShowFeedback(L10n.FeedbackOnlyHostCanRollback);
                return;
            }

            Log.Info("[KKSavePoint] Host player confirmed, proceeding with rollback...");
        }

        _isLoading = true;
        _turnRecordLoadCount++;
        _isLoadingTurnRecordFlag = true;
        Log.Info($"[KKSavePoint] _isLoadingTurnRecordFlag set to true");
        try
        {
            Log.Info($"[KKSavePoint] Loading turn record: Turn {record.TurnNumber} (using save from Turn {recordToLoad.TurnNumber})");
            ShowFeedback(IsChineseLocale() ? $"正在加载: 回合{record.TurnNumber}" : $"Loading: Turn {record.TurnNumber}");

            string? gameSavePath = null;
            var appDataDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
            var sts2Dir = Path.Combine(appDataDir, "SlayTheSpire2");

            if (Directory.Exists(sts2Dir))
            {
                var foundFiles = Directory.GetFiles(sts2Dir, "*.save", SearchOption.AllDirectories);
                
                bool isMultiplayerMode = currentRunState != null && currentRunState.Players.Count > 1;
                string targetSaveName = isMultiplayerMode ? "current_run_mp.save" : "current_run.save";
                var targetSaveFiles = foundFiles.Where(f => Path.GetFileName(f).Equals(targetSaveName, StringComparison.OrdinalIgnoreCase)).ToArray();

                if (targetSaveFiles.Length > 0)
                {
                    gameSavePath = targetSaveFiles[0];
                    Log.Info($"[KKSavePoint] Using {targetSaveName}: {gameSavePath}");
                }
                else if (isMultiplayerMode)
                {
                    Log.Error($"[KKSavePoint] No multiplayer save file found: {targetSaveName}");
                    ShowFeedback(IsChineseLocale() ? "找不到多人存档文件，请确保您在多人模式下创建了存档点。" : "No multiplayer save file found. Please ensure you created a save point while in multiplayer mode.");
                    return;
                }
                else
                {
                    string alternativeSaveName = "current_run_mp.save";
                    var alternativeSaveFiles = foundFiles.Where(f => Path.GetFileName(f).Equals(alternativeSaveName, StringComparison.OrdinalIgnoreCase)).ToArray();

                    if (alternativeSaveFiles.Length > 0)
                    {
                        gameSavePath = alternativeSaveFiles[0];
                        Log.Info($"[KKSavePoint] Using alternative {alternativeSaveName}: {gameSavePath}");
                    }
                    else if (foundFiles.Length > 0)
                    {
                        gameSavePath = foundFiles[0];
                        Log.Info($"[KKSavePoint] No {targetSaveName} or {alternativeSaveName} found, using first save file: {gameSavePath}");
                    }
                }
            }
            else
            {
                Log.Info($"[KKSavePoint] SlayTheSpire2 directory not found: {sts2Dir}");
            }

            if (string.IsNullOrEmpty(gameSavePath))
            {
                Log.Error($"[KKSavePoint] Cannot find game save path");
                ShowFeedback("Cannot find game save path");
                return;
            }

            Log.Info($"[KKSavePoint] Copying turn record to: {gameSavePath}");
            File.Copy(savePointPath, gameSavePath, true);

            var saveData = SaveManager.Instance.LoadRunSave();
            if (saveData?.SaveData == null)
            {
                Log.Error($"[KKSavePoint] Failed to load save data from copied file");
                ShowFeedback("Failed to load save data from copied file");
                return;
            }

            if (isMultiplayer)
            {
                Log.Info("[KKSavePoint] Multiplayer rollback: showing manual message...");
                ShowFeedback("已替换多人存档，请回到主菜单并重新加载。");
            }
            else
            {
                RunManager.Instance.ActionQueueSet.Reset();
                NRunMusicController.Instance?.StopMusic();
                RunManager.Instance.CleanUp();

                Log.Info("[KKSavePoint] Cleaned up, starting load now");

                var runState = RunState.FromSerializable(saveData.SaveData);
                RunManager.Instance.SetUpSavedSinglePlayer(runState, saveData.SaveData);

                Log.Info($"[KKSavePoint] Continuing run with character: {saveData.SaveData.Players[0].CharacterId}");

                SfxCmd.Play(runState.Players[0].Character.CharacterTransitionSfx);
                NGame.Instance.ReactionContainer.InitializeNetworking(new NetSingleplayerGameService());
                TaskHelper.RunSafely(NGame.Instance.LoadRun(runState, null));

                // 设置回放队列，在 OnTurnStarted 中逐步执行回放
                _targetReplayTurn = record.TurnNumber;
                _replayQueue.Clear();
                _replayQueueIndex = 0;
                foreach (var t in _turnsToReplay)
                {
                    _replayQueue.Add(t);
                }
                _isReplaying = true;
                Log.Info($"[KKSavePoint] Replay queue set up for turn {_targetReplayTurn}, {_replayQueue.Count} turns to replay");
            }

            Log.Info($"[KKSavePoint] Turn record loaded successfully: Turn {record.TurnNumber}");
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to load turn record: {ex}");
            ShowFeedback(L10n.FeedbackFailedToLoad);
            ShowFeedback($"Failed to load turn record: {ex}");
        }
        finally
        {
            _isLoading = false;
            _turnRecordLoadCount--;
            _isLoadingTurnRecordFlag = false;
            Log.Info($"[KKSavePoint] _turnRecordLoadCount is now {_turnRecordLoadCount}, _isLoadingTurnRecordFlag = false");
        }
    }

    private static async void DelayedReplay(int targetTurnNumber)
    {
        Log.Info($"[KKSavePoint] DelayedReplay started for turn {targetTurnNumber}");
        await Task.Delay(2000);
        Log.Info($"[KKSavePoint] DelayedReplay delay complete, calling ReplayTurnOperations");
        // 确保在主线程执行
        try
        {
            ReplayTurnOperations(targetTurnNumber);
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed in delayed replay: {ex}");
        }
    }

    private static void ReplayTurnOperations(int targetTurnNumber)
    {
        try
        {
            Log.Info($"[KKSavePoint] Starting replay operations for turn {targetTurnNumber}");
            _isReplaying = true;

            if (targetTurnNumber == 1)
            {
                Log.Info($"[KKSavePoint] Target turn is 1, no operations to replay");
                _isReplaying = false;
                return;
            }

            if (_turnsToReplay.Count == 0)
            {
                Log.Info($"[KKSavePoint] No turns to replay");
                _isReplaying = false;
                return;
            }

            // 依次回放所有回合，每回合提交后等待回合结束
            for (int i = 0; i < _turnsToReplay.Count; i++)
            {
                var turnData = _turnsToReplay[i];
                int currentTurnNumber = turnData.TurnNumber;
                Log.Info($"[KKSavePoint] Replaying turn {currentTurnNumber} with {turnData.CardPlays.Count} card plays");

                // 获取开始时的回合号
                int startRound = GetCurrentRoundNumber();

                // 提交当前回合的所有卡牌
                foreach (var cardPlay in turnData.CardPlays)
                {
                    Log.Info($"[KKSavePoint] Replaying card: {cardPlay.CardName} (ID: {cardPlay.CardId})");
                    ReplayCardPlay(cardPlay);
                }

                // 提交结束回合动作
                SubmitEndTurnAction(currentTurnNumber);

                // 等待回合结束
                if (i < _turnsToReplay.Count - 1)
                {
                    Log.Info($"[KKSavePoint] Waiting for turn {currentTurnNumber} to end...");
                    WaitForTurnToEnd(startRound);
                }

                // 回合结束后清除 selector
                if (_currentReplaySelectorScope != null)
                {
                    try { _currentReplaySelectorScope.Dispose(); } catch { }
                    _currentReplaySelectorScope = null;
                    Log.Info($"[KKSavePoint] Selector cleared after turn {currentTurnNumber}");
                }
            }

            // 回放完成后清空
            _pendingReplayData = null;
            _turnsToReplay.Clear();
            _isReplaying = false;

            Log.Info($"[KKSavePoint] Replay operations completed for turn {targetTurnNumber}");
        }
        catch (Exception ex)
        {
            _isReplaying = false;
            Log.Error($"[KKSavePoint] Failed to replay turn operations: {ex}");
        }
    }

    private static int GetCurrentRoundNumber()
    {
        try
        {
            var combatManager = CombatManager.Instance;
            if (combatManager != null)
            {
                dynamic combatState = combatManager.DebugOnlyGetState();
                if (combatState != null)
                {
                    return combatState.RoundNumber;
                }
            }
        }
        catch { }
        return 1;
    }

    private static void WaitForTurnToEnd(int startRound)
    {
        int maxIterations = 300; // 最多等待3秒（假设10ms一次检查）
        int iterations = 0;

        while (iterations < maxIterations)
        {
            // 使用 Thread.Sleep 让出时间片，让游戏可以处理动作队列
            System.Threading.Thread.Sleep(10);
            iterations++;

            // 检查回合是否已经改变
            int currentRound = GetCurrentRoundNumber();
            if (currentRound > startRound)
            {
                Log.Info($"[KKSavePoint] Turn ended, now at round {currentRound}");
                return;
            }

            // 检查是否不再是玩家回合（可能进入了敌人回合）
            try
            {
                var combatManager = CombatManager.Instance;
                if (combatManager != null)
                {
                    dynamic combatState = combatManager.DebugOnlyGetState();
                    if (combatState != null && combatState.CurrentSide != CombatSide.Player)
                    {
                        Log.Info($"[KKSavePoint] Not player turn anymore, side = {combatState.CurrentSide}");
                    }
                }
            }
            catch { }
        }

        Log.Warn($"[KKSavePoint] WaitForTurnToEnd timed out after {maxIterations} iterations");
    }

    internal static void SubmitEndTurnAction(int turnNumber)
    {
        try
        {
            var combatManager = CombatManager.Instance;
            if (combatManager != null)
            {
                dynamic combatState = combatManager.DebugOnlyGetState();
                if (combatState != null)
                {
                    var player = LocalContext.GetMe(combatState.RunState);
                    if (player != null)
                    {
                        var endTurnActionType = Type.GetType("MegaCrit.Sts2.Core.GameActions.EndPlayerTurnAction, sts2");
                        if (endTurnActionType != null)
                        {
                            var constructor = endTurnActionType.GetConstructor(new Type[] { 
                                typeof(MegaCrit.Sts2.Core.Entities.Players.Player), 
                                typeof(int) 
                            });
                            if (constructor != null)
                            {
                                dynamic endTurnAction = constructor.Invoke(new object[] { player, turnNumber });
                                var actionQueueSet = RunManager.Instance.ActionQueueSet;
                                if (actionQueueSet != null)
                                {
                                    actionQueueSet.EnqueueWithoutSynchronizing(endTurnAction);
                                    Log.Info($"[KKSavePoint] EndTurn action submitted for round {turnNumber}");
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[KKSavePoint] Failed to submit end turn action: {ex.Message}");
        }
    }

    internal static void ReplayCardPlay(CardPlayRecord record)
    {
        try
        {
            var combatManager = CombatManager.Instance;
            if (combatManager == null)
            {
                Log.Error("[KKSavePoint] CombatManager is null");
                return;
            }

            dynamic combatState = combatManager.DebugOnlyGetState();
            if (combatState == null)
            {
                Log.Error("[KKSavePoint] CombatState is null");
                return;
            }

            var player = LocalContext.GetMe(combatState.RunState);
            if (player == null)
            {
                Log.Error("[KKSavePoint] Player not found for card replay");
                return;
            }

            var handPile = player.PlayerCombatState?.Hand;
            if (handPile == null)
            {
                Log.Error("[KKSavePoint] Player hand is null");
                return;
            }

            // 不在这里等待，等待逻辑在异步回放方法中

            var handCards = handPile.Cards;

            // 查找手牌中的对应卡牌
            dynamic cardToPlay = null;
            int cardIndex = -1;
            int currentIndex = 0;
            foreach (dynamic card in handCards)
            {
                if (card != null)
                {
                    string? cardIdStr = null;
                    try
                    {
                        var modelId = card.Id;
                        if (modelId != null)
                        {
                            cardIdStr = modelId.Entry ?? modelId.ToString();
                        }
                    }
                    catch
                    {
                        cardIdStr = card.Id?.ToString();
                    }
                    
                    if (cardIdStr == record.CardId)
                    {
                        cardToPlay = card;
                        cardIndex = currentIndex;
                        break;
                    }
                }
                currentIndex++;
            }
            
            if (cardToPlay == null)
            {
                Log.Warn($"[KKSavePoint] Card {record.CardName} not found in hand, skipping");
                return;
            }

            // 检查能量是否足够
            var currentEnergy = player.PlayerCombatState?.Energy ?? 0;
            if (currentEnergy < record.EnergyCost)
            {
                Log.Warn($"[KKSavePoint] Not enough energy to play {record.CardName}, skipping");
                return;
            }

            Log.Info($"[KKSavePoint] Replaying card: {record.CardName} (ID: {record.CardId}, index: {cardIndex})");

            // 清除之前的 selector
            if (_currentReplaySelectorScope != null)
            {
                try { _currentReplaySelectorScope.Dispose(); } catch { }
                _currentReplaySelectorScope = null;
            }
            _currentReplaySelector = null;

            // 如果卡牌有选牌记录（如HEADBUTT需要从弃牌堆选牌），设置Selector自动选择
            if (!string.IsNullOrEmpty(record.SelectedCardId) || record.SelectedCardIndex.HasValue)
            {
                Log.Info($"[KKSavePoint] Card {record.CardName} has selection recorded: CardId={record.SelectedCardId}, Index={record.SelectedCardIndex}, setting up selector");
                try
                {
                    // 首先清理之前的 selector
                    if (_currentReplaySelectorScope != null)
                    {
                        try { _currentReplaySelectorScope.Dispose(); } catch { }
                        _currentReplaySelectorScope = null;
                    }
                    _currentReplaySelector = null;
                    
                    // 创建新的 selector
                    var replaySelector = new ReplayCardSelector(record.SelectedCardId, record.SelectedCardIndex);
                    _currentReplaySelector = replaySelector;
                    Log.Info($"[KKSavePoint] Created replay selector set directly: {record.SelectedCardId}, {record.SelectedCardIndex}");
                    
                    // 也尝试使用 PushSelector 作为备用
                    try
                    {
                        var cardSelectorType = Type.GetType("MegaCrit.Sts2.Core.Commands.CardSelectCmd, sts2");
                        if (cardSelectorType != null)
                        {
                            Log.Info($"[KKSavePoint] Found CardSelectCmd type");
                            var pushSelectorMethod = cardSelectorType.GetMethod("PushSelector");
                            if (pushSelectorMethod != null)
                            {
                                Log.Info($"[KKSavePoint] Found PushSelector method");
                                _currentReplaySelectorScope = (IDisposable)pushSelectorMethod.Invoke(null, new object[] { replaySelector });
                                Log.Info($"[KKSavePoint] Selector pushed successfully, scope type: {_currentReplaySelectorScope?.GetType()?.Name}");
                            }
                        }
                    }
                    catch (Exception pushEx)
                    {
                        Log.Info($"[KKSavePoint] PushSelector failed (not critical): {pushEx.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[KKSavePoint] Failed to set selector: {ex}");
                }
            }
            else
            {
                Log.Info($"[KKSavePoint] Card {record.CardName} has no selection recorded");
            }

            // 找到目标怪物
            dynamic target = null;
            if (record.TargetPositions != null && record.TargetPositions.Count > 0)
            {
                int targetIndex = record.TargetPositions[0];
                if (targetIndex >= 0 && targetIndex < 10)
                {
                    try
                    {
                        var creatures = combatState.Creatures;
                        if (creatures != null && targetIndex < creatures.Count)
                        {
                            target = creatures[targetIndex];
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"[KKSavePoint] Failed to get target by index: {ex.Message}");
                    }
                }
            }
            
            if (target == null && record.TargetIds != null && record.TargetIds.Count > 0)
            {
                try
                {
                    var creatures = combatState.Creatures;
                    if (creatures != null)
                    {
                        string targetIdStr = record.TargetIds[0];
                        foreach (dynamic creature in creatures)
                        {
                            // 使用 ModelId 来匹配
                            string? creatureModelIdStr = null;
                            try
                            {
                                var modelId = creature.ModelId;
                                if (modelId != null)
                                {
                                    creatureModelIdStr = modelId.Entry ?? modelId.ToString();
                                }
                            }
                            catch { }
                            
                            if (creatureModelIdStr != null && creatureModelIdStr.Contains(targetIdStr))
                            {
                                target = creature;
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"[KKSavePoint] Failed to get target by model id: {ex.Message}");
                }
            }
            
            Log.Info($"[KKSavePoint] Target: {(target != null ? target.LogName?.ToString() : "null")}");

            // 暂时先不处理选牌回放，先确保基本回放功能正常
            /*
            // 如果卡牌有选牌记录（如HEADBUTT需要从弃牌堆选牌），记录下来供后续处理
            // 注意：Headbutt的选择是在OnPlay执行过程中发生的，所以我们需要记录选择信息
            // 然后通过Hook拦截FromSimpleGrid来自动选择
            if (!string.IsNullOrEmpty(record.SelectedCardId) || record.SelectedCardIndex.HasValue)
            {
                _pendingReplayCardSelection = new PendingCardSelection
                {
                    CardId = record.SelectedCardId,
                    CardIndex = record.SelectedCardIndex,
                    TargetCardId = record.SelectedCardId
                };
                Log.Info($"[KKSavePoint] Set pending replay selection: CardId={record.SelectedCardId}, CardIndex={record.SelectedCardIndex}");
            }
            */

            // 使用游戏系统提交卡牌动作
            // 创建 PlayCardAction 并通过 ActionQueueSet 提交
            // 游戏会自动处理渲染和效果
            try
            {
                // 尝试获取 PlayCardAction 类型
                var playCardActionType = Type.GetType("MegaCrit.Sts2.Core.GameActions.PlayCardAction, sts2");
                Log.Info($"[KKSavePoint] PlayCardAction type: {playCardActionType?.FullName ?? "null"}");
                
                if (playCardActionType != null)
                {
                    // 尝试不同的构造函数签名
                    var constructor = playCardActionType.GetConstructor(new Type[] { typeof(object), typeof(object) });
                    Log.Info($"[KKSavePoint] Constructor (object, object): {constructor != null}");
                    
                    if (constructor == null)
                    {
                        // 尝试实际类型
                        constructor = playCardActionType.GetConstructor(new Type[] { 
                            typeof(MegaCrit.Sts2.Core.Models.CardModel), 
                            typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature) 
                        });
                        Log.Info($"[KKSavePoint] Constructor (CardModel, Creature): {constructor != null}");
                    }
                    
                    if (constructor != null)
                    {
                        try
                        {
                            int handSizeBefore = handCards.Count;
                            Log.Info($"[KKSavePoint] Hand size before playing card: {handSizeBefore}");

                            dynamic playCardAction = constructor.Invoke(new object[] { cardToPlay, target });

                            // 通过 ActionQueueSet 提交动作
                            var actionQueueSet = RunManager.Instance.ActionQueueSet;
                            if (actionQueueSet != null)
                            {
                                actionQueueSet.EnqueueWithoutSynchronizing(playCardAction);
                                Log.Info($"[KKSavePoint] Card action submitted to queue successfully");

                                // 不在这里等待，等待逻辑在异步回放方法中

                                int handSizeAfter = handPile.Cards.Count;
                                Log.Info($"[KKSavePoint] Hand size after playing card: {handSizeAfter}");

                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[KKSavePoint] Invoke failed: {ex.Message}");
                        }
                    }

                    Log.Warn($"[KKSavePoint] Could not create PlayCardAction, falling back to manual play");

                    // 回退方案：手动处理
                    // 扣除能量
                    if (player.PlayerCombatState != null)
                    {
                        player.PlayerCombatState.Energy -= record.EnergyCost;
                    }

                    // 从手牌中移除卡牌
                    if (handCards.Remove(cardToPlay))
                    {
                        Log.Info($"[KKSavePoint] Card {record.CardName} removed from hand (fallback)");
                    }
                }

                // 尝试执行卡牌效果
                ExecuteCardEffect(cardToPlay, record, combatState, player);

                Log.Info($"[KKSavePoint] Card {record.CardName} played (fallback mode)");
            }
            catch (Exception ex)
            {
                Log.Error($"[KKSavePoint] Failed to replay card play: {ex}");
            }
        }
        catch (Exception outerEx)
        {
            Log.Error($"[KKSavePoint] Outer exception in ReplayCardPlay: {outerEx}");
        }
        // 注意：不在这里清理 selector！因为卡牌效果可能在提交后才执行选择
        // selector 会在下一张卡牌回放时清理，或者在整个回合结束时清理
    }

    internal static async Task ReplayTurnCardsAsync(TurnPlaybackData turnData)
    {
        try
        {
            _isReplayingAsync = true;
            foreach (var cardPlay in turnData.CardPlays)
            {
                Log.Info($"[KKSavePoint] Replaying card: {cardPlay.CardName} (ID: {cardPlay.CardId})");
                ReplayCardPlay(cardPlay);
                await Task.Delay(300);
            }
            // 回合结束后清理所有 selector
            if (_currentReplaySelectorScope != null)
            {
                try { _currentReplaySelectorScope.Dispose(); } catch { }
                _currentReplaySelectorScope = null;
            }
            _currentReplaySelector = null;
            
            SubmitEndTurnAction(turnData.TurnNumber);
            _replayQueueIndex++;
            if (_replayQueueIndex >= _replayQueue.Count)
            {
                Log.Info($"[KKSavePoint] Reached end of replay queue, stopping replay mode");
                _isReplaying = false;
                _replayQueue.Clear();
                _replayQueueIndex = 0;
                _targetReplayTurn = 0;
            }
            _isReplayingAsync = false;
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Error in async replay: {ex}");
            _isReplaying = false;
            _isReplayingAsync = false;
            _replayQueue.Clear();
            _replayQueueIndex = 0;
            _targetReplayTurn = 0;
            // 确保 selector 被清理
            if (_currentReplaySelectorScope != null)
            {
                try { _currentReplaySelectorScope.Dispose(); } catch { }
                _currentReplaySelectorScope = null;
            }
            _currentReplaySelector = null;
        }
    }

    private static void ExecuteCardEffect(dynamic card, CardPlayRecord record, dynamic combatState, dynamic player)
    {
        try
        {
            // 尝试使用反射执行卡牌效果
            Type cardType = card.GetType();
            
            // 尝试找 OnPlay 方法（protected async Task OnPlay(PlayerChoiceContext, CardPlay)）
            var playMethod = cardType.GetMethod("OnPlay", 
                System.Reflection.BindingFlags.Instance | 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Public);
            
            if (playMethod == null)
            {
                // 尝试找 Play 方法
                playMethod = cardType.GetMethod("Play", 
                    System.Reflection.BindingFlags.Instance | 
                    System.Reflection.BindingFlags.Public);
            }
            
            if (playMethod != null)
            {
                Log.Info($"[KKSavePoint] Found method: {playMethod.Name} for card type: {cardType.Name}");
                
                // 获取参数类型
                var parameters = playMethod.GetParameters();
                Log.Info($"[KKSavePoint] Method has {parameters.Length} parameters");
                
                // 创建参数
                object[] args = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    Type paramType = parameters[i].ParameterType;
                    Log.Info($"[KKSavePoint] Parameter {i}: {paramType.FullName}");
                    
                    // 根据参数类型尝试创建合适的对象
                    if (paramType.FullName?.Contains("PlayerChoiceContext") == true)
                    {
                        try
                        {
                            // 使用反射创建 PlayerChoiceContext
                            var contextType = Type.GetType("MegaCrit.Sts2.Core.Context.PlayerChoiceContext, MegaCrit.Sts2.Core");
                            if (contextType != null)
                            {
                                args[i] = Activator.CreateInstance(contextType, combatState.RunState);
                                Log.Info($"[KKSavePoint] Created PlayerChoiceContext via reflection");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[KKSavePoint] Failed to create PlayerChoiceContext: {ex.Message}");
                        }
                    }
                    else if (paramType.FullName?.Contains("CardPlay") == true)
                    {
                        try
                        {
                            // 使用反射创建 CardPlay - card是CardModel, player.Creature是目标
                            var cardPlayType = Type.GetType("MegaCrit.Sts2.Core.Commands.CardPlay, MegaCrit.Sts2.Core.Commands");
                            if (cardPlayType != null)
                            {
                                args[i] = Activator.CreateInstance(cardPlayType, card, player.Creature);
                                Log.Info($"[KKSavePoint] Created CardPlay via reflection");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[KKSavePoint] Failed to create CardPlay: {ex.Message}");
                        }
                    }
                }
                
                // 执行方法
                try
                {
                    var result = playMethod.Invoke(card, args);
                    if (result is System.Threading.Tasks.Task task)
                    {
                        Log.Info($"[KKSavePoint] Card effect task started, waiting...");
                        task.Wait(TimeSpan.FromSeconds(5)); // 等待最多5秒
                        Log.Info($"[KKSavePoint] Card effect task completed");
                    }
                    
                    Log.Info($"[KKSavePoint] Card effect executed successfully");
                }
                catch (Exception ex)
                {
                    Log.Error($"[KKSavePoint] Failed to invoke method: {ex.Message}");
                }
            }
            else
            {
                Log.Warn($"[KKSavePoint] No OnPlay or Play method found for card type: {cardType.Name}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to execute card effect: {ex}");
        }
    }


    private static void ShowDeckDialog(int savePointIndex)
    {
        try
        {
            Initialize();
            
            SavePointRecord? record;
            lock (_lock)
            {
                if (savePointIndex < 0 || savePointIndex >= _savePoints.Count)
                {
                    ShowFeedback(L10n.FeedbackInvalidCheckpoint);
                    return;
                }
                record = _savePoints[savePointIndex];
            }
            
            if (string.IsNullOrEmpty(record?.SaveFileName))
            {
                ShowFeedback(L10n.FeedbackNoSaveData);
                return;
            }
            
            var savePointPath = Path.Combine(_savePointsDir, record.SaveFileName);
            if (!File.Exists(savePointPath))
            {
                ShowFeedback(L10n.FeedbackFileNotFound);
                return;
            }
            
            // 读取存档内容
            var saveContent = File.ReadAllText(savePointPath);
            
            // 创建新窗口
            var window = new Window
            {
                Title = L10n.DeckTitle,
                Exclusive = false
            };
            
            var screenSize = DisplayServer.ScreenGetSize();
            var windowWidth = (int)(screenSize.X * 0.5f);
            var windowHeight = (int)(screenSize.Y * 0.6f);
            window.Size = new Vector2I(windowWidth, windowHeight);
            
            var content = new VBoxContainer
            {
                CustomMinimumSize = new Vector2(windowWidth - 40f, windowHeight - 40f),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            content.AddThemeConstantOverride("separation", 8);
            
            var scrollContainer = new ScrollContainer
            {
                CustomMinimumSize = new Vector2(windowWidth - 40f, windowHeight - 100f),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            
            var listContainer = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            listContainer.AddThemeConstantOverride("separation", 4);
            
            // 尝试解析存档并显示卡牌
            bool isSinglePlayer = !record.IsMultiplayer;
            PopulateCardList(listContainer, saveContent, isSinglePlayer, savePointIndex, window);
            
            scrollContainer.AddChild(listContainer);
            content.AddChild(scrollContainer);
            
            // 添加关闭按钮
            var buttonRow = new HBoxContainer
            {
                CustomMinimumSize = new Vector2(0f, 40f),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            buttonRow.AddThemeConstantOverride("separation", 8);
            
            var closeButton = new Button
            {
                Text = L10n.Close,
                CustomMinimumSize = new Vector2(100f, 40f),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
            };
            closeButton.Pressed += () =>
            {
                window.Hide();
                window.QueueFree();
            };
            buttonRow.AddChild(closeButton);
            
            var openLogButton = new Button
            {
                Text = "Open Log",
                CustomMinimumSize = new Vector2(100f, 40f),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                TooltipText = "Open log directory"
            };
            openLogButton.Pressed += () =>
            {
                var appDataPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
                var gameLogsPath = Path.Combine(appDataPath, "SlayTheSpire2", "logs");
                if (Directory.Exists(gameLogsPath))
                {
                    System.Diagnostics.Process.Start("explorer.exe", gameLogsPath);
                }
                else
                {
                    System.Diagnostics.Process.Start("explorer.exe", appDataPath);
                }
            };
            buttonRow.AddChild(openLogButton);
            
            content.AddChild(buttonRow);
            
            window.AddChild(content);
            window.CloseRequested += () =>
            {
                window.Hide();
                window.QueueFree();
            };
            
            var gameRoot = NGame.Instance;
            if (gameRoot != null)
            {
                gameRoot.AddChild(window);
                window.PopupCentered();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to show deck dialog: {ex}");
            ShowFeedback("Failed to show deck");
        }
    }

    private static void PopulateCardList(VBoxContainer listContainer, string saveContent, bool isSinglePlayer, int savePointIndex, Window window)
    {
        try
        {
            // 尝试解压saveContent（如果是压缩的）
            string jsonContent = saveContent;
            try
            {
                // 检查是否是Base64编码的压缩数据
                if (!string.IsNullOrEmpty(saveContent) && saveContent.Length > 50)
                {
                    jsonContent = DecompressString(saveContent);
                }
            }
            catch (Exception ex)
            {
                jsonContent = saveContent;
            }
            
            // 打印JSON内容的前500个字符用于调试
            if (!string.IsNullOrEmpty(jsonContent))
            {
                var preview = jsonContent.Length > 500 ? jsonContent.Substring(0, 500) + "..." : jsonContent;
            }
            
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;
            
            // 尝试读取players[0].current_hp来确认JSON结构
            try
            {
                if (root.TryGetProperty("players", out JsonElement players) && players.ValueKind == JsonValueKind.Array && players.GetArrayLength() > 0)
                {
                    var firstPlayer = players[0];
                    
                    // 检查deck属性
                    if (firstPlayer.TryGetProperty("deck", out JsonElement deck))
                    {
                        if (deck.ValueKind == JsonValueKind.Array)
                        {
                        }
                        else
                        {
                        }
                    }
                    else
                    {
                    }
                    
                    if (firstPlayer.TryGetProperty("current_hp", out JsonElement currentHp))
                    {
                    }
                }
                else
                {
                }
            }
            catch (Exception ex)
            {
            }
            
            // 打印根节点的所有属性，帮助调试
            
            // 尝试多种方式查找卡牌列表
            JsonElement? cardsArray = null;
            string? cardsPath = null;
            
            // 方式1: players[0].deck (小写)
            if (root.TryGetProperty("players", out JsonElement playersElement) && playersElement.ValueKind == JsonValueKind.Array && playersElement.GetArrayLength() > 0)
            {
                var firstPlayer = playersElement[0];
                if (firstPlayer.TryGetProperty("deck", out JsonElement deckElement) && deckElement.ValueKind == JsonValueKind.Array)
                {
                    cardsArray = deckElement;
                    cardsPath = "players[0].deck";
                }
            }
            
            // 方式2: Players[0].Deck (大写 - 兼容旧代码)
            if (!cardsArray.HasValue)
            {
                if (root.TryGetProperty("Players", out JsonElement playersArray) && playersArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var player in playersArray.EnumerateArray())
                    {
                        if (player.TryGetProperty("Deck", out JsonElement deck) && deck.ValueKind == JsonValueKind.Array)
                        {
                            cardsArray = deck;
                            cardsPath = "Players[*].Deck";
                            break;
                        }
                        if (player.TryGetProperty("Cards", out JsonElement cards) && cards.ValueKind == JsonValueKind.Array)
                        {
                            cardsArray = cards;
                            cardsPath = "Players[*].Cards";
                            break;
                        }
                    }
                }
            }
            
            // 方式3: 直接在根节点找Deck或Cards
            if (!cardsArray.HasValue)
            {
                if (root.TryGetProperty("Deck", out JsonElement deck))
                {
                    cardsArray = deck;
                    cardsPath = "Deck";
                }
                else if (root.TryGetProperty("Cards", out JsonElement cards))
                {
                    cardsArray = cards;
                    cardsPath = "Cards";
                }
            }
            
            if (!cardsArray.HasValue || cardsArray.Value.ValueKind != JsonValueKind.Array || cardsArray.Value.GetArrayLength() == 0)
            {
                var noCardsLabel = new Label
                {
                    Text = L10n.NoCards,
                    CustomMinimumSize = new Vector2(0f, 40f),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                listContainer.AddChild(noCardsLabel);
                return;
            }
            
            // 显示卡牌列表
            int cardIndex = 0;
            foreach (var card in cardsArray.Value.EnumerateArray())
            {
                var cardRow = new HBoxContainer
                {
                    CustomMinimumSize = new Vector2(0f, 32f),
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
                };
                cardRow.AddThemeConstantOverride("separation", 8);
                
                // 获取卡牌名称
                string cardName = "Unknown Card";
                string cardId = "";
                int floorAdded = 0;
                int upgradeLevel = 0;
                string localizedName = null;
                
                try
                {
                    if (card.TryGetProperty("id", out JsonElement idProp))
                    {
                        cardId = idProp.GetString() ?? "";
                        cardName = cardId;
                    }
                    else if (card.TryGetProperty("Id", out JsonElement idProp2))
                    {
                        cardId = idProp2.GetString() ?? "";
                        cardName = cardId;
                    }
                    else if (card.TryGetProperty("Name", out JsonElement nameProp))
                    {
                        cardName = nameProp.GetString() ?? cardName;
                    }
                    else if (card.TryGetProperty("CardId", out JsonElement cardIdProp))
                    {
                        cardId = cardIdProp.GetString() ?? "";
                        cardName = cardId;
                    }
                    else if (card.TryGetProperty("CardName", out JsonElement cardNameProp))
                    {
                        cardName = cardNameProp.GetString() ?? cardName;
                    }
                    else
                    {
                        cardName = card.GetRawText().Substring(0, Math.Min(50, card.GetRawText().Length)) + "...";
                    }
                    
                    // 尝试获取本地化的卡牌名称
                    if (!string.IsNullOrEmpty(cardId))
                    {
                        // 卡牌ID格式通常是 "CARD.TREMBLE"，我们需要 "TREMBLE"
                        string key = cardId;
                        if (key.StartsWith("CARD."))
                        {
                            key = key.Substring(5);
                        }
                        
                        // 定义所有要尝试的本地化组合 - 参考CardModel.TitleLocString
                        var attempts = new List<(string table, string key)>()
                        {
                            ("cards", key + ".title"),
                            ("cards", cardId + ".title"),
                            ("cards", key),
                            ("cards", cardId),
                            ("card_names", key + ".title"),
                            ("card_names", cardId + ".title"),
                            ("card_names", key),
                            ("card_names", cardId),
                            ("ui", key + ".title"),
                            ("ui", cardId + ".title"),
                            ("ui", key),
                            ("ui", cardId),
                        };
                        
                        // 尝试每个组合
                        foreach (var (table, locKey) in attempts)
                        {
                            try
                            {
                                var locString = new LocString(table, locKey);
                                string locResult = locString.GetFormattedText();
                                
                                // 如果不是占位符，就使用它
                                if (!string.IsNullOrEmpty(locResult) && !locResult.StartsWith("[") && !locResult.StartsWith("("))
                                {
                                    localizedName = locResult;
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                            }
                        }
                    }
                    
                    // 获取floor_added_to_deck
                    if (card.TryGetProperty("floor_added_to_deck", out JsonElement floorProp))
                    {
                        floorAdded = floorProp.GetInt32();
                    }
                    
                    // 获取current_upgrade_level
                    if (card.TryGetProperty("current_upgrade_level", out JsonElement upgradeProp))
                    {
                        upgradeLevel = upgradeProp.GetInt32();
                    }
                }
                catch { }
                
                // 显示卡牌名称和参数
                string displayText = cardId;
                if (!string.IsNullOrEmpty(localizedName))
                {
                    displayText += $" ({localizedName})";
                }
                if (floorAdded > 0 || upgradeLevel > 0)
                {
                    displayText += $" [F:{floorAdded}";
                    if (upgradeLevel > 0)
                    {
                        displayText += $", +{upgradeLevel}";
                    }
                    displayText += "]";
                }
                
                var cardLabel = new Label
                {
                    Text = displayText,
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                cardRow.AddChild(cardLabel);
                
                // 如果是单人模式，添加删除按钮
                if (isSinglePlayer)
                {
                    int capturedCardIndex = cardIndex;
                    var deleteCardButton = new Button
                    {
                        Text = L10n.DeleteCard,
                        CustomMinimumSize = new Vector2(70f, 32f)
                    };
                    deleteCardButton.Pressed += () =>
                    {
                        DeleteCardFromSave(savePointIndex, capturedCardIndex, window);
                    };
                    cardRow.AddChild(deleteCardButton);
                }
                
                listContainer.AddChild(cardRow);
                cardIndex++;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to populate card list: {ex}");
            var errorLabel = new Label
            {
                Text = "Failed to load cards",
                CustomMinimumSize = new Vector2(0f, 40f)
            };
            listContainer.AddChild(errorLabel);
        }
    }

    private static void DeleteCardFromSave(int savePointIndex, int cardIndex, Window window)
    {
        try
        {
            Initialize();
            
            SavePointRecord? record;
            lock (_lock)
            {
                if (savePointIndex < 0 || savePointIndex >= _savePoints.Count)
                {
                    ShowFeedback(L10n.FeedbackInvalidCheckpoint);
                    return;
                }
                record = _savePoints[savePointIndex];
            }
            
            if (string.IsNullOrEmpty(record?.SaveFileName))
            {
                ShowFeedback(L10n.FeedbackNoSaveData);
                return;
            }
            
            if (record.IsMultiplayer)
            {
                ShowFeedback(L10n.OnlySinglePlayerCanDelete);
                return;
            }
            
            var savePointPath = Path.Combine(_savePointsDir, record.SaveFileName);
            if (!File.Exists(savePointPath))
            {
                ShowFeedback(L10n.FeedbackFileNotFound);
                return;
            }
            
            var saveContent = File.ReadAllText(savePointPath);
            
            // 解析JSON并删除卡牌
            using var doc = JsonDocument.Parse(saveContent);
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            
            ProcessAndDeleteCard(doc.RootElement, writer, cardIndex);
            writer.Flush();
            stream.Position = 0;
            
            var modifiedJson = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            File.WriteAllText(savePointPath, modifiedJson);
            
            ShowFeedback(L10n.CardDeleted);
            
            // 刷新对话框
            window.Hide();
            window.QueueFree();
            ShowDeckDialog(savePointIndex);
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to delete card: {ex}");
            ShowFeedback("Failed to delete card");
        }
    }

    private static void ProcessAndDeleteCard(JsonElement element, Utf8JsonWriter writer, int targetCardIndex)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            
            // 先检查是否有deck属性
            bool hasDeck = false;
            foreach (var prop in element.EnumerateObject())
            {
                if ((prop.Name == "deck" || prop.Name == "Deck") && prop.Value.ValueKind == JsonValueKind.Array)
                {
                    hasDeck = true;
                    break;
                }
            }
            
            // 如果有deck，处理它
            if (hasDeck)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    if ((prop.Name == "deck" || prop.Name == "Deck") && prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        writer.WritePropertyName(prop.Name);
                        writer.WriteStartArray();
                        
                        int currentIndex = 0;
                        foreach (var card in prop.Value.EnumerateArray())
                        {
                            if (currentIndex != targetCardIndex)
                            {
                                card.WriteTo(writer);
                            }
                            currentIndex++;
                        }
                        
                        writer.WriteEndArray();
                    }
                    else
                    {
                        writer.WritePropertyName(prop.Name);
                        ProcessAndDeleteCard(prop.Value, writer, targetCardIndex);
                    }
                }
            }
            else
            {
                // 没有直接的deck，检查players数组
                bool processedPlayers = false;
                foreach (var prop in element.EnumerateObject())
                {
                    if ((prop.Name == "players" || prop.Name == "Players") && prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        processedPlayers = true;
                        writer.WritePropertyName(prop.Name);
                        writer.WriteStartArray();
                        
                        foreach (var player in prop.Value.EnumerateArray())
                        {
                            writer.WriteStartObject();
                            
                            bool playerHasDeck = false;
                            foreach (var playerProp in player.EnumerateObject())
                            {
                                if ((playerProp.Name == "deck" || playerProp.Name == "Deck") && playerProp.Value.ValueKind == JsonValueKind.Array)
                                {
                                    playerHasDeck = true;
                                    break;
                                }
                            }
                            
                            if (playerHasDeck)
                            {
                                foreach (var playerProp in player.EnumerateObject())
                                {
                                    if ((playerProp.Name == "deck" || playerProp.Name == "Deck") && playerProp.Value.ValueKind == JsonValueKind.Array)
                                    {
                                        writer.WritePropertyName(playerProp.Name);
                                        writer.WriteStartArray();
                                        
                                        int currentIndex = 0;
                                        foreach (var card in playerProp.Value.EnumerateArray())
                                        {
                                            if (currentIndex != targetCardIndex)
                                            {
                                                card.WriteTo(writer);
                                            }
                                            currentIndex++;
                                        }
                                        
                                        writer.WriteEndArray();
                                    }
                                    else
                                    {
                                        writer.WritePropertyName(playerProp.Name);
                                        playerProp.Value.WriteTo(writer);
                                    }
                                }
                            }
                            else
                            {
                                foreach (var playerProp in player.EnumerateObject())
                                {
                                    writer.WritePropertyName(playerProp.Name);
                                    playerProp.Value.WriteTo(writer);
                                }
                            }
                            
                            writer.WriteEndObject();
                        }
                        
                        writer.WriteEndArray();
                    }
                    else
                    {
                        writer.WritePropertyName(prop.Name);
                        ProcessAndDeleteCard(prop.Value, writer, targetCardIndex);
                    }
                }
            }
            
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray())
            {
                ProcessAndDeleteCard(item, writer, targetCardIndex);
            }
            writer.WriteEndArray();
        }
        else
        {
            element.WriteTo(writer);
        }
    }

    private static JsonElement? FindCardsArray(JsonElement element, int depth)
    {
        // 防止递归过深
        if (depth > 10) return null;
        
        try
        {
            // 如果是对象，检查是否有Deck或Cards属性
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("Deck", out JsonElement deck) && deck.ValueKind == JsonValueKind.Array && deck.GetArrayLength() > 0)
                {
                    Log.Info($"[KKSavePoint] Found Deck array with {deck.GetArrayLength()} cards at depth {depth}");
                    return deck;
                }
                if (element.TryGetProperty("Cards", out JsonElement cards) && cards.ValueKind == JsonValueKind.Array && cards.GetArrayLength() > 0)
                {
                    Log.Info($"[KKSavePoint] Found Cards array with {cards.GetArrayLength()} cards at depth {depth}");
                    return cards;
                }
                
                // 递归检查所有子属性
                foreach (var prop in element.EnumerateObject())
                {
                    var result = FindCardsArray(prop.Value, depth + 1);
                    if (result.HasValue) return result;
                }
            }
            
            // 如果是数组，递归检查所有元素
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    var result = FindCardsArray(item, depth + 1);
                    if (result.HasValue) return result;
                }
            }
        }
        catch { }
        
        return null;
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCardPlayed))]
    public static class HookBeforeCardPlayedPatch
    {
        public static void Postfix(CombatState combatState, dynamic cardPlay)
        {
            if (!FeatureSettingsStore.Current.EnableSavePoint) return;

            if (SavePointFeature._isReplaying)
            {
                return;
            }

            try
            {
                // 在 BeforeCardPlayed 时记录，因为此时 Target 还有值
                if (cardPlay?.Card == null) return;

                var player = LocalContext.GetMe(combatState.RunState);
                if (player == null) return;

                string cardIdStr = "unknown";
                string cardTitle = "Unknown Card";
                int energyCost = 0;
                try
                {
                    var cardModel = cardPlay.Card;
                    var modelId = cardModel.Id;
                    if (modelId != null)
                    {
                        cardIdStr = modelId.Entry ?? modelId.ToString();
                    }
                    try
                    {
                        cardTitle = cardModel.Title ?? cardIdStr;
                    }
                    catch
                    {
                        cardTitle = cardIdStr;
                    }
                    try
                    {
                        var cardEnergyCost = cardModel.EnergyCost;
                        if (cardEnergyCost != null)
                        {
                            energyCost = cardEnergyCost.Amount;
                        }
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    Log.Warn($"[KKSavePoint] Error getting card info in BeforeCardPlayed: {ex.Message}");
                }

                var record = new CardPlayRecord
                {
                    ActionIndex = _currentActionIndex++,
                    CardId = cardIdStr,
                    CardName = cardTitle,
                    EnergyCost = energyCost,
                    Timestamp = DateTime.Now,
                    SelectedCardId = _pendingSelectedCardId,
                    SelectedCardIndex = _pendingSelectedCardIndex
                };
                
                // 立即清空，防止错误地附加到后续卡牌
                _pendingSelectedCardId = null;
                _pendingSelectedCardIndex = null;

                // 在 BeforeCardPlayed 时 Target 应该还有值
                if (cardPlay?.Target != null)
                {
                    try
                    {
                        // Creature 没有 Id 属性，有 ModelId 属性
                        string targetId = cardPlay.Target?.ModelId?.Entry ?? cardPlay.Target?.ModelId?.ToString() ?? "unknown";
                        record.TargetIds.Add(targetId);

                        // 使用 LINQ 获取目标在 Creatures 列表中的索引
                        int targetIndex = -1;
                        try
                        {
                            var combatStateTmp = combatState;
                            if (combatStateTmp != null)
                            {
                                var creatures = combatStateTmp.Creatures;
                                if (creatures != null)
                                {
                                    int idx = 0;
                                    foreach (var c in creatures)
                                    {
                                        if (c == cardPlay.Target)
                                        {
                                            targetIndex = idx;
                                            break;
                                        }
                                        idx++;
                                    }
                                }
                            }
                        }
                        catch { }
                        record.TargetPositions.Add(targetIndex);

                        Log.Info($"[KKSavePoint] BeforeCardPlayed - Target recorded: {targetId}, Index: {targetIndex}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"[KKSavePoint] Error recording target in BeforeCardPlayed: {ex.Message}");
                        record.TargetIds.Add("unknown");
                        record.TargetPositions.Add(-1);
                    }
                }
                else
                {
                    Log.Info($"[KKSavePoint] BeforeCardPlayed - No target for card: {cardTitle}");
                }

                _currentTurnCardPlays.Add(record);
                Log.Info($"[KKSavePoint] Card recorded (BeforeCardPlayed): {record.CardName}, Targets: {record.TargetIds.Count}");
            }
            catch (Exception ex)
            {
                Log.Error($"[KKSavePoint] Failed to record BeforeCardPlayed: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed))]
    public static class HookAfterCardPlayedPatch
    {
        public static void Postfix(CombatState combatState, dynamic choiceContext, dynamic cardPlay)
        {
            if (!FeatureSettingsStore.Current.EnableSavePoint) return;

            if (SavePointFeature._isReplaying)
            {
                return;
            }

            try
            {
                if (!string.IsNullOrEmpty(_pendingSelectedCardId) && _currentTurnCardPlays.Count > 0)
                {
                    var lastRecord = _currentTurnCardPlays[_currentTurnCardPlays.Count - 1];
                    lastRecord.SelectedCardId = _pendingSelectedCardId;
                    if (_pendingSelectedCardIndex.HasValue)
                    {
                        lastRecord.SelectedCardIndex = _pendingSelectedCardIndex;
                    }
                    Log.Info($"[KKSavePoint] AfterCardPlayed - Attached selected card: {_pendingSelectedCardId} to card play: {lastRecord.CardName}");
                    _pendingSelectedCardId = null;
                    _pendingSelectedCardIndex = null;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[KKSavePoint] Failed in AfterCardPlayed: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardChangedPiles))]
    public static class HookAfterCardChangedPilesPatch
    {
        public static void Postfix(IRunState runState, CombatState? combatState, dynamic card, MegaCrit.Sts2.Core.Entities.Cards.PileType oldPile, dynamic source)
        {
            if (!FeatureSettingsStore.Current.EnableSavePoint) return;

            if (SavePointFeature._isReplaying)
            {
                return;
            }

            try
            {
                string? cardIdStr = null;
                try
                {
                    var modelId = card?.Id;
                    if (modelId != null)
                    {
                        cardIdStr = modelId.Entry ?? modelId.ToString();
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(cardIdStr))
                {
                    return;
                }

                if (oldPile == MegaCrit.Sts2.Core.Entities.Cards.PileType.Discard)
                {
                    _pendingSelectedCardId = cardIdStr;
                    Log.Info($"[KKSavePoint] AfterCardChangedPiles - Card moved from Discard: {cardIdStr}, oldPile: {oldPile}");
                }
                else if (oldPile == MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand)
                {
                    _pendingSelectedCardId = cardIdStr;
                    Log.Info($"[KKSavePoint] AfterCardChangedPiles - Card moved from Hand: {cardIdStr}, oldPile: {oldPile}");
                }
                else if (oldPile == MegaCrit.Sts2.Core.Entities.Cards.PileType.Draw)
                {
                    Log.Info($"[KKSavePoint] AfterCardChangedPiles - Card moved from Draw: {cardIdStr}, oldPile: {oldPile}");
                }
                else
                {
                    Log.Info($"[KKSavePoint] AfterCardChangedPiles - Card moved from other pile: {cardIdStr}, oldPile: {oldPile}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[KKSavePoint] Failed in AfterCardChangedPiles: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(CardSelectCmd), "LogChoice")]
    public static class HookCardSelectLogChoicePatch
    {
        public static void Postfix(Player player, IEnumerable<CardModel?> cards)
        {
            if (!FeatureSettingsStore.Current.EnableSavePoint) return;

            if (SavePointFeature._isReplaying)
            {
                return;
            }

            try
            {
                Log.Info($"[KKSavePoint] CardSelectCmd.LogChoice called - Player: {player?.NetId}");

                foreach (var card in cards)
                {
                    if (card != null)
                    {
                        var cardId = card.Id;
                        string? cardIdStr = cardId?.Entry ?? cardId?.ToString();
                        if (!string.IsNullOrEmpty(cardIdStr))
                        {
                            _pendingSelectedCardId = cardIdStr;
                            Log.Info($"[KKSavePoint] CardSelectCmd.LogChoice - Player {player.NetId} selected card: {cardIdStr}");

                            if (_currentTurnCardPlays.Count > 0)
                            {
                                var lastRecord = _currentTurnCardPlays[_currentTurnCardPlays.Count - 1];
                                lastRecord.SelectedCardId = cardIdStr;
                                Log.Info($"[KKSavePoint] LogChoice - Attached selected card to: {lastRecord.CardName}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[KKSavePoint] Failed in CardSelectCmd.LogChoice patch: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(CardSelectCmd), "FromSimpleGrid")]
    public static class HookCardSelectFromSimpleGridPatch
    {
        public static bool Prefix(
            PlayerChoiceContext context,
            IReadOnlyList<CardModel> cardsIn,
            Player player,
            CardSelectorPrefs prefs,
            ref Task<IEnumerable<CardModel>> __result)
        {
            if (!FeatureSettingsStore.Current.EnableSavePoint) return true;
            
            if (SavePointFeature._isReplaying && SavePointFeature._currentReplaySelector != null)
            {
                Log.Info($"[KKSavePoint] FromSimpleGrid intercepted during replay, using our selector");
                try
                {
                    var task = SavePointFeature._currentReplaySelector.GetSelectedCards(cardsIn, prefs.MinSelect, prefs.MaxSelect);
                    __result = task;
                    Log.Info($"[KKSavePoint] Returning result from our selector");
                    return false; // Skip original method
                }
                catch (Exception ex)
                {
                    Log.Error($"[KKSavePoint] Failed in FromSimpleGrid prefix: {ex}");
                }
            }
            Log.Info($"[KKSavePoint] FromSimpleGrid not intercepted, isReplaying: {SavePointFeature._isReplaying}, selector: {SavePointFeature._currentReplaySelector != null}");
            return true; // Continue with original method
        }
    }

    [HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Add), new Type[] { typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool) })]
    public static class HookCardPileCmdAddPatch
    {
        public static void Postfix(CardModel card, PileType newPileType, CardPilePosition position, AbstractModel? source, bool skipVisuals)
        {
            if (!FeatureSettingsStore.Current.EnableSavePoint) return;

            if (SavePointFeature._isReplaying)
            {
                return;
            }

            try
            {
                var cardId = card?.Id;
                string? cardIdStr = cardId?.Entry ?? cardId?.ToString();
                if (!string.IsNullOrEmpty(cardIdStr))
                {
                    Log.Info($"[KKSavePoint] CardPileCmd.Add - Card: {cardIdStr}, to: {newPileType}, position: {position}, source: {source}");

                    if (position == CardPilePosition.Top && newPileType == PileType.Draw)
                    {
                        _pendingSelectedCardId = cardIdStr;
                        Log.Info($"[KKSavePoint] CardPileCmd.Add - Card moved to top of Draw pile: {cardIdStr}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[KKSavePoint] Failed in CardPileCmd.Add patch: {ex}");
            }
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

                    var turnData = new TurnPlaybackData
                    {
                        TurnNumber = combatState.RoundNumber,
                        CardPlays = new List<CardPlayRecord>(_currentTurnCardPlays),
                        PlayerHpBeforeTurn = _playerHpBeforeTurn,
                        PlayerHpAfterTurn = playerHpAfterTurn,
                        BlockBeforeTurn = _playerBlockBeforeTurn,
                        BlockAfterTurn = playerBlockAfterTurn,
                        MonsterHpBeforeTurn = new Dictionary<string, int>(_monsterHpBeforeTurn),
                        MonsterHpAfterTurn = monsterHpAfterTurn,
                        Gold = player?.Gold ?? 0,
                        Timestamp = DateTime.Now
                    };

                    _turnPlaybackData.Add(turnData);
                    Log.Info($"[KKSavePoint] Turn saved: Turn {turnData.TurnNumber}, Card plays: {turnData.CardPlays.Count}");

                    _currentTurnCardPlays.Clear();
                    _currentActionIndex = 0;
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
                _playerHpBeforeTurn = (int)player.Creature.CurrentHp;
                _playerBlockBeforeTurn = (int)player.Creature.Block;
                _currentActionIndex = 0;

                _monsterHpBeforeTurn.Clear();
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
                                _monsterHpBeforeTurn[enemyId] = hp;
                            }
                            catch
                            {
                                _monsterHpBeforeTurn[enemy.GetHashCode().ToString()] = 0;
                            }
                        }
                    }
                }

                Log.Info($"[KKSavePoint] Turn start recorded: HP {_playerHpBeforeTurn}, Block {_playerBlockBeforeTurn}");
            }
            catch (Exception ex)
            {
                Log.Error($"[KKSavePoint] Failed to record turn start: {ex}");
            }
        }
    }

}

public partial class SavePointDropTarget : Control
{

    private Action<string[]>? _onFilesDropped;
    public Window? _Window { get; set; }



    public void SetDropCallback(Action<string[]> callback)

    {

        _onFilesDropped = callback;

    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed)
        {
            if (keyEvent.Keycode == Key.Escape)
            {
                // 关闭Window
                if (_Window != null)
                {
                    _Window.Hide();
                    _Window.QueueFree();
                }
            }
        }
    }



    public override bool _CanDropData(Vector2 atPosition, Variant data)

    {

        if (data.VariantType == Variant.Type.Array)

        {

            var files = data.AsStringArray();

            if (files != null)

            {

                foreach (var file in files)

                {

                    if (file.EndsWith(".txt", System.StringComparison.OrdinalIgnoreCase) ||

                        file.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))

                    {

                        return true;

                    }

                }

            }

        }

        return false;

    }



    public override void _DropData(Vector2 atPosition, Variant data)

    {

        if (data.VariantType == Variant.Type.Array)

        {

            var files = data.AsStringArray();

            if (files != null && files.Length > 0)

            {

                var validFiles = files.Where(f =>

                    f.EndsWith(".txt", System.StringComparison.OrdinalIgnoreCase) ||

                    f.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase)).ToArray();



                if (validFiles.Length > 0)

                {

                    _onFilesDropped?.Invoke(validFiles);

                }

            }

        }

    }

}



[HarmonyPatch(typeof(NTopBar), nameof(NTopBar.Initialize))]

public static class NTopBarInitializeSavePointPatch

{

    public static void Postfix(NTopBar __instance)

    {

        try

        {

            SavePointFeature.AttachToTopBar(__instance);

        }

        catch (Exception ex)

        {

            Log.Error($"[KKSavePoint] Failed to add SavePoint button: {ex}");

        }

    }

}



[HarmonyPatch(typeof(Hook), nameof(Hook.AfterRoomEntered))]

public static class SavePointAfterRoomEnteredPatch

{

    public static void Postfix(IRunState runState, AbstractRoom room, ref Task __result)

    {

        if (!FeatureSettingsStore.Current.EnableSavePoint)

        {

            return;

        }



        int floor = 0;
        try
        {
            // 尝试从runState获取楼层信息 - 更全面的探索
            var runStateObj = runState as dynamic;
            if (runStateObj != null)
            {
                // 尝试方法1: Map相关属性（FloorIcon可能使用的路径）
                try
                {
                    var map = runStateObj.Map;
                    if (map != null)
                    {
                        // 尝试不同的Map属性
                        try { if (map.CurrentFloor > 0) floor = map.CurrentFloor; } catch { }
                        try { if (map.Floor > 0) floor = map.Floor; } catch { }
                        try { if (map.ActFloor > 0) floor = map.ActFloor; } catch { }

                        // 尝试Act相关属性
                        try
                        {
                            var currentAct = map.CurrentAct;
                            if (currentAct != null)
                            {
                                try { if (currentAct.CurrentFloor > 0) floor = currentAct.CurrentFloor; } catch { }
                                try { if (currentAct.Floor > 0) floor = currentAct.Floor; } catch { }
                                try { if (currentAct.ActFloor > 0) floor = currentAct.ActFloor; } catch { }
                            }
                        }
                        catch { }

                        // 尝试Map的其他可能属性
                        try { if (map.CurrentActFloor > 0) floor = map.CurrentActFloor; } catch { }
                        try { if (map.ActiveFloor > 0) floor = map.ActiveFloor; } catch { }
                    }
                }
                catch { }

                // 尝试方法2: RunState直接属性
                try { if (runStateObj.CurrentFloor > 0) floor = runStateObj.CurrentFloor; } catch { }
                try { if (runStateObj.Floor > 0) floor = runStateObj.Floor; } catch { }
                try { if (runStateObj.ActFloor > 0) floor = runStateObj.ActFloor; } catch { }
                try { if (runStateObj.CurrentActFloor > 0) floor = runStateObj.CurrentActFloor; } catch { }
                try { if (runStateObj.ActiveFloor > 0) floor = runStateObj.ActiveFloor; } catch { }

                // 尝试方法3: 其他可能的路径
                try
                {
                    var gameState = runStateObj.GameState;
                    if (gameState != null)
                    {
                        try { if (gameState.CurrentFloor > 0) floor = gameState.CurrentFloor; } catch { }
                        try { if (gameState.Floor > 0) floor = gameState.Floor; } catch { }
                    }
                }
                catch { }

                try
                {
                    var state = runStateObj.State;
                    if (state != null)
                    {
                        try { if (state.CurrentFloor > 0) floor = state.CurrentFloor; } catch { }
                        try { if (state.Floor > 0) floor = state.Floor; } catch { }
                    }
                }
                catch { }
            }

            // 尝试从room获取楼层信息
            if (floor == 0 && room != null)
            {
                var roomObj = room as dynamic;
                try { if (roomObj.Floor > 0) floor = roomObj.Floor; } catch { }
                try { if (roomObj.CurrentFloor > 0) floor = roomObj.CurrentFloor; } catch { }
                try { if (roomObj.RoomFloor > 0) floor = roomObj.RoomFloor; } catch { }
                try { if (roomObj.ActFloor > 0) floor = roomObj.ActFloor; } catch { }
            }
        }
        catch { }

        __result = RecordAfterRoomEnteredAsync(__result, room, floor);

    }



    private static async Task RecordAfterRoomEnteredAsync(Task originalTask, AbstractRoom room, int floor)

    {

        await originalTask;



        if (room == null)

        {

            return;

        }



        string roomName = room.GetType().Name;

        if (room is CombatRoom)

        {

            roomName = "Combat";

        }

        else if (room is TreasureRoom)

        {

            roomName = "Treasure";

        }

        else if (room is MerchantRoom)

        {

            roomName = "Merchant";

        }

        else if (roomName.Contains("Rest"))

        {

            roomName = "Rest Site";

        }

        else if (room is EventRoom)

        {

            roomName = "Event";

        }


        SavePointFeature.RecordSavePoint(roomName, floor);

    }

}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetUpCombat))]
public static class SavePointCombatSetUpPatch
{
    public static void Postfix(CombatState state)
    {
        if (!FeatureSettingsStore.Current.EnableSavePoint) return;
        
        Log.Info($"[KKSavePoint] SetUpCombat called, _turnRecordLoadCount = {SavePointFeature._turnRecordLoadCount}");
        
        CombatManager.Instance.TurnStarted += OnTurnStarted;
        CombatManager.Instance.CombatEnded += OnCombatEnded;
    }
    
    private static void OnTurnStarted(CombatState state)
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
        
        // 只有在非加载回合记录且非回放时才清空回合记录（新战斗开始时）
        if (!SavePointFeature._isLoadingTurnRecordFlag && !SavePointFeature._isReplaying && state.CurrentSide == CombatSide.Player && state.RoundNumber == 1)
        {
            Log.Info($"[KKSavePoint] Clearing turn records in OnTurnStarted (new combat)");
            SavePointFeature.ClearTurnRecords();
        }
        else
        {
            Log.Info($"[KKSavePoint] Skipping clear turn records, _isLoadingTurnRecordFlag = {SavePointFeature._isLoadingTurnRecordFlag}, _isReplaying = {SavePointFeature._isReplaying}");
        }
        
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
            SavePointFeature.RecordTurnSavePoint(roomName, floor, state.RoundNumber);
        }
        else
        {
            Log.Info($"[KKSavePoint] Not player turn, skipping - CurrentSide: {state.CurrentSide}");
        }
    }
    
    private static void OnCombatEnded(CombatRoom room)
    {
        try
        {
            CombatManager.Instance.TurnStarted -= OnTurnStarted;
            CombatManager.Instance.CombatEnded -= OnCombatEnded;
        }
        catch { }
    }

}


