using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace KKSavePoint.Features.Settings;

public static class SettingsFeature
{
    public static void AttachToMainMenu(NMainMenu mainMenu)
    {
        SettingsButtonInstaller.Attach(mainMenu, SettingsPopupController.Open);
    }
}

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
public static class NMainMenuReadySettingsPatch
{
    public static void Postfix(NMainMenu __instance)
    {
        try
        {
            Log.Info("[KKSavePoint] NMainMenu._Ready postfix running.");
            SettingsFeature.AttachToMainMenu(__instance);
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to add main-menu settings button: {ex}");
        }
    }
}
