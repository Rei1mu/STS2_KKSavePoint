using System;

using System.Collections.Generic;

using System.IO;

using System.Text.Json;

using System.Threading.Tasks;

using Godot;

using HarmonyLib;

using KKSavePoint.Core;

using MegaCrit.Sts2.Core.Commands;

using MegaCrit.Sts2.Core.Context;

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



namespace KKSavePoint.Features;



public static class SavePointFeature

{

    private const string SavePointButtonName = "KKSavePointButton";

    private const string SavePointDialogName = "KKSavePointDialog";

    private const int MaxSavePoints = 2000;

    private const string SaveFileName = "kksavepoint_savepoints.json";



    private static readonly List<SavePointRecord> _savePoints = new();

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
        public static string ExportPath => IsChineseLocale() ? "导出路径: 游戏目录" : "Export path: GameFolder";
        public static string TooltipClickToLoad => IsChineseLocale() ? "点击加载此存档点" : "Click to load this checkpoint";
        public static string TooltipCharacter => IsChineseLocale() ? "角色" : "Character";
        public static string TooltipDifficulty => IsChineseLocale() ? "难度" : "Difficulty";
        public static string TooltipSavedAt => IsChineseLocale() ? "保存时间" : "Saved at";
        public static string TooltipCopyToClipboard => IsChineseLocale() ? "复制此存档到剪贴板" : "Copy this checkpoint to clipboard";
        public static string TooltipExportToFile => IsChineseLocale() ? "导出此存档到文件" : "Export this checkpoint to file";
        public static string TooltipDelete => IsChineseLocale() ? "删除此存档点" : "Delete this checkpoint";
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
            FocusMode = Control.FocusModeEnum.All
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



        var dialog = new AcceptDialog

        {

            Name = SavePointDialogName,

            Title = L10n.Title,

            Exclusive = true

        };



        var screenSize = DisplayServer.ScreenGetSize();

        var dialogWidth = (int)(screenSize.X * 0.5f);

        var dialogHeight = (int)(screenSize.Y * 0.6f);



        var dropTarget = new SavePointDropTarget

