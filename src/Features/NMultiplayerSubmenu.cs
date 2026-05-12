
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

                    // 使用延迟来确保子菜单完全初始化
                    Log.Info("[KKSavePoint] Scheduling delayed StartLoad call (waiting for menu transition)...");
                    _ = Task.Delay(1000).ContinueWith(_ =>
                    {
                        try
                        {
                            // 查找 _loadButton 字段
                            var loadButtonField = __instance.GetType().GetField("_loadButton", BindingFlags.Instance | BindingFlags.NonPublic);
                            if (loadButtonField == null)
                            {
                                Log.Warn("[KKSavePoint]_k12f _loadButton field not found!");
                                return;
                            }
                            
                            var loadButton = loadButtonField.GetValue(__instance);
                            if (loadButton == null)
                            {
                                Log.Warn("[KKSavePoint]_k12f _loadButton is null!");
                                return;
                            }
                            
                            // 查找 ForceClick 方法
                            var forceClickMethod = loadButton.GetType().GetMethod("ForceClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (forceClickMethod != null)
                            {
                                Log.Info("[KKSavePoint]_k12 Found ForceClick method on _loadButton, invoking...");
                                forceClickMethod.Invoke(loadButton, null);
                                Log.Info("[KKSavePoint]_k12 Successfully called ForceClick on _loadButton!");
                            }
                            else
                            {
                                Log.Warn("[KKSavePoint]_k12f ForceClick method not found on _loadButton!");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[KKSavePoint]_k12f Error in delayed ForceClick call: {ex}");
                        }
                    });



                    // // 添加延迟以等待菜单转换完成，避免 NLoadingOverlay disposed 错误
                    // Log.Info("[KKSavePoint] Scheduling delayed click on load button (waiting for menu transition)...");

                    // // 使用 Godot 定时器替代 Task.Run，避免跨线程问题
                    // var timer = new Godot.Timer();
                    // timer.WaitTime = 0.5f;
                    // timer.OneShot = true;
                    // timer.Timeout += () =>
                    // {
                    //     GD.Print("[KKSavePoint] Delay complete, now clicking load button...");
                    //     ClickLoadButton(__instance);
                    //     _shouldHost = false;  // 清除 host 标志
                    //     timer.QueueFree();
                    // };
                    // NGame.Instance.AddChild(timer);
                    // timer.Start();
                }
                else if (_shouldJoin)
                {
                    Log.Info("[KKSavePoint] Auto navigating to Join..."); return;
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

                    // 这里不能_shouldJoin = false; 清除 join 标志
                }
            }
            catch (Exception ex)
            {
                _shouldHost = false;  // 出错时也要清除标志
                _shouldJoin = false;
                Log.Info($"[KKSavePoint] havent loaded NMultiplayerSubmenu assets ");
            }
        }
    }
}