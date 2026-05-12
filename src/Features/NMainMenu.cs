
using System.Reflection;
using Godot;
using HarmonyLib;
using KKSavePoint.Core;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Saves;

namespace KKSavePoint.Features;

public partial class SavePointFeature
{
    [HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
    public static class NMainMenuReadyPatch
    {
        public static void Postfix(NMainMenu __instance)
        {
            Log.Info($"[KKSavePoint]_k7 NMainMenu._Ready called, checking auto-navigation...Flags: _shouldHost={_shouldHost}, _shouldJoin={_shouldJoin}");
            if (_shouldHost)
            {
                // 调用 OpenMultiplayerSubmenu() 方法
                var openSubmenuMethod = __instance.GetType().GetMethod("OpenMultiplayerSubmenu", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (openSubmenuMethod == null)
                {
                    Log.Warn("[KKSavePoint]_k7_f OpenMultiplayerSubmenu method not found on NMainMenu!");
                    return;
                }

                var multiplayerSubmenu = openSubmenuMethod.Invoke(__instance, new object[0]);
                if (multiplayerSubmenu == null)
                {
                    Log.Warn("[KKSavePoint]_k7_f NMultiplayerSubmenu is null after calling OpenMultiplayerSubmenu!");
                    return;
                }
                AutoEnterHostFromSaveOnGameStart();
            }
            else if (_shouldJoin)
            {
                Log.Info("[KKSavePoint]_j2 _shouldJoin is true");
                AutoEnterJoinHostOnGameStart(__instance); return;

            }
            else
            {
                Log.Info("[KKSavePoint] No auto-navigation flags set, skipping");
            }
        }
    }
}