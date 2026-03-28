using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;

namespace KKSavePoint.Core;

public static class ModBootstrap
{
    private const string HarmonyId = "kksavepoint.harmony";

    private static bool _initialized;
    private static Harmony? _harmony;

    public static void Initialize()
    {
        if (_initialized)
        {
            Log.Info("[KKSavePoint] ModBootstrap.Initialize skipped (already initialized).");
            return;
        }

        _initialized = true;
        Log.Info("[KKSavePoint] Mod bootstrap starting.");

        FeatureSettingsStore.Initialize();
        Log.Info($"[KKSavePoint] Loaded settings: {FeatureSettingsStore.Current}");

        _harmony = new Harmony(HarmonyId);
        _harmony.PatchAll();
        Log.Info($"[KKSavePoint] Harmony patches applied with id '{HarmonyId}'.");
    }
}
