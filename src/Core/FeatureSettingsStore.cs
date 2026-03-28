using System;
using System.IO;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace KKSavePoint.Core;

public static class FeatureSettingsStore
{
    private const string SettingsPath = "user://kksavepoint_settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly object LockObject = new();

    private static FeatureSettings _current = FeatureSettings.EnabledByDefault();

    public static FeatureSettings Current
    {
        get
        {
            lock (LockObject)
            {
                return _current;
            }
        }
    }

    public static void Initialize()
    {
        lock (LockObject)
        {
            _current = LoadInternal();
            Log.Info($"[KKSavePoint] Settings initialized from '{ResolveAbsolutePath()}': {_current}");
        }
    }

    public static void Update(Action<FeatureSettings> mutate)
    {
        lock (LockObject)
        {
            var before = _current.ToString();
            mutate(_current);
            Log.Info($"[KKSavePoint] Settings updated in memory. Before='{before}' After='{_current}'");
            SaveInternal(_current);
        }
    }

    public static FeatureSettings ReloadFromDisk()
    {
        lock (LockObject)
        {
            _current = LoadInternal();
            Log.Info($"[KKSavePoint] Settings reloaded from disk: {_current}");
            return _current;
        }
    }

    private static FeatureSettings LoadInternal()
    {
        try
        {
            var fullPath = ResolveAbsolutePath();
            if (!File.Exists(fullPath))
            {
                Log.Info($"[KKSavePoint] Settings file not found at '{fullPath}'. Using defaults.");
                return FeatureSettings.EnabledByDefault();
            }

            var json = File.ReadAllText(fullPath);
            var loaded = JsonSerializer.Deserialize<FeatureSettings>(json, JsonOptions);
            Log.Info($"[KKSavePoint] Settings file read from '{fullPath}'.");
            return loaded ?? FeatureSettings.EnabledByDefault();
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to read settings. Using defaults. {ex}");
            return FeatureSettings.EnabledByDefault();
        }
    }

    private static void SaveInternal(FeatureSettings settings)
    {
        try
        {
            var fullPath = ResolveAbsolutePath();
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(fullPath, json);
            Log.Info($"[KKSavePoint] Settings saved: {settings}");
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to save settings. {ex}");
        }
    }

    private static string ResolveAbsolutePath()
    {
        return ProjectSettings.GlobalizePath(SettingsPath);
    }
}