        {

            Name = "DropTarget",

            CustomMinimumSize = new Vector2(dialogWidth, dialogHeight),

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

                dialog.Hide();

                dialog.QueueFree();

                ShowSavePointDialog();

            }

        });



        var content = new VBoxContainer

        {

            CustomMinimumSize = new Vector2(dialogWidth - 60f, 0f),

            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill

        };

        content.AddThemeConstantOverride("separation", 8);



        var scrollContainer = new ScrollContainer

        {

            CustomMinimumSize = new Vector2(dialogWidth - 80f, dialogHeight - 150f),

            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,

            SizeFlagsVertical = Control.SizeFlags.ExpandFill

        };



        var listContainer = new VBoxContainer

        {

            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill

        };

        listContainer.AddThemeConstantOverride("separation", 4);



        int count;

        lock (_lock)

        {

            count = _savePoints.Count;

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

                    

                    var row = new HBoxContainer

                    {

                        CustomMinimumSize = new Vector2(0f, 36f),

                        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill

                    };

                    row.AddThemeConstantOverride("separation", 4);



                    var hashText = string.IsNullOrEmpty(record.Hash) ? "-------" : record.Hash;

                    var charText = string.IsNullOrEmpty(record.CharacterName) ? "?" : record.CharacterName;

                    var diffText = string.IsNullOrEmpty(record.Difficulty) ? "?" : record.Difficulty;

                    var itemButton = new Button
                    {
                        Text = $"[{record.Index}][{hashText}][{charText}][{diffText}][F{record.Floor}] {record.RoomName} | HP {record.CurrentHp}/{record.MaxHp} | Gold {record.Gold} | {record.Timestamp:HH:mm:ss}",
                        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                        TooltipText = $"{L10n.TooltipClickToLoad}.\n{L10n.TooltipCharacter}: {record.CharacterName}\n{L10n.TooltipDifficulty}: {record.Difficulty}\n{L10n.TooltipSavedAt} {record.Timestamp:yyyy-MM-dd HH:mm:ss}"
                    };

                    int capturedIndex = i;

                    itemButton.Pressed += () =>
                    {
                        dialog.Hide();
                        dialog.QueueFree();
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
                        dialog.Hide();
                        dialog.QueueFree();
                        ShowSavePointDialog();
                    };

                    row.AddChild(deleteButton);



                    listContainer.AddChild(row);

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
            CustomMinimumSize = new Vector2(150f, 32f),
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
                dialog.Hide();
                dialog.QueueFree();
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
            CustomMinimumSize = new Vector2(80f, 32f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
        };
        clearButton.Pressed += () =>
        {
            ClearAllSavePoints();
            dialog.Hide();
            dialog.QueueFree();
            ShowFeedback(L10n.FeedbackAllCleared);
        };
        buttonRow.AddChild(clearButton);

        var closeButton = new Button
        {
            Text = L10n.Close,
            CustomMinimumSize = new Vector2(80f, 32f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
        };
        closeButton.Pressed += () =>
        {
            dialog.Hide();
            dialog.QueueFree();
        };
        buttonRow.AddChild(closeButton);

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

        dialog.AddChild(dropTarget);

        gameRoot.AddChild(dialog);



        dialog.PopupCentered(new Vector2I(dialogWidth, dialogHeight));

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

                var foundFiles = Directory.GetFiles(sts2Dir, "current_run.save", SearchOption.AllDirectories);

                if (foundFiles.Length > 0)

                {

                    gameSavePath = foundFiles[0];

                }

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

                SaveFileName = saveFileName

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

                var foundFiles = Directory.GetFiles(sts2Dir, "current_run.save", SearchOption.AllDirectories);

                if (foundFiles.Length > 0)

                {

                    gameSavePath = foundFiles[0];

                }

            }



            if (string.IsNullOrEmpty(gameSavePath))

            {

                Log.Error($"[KKSavePoint] Cannot find game save path");

                ShowFeedback(L10n.FeedbackFailedToLoad);

                return;

            }



            Log.Info($"[KKSavePoint] Copying save point to: {gameSavePath}");

            File.Copy(savePointPath, gameSavePath, true);



            var saveData = SaveManager.Instance.LoadRunSave();

            if (saveData?.SaveData == null)

            {

                Log.Error($"[KKSavePoint] Failed to load save data from copied file");

                ShowFeedback(L10n.FeedbackFailedToLoad);

                return;

            }



            RunManager.Instance.ActionQueueSet.Reset();

            NRunMusicController.Instance?.StopMusic();

            

            if (isMultiplayer)

            {

                Log.Info("[KKSavePoint] Multiplayer rollback: attempting to preserve multiplayer session...");

                

                try

                {

                    var runState = RunState.FromSerializable(saveData.SaveData);

                    

                    RunManager.Instance.CleanUp();

                    

                    Log.Info("[KKSavePoint] Cleaned up, reloading run state for multiplayer...");

                    

                    RunManager.Instance.SetUpSavedSinglePlayer(runState, saveData.SaveData);

                    

                    SfxCmd.Play(runState.Players[0].Character.CharacterTransitionSfx);

                    TaskHelper.RunSafely(NGame.Instance.LoadRun(runState, null));

                    

                    Log.Info($"[KKSavePoint] Multiplayer rollback completed. Other players should sync automatically.");

                    ShowFeedback(L10n.FeedbackMultiplayerRollbackDone);

                }

                catch (Exception mpEx)

                {

                    Log.Error($"[KKSavePoint] Multiplayer rollback failed, falling back to single player: {mpEx}");

                    

                    RunManager.Instance.CleanUp();

                    

                    var runState = RunState.FromSerializable(saveData.SaveData);

                    RunManager.Instance.SetUpSavedSinglePlayer(runState, saveData.SaveData);

                    SfxCmd.Play(runState.Players[0].Character.CharacterTransitionSfx);

                    NGame.Instance.ReactionContainer.InitializeNetworking(new NetSingleplayerGameService());

                    TaskHelper.RunSafely(NGame.Instance.LoadRun(runState, null));

                    

                    ShowFeedback(L10n.FeedbackRollbackSinglePlayer);

                }

            }

            else

            {

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

                        SaveFileName = saveFileName

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

                        SaveFileName = saveFileName

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



    private static void ShowFeedback(string text)
    {
        Log.Info($"[KKSavePoint] SavePoint feedback: {text}");
        
        // 尝试更新状态标签
        if (_statusLabel != null)
        {
            _statusLabel.Text = text;
        }
        
        // 使用全屏文字特效显示反馈（与原版GambleButtonFeature一致）
        try
        {
            var vfx = NFullscreenTextVfx.Create($"[KKSavePoint] {text}");
            if (vfx != null)
            {
                NGame.Instance?.AddChildSafely(vfx);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to show feedback vfx: {ex}");
        }
    }

}



public partial class SavePointDropTarget : Control

{

    private Action<string[]>? _onFilesDropped;



    public void SetDropCallback(Action<string[]> callback)

    {

        _onFilesDropped = callback;

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

