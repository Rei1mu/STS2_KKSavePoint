using System;
using System.Reflection;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace KKSavePoint.src.Features;

/// <summary>
/// Handles automatic dismissal of NErrorPopup when NetError.Quit occurs
/// </summary>
public static class NErrorPopupHandler
{
    private static NetError? _lastErrorPopupNetError = null;

    /// <summary>
    /// Apply Harmony patches for NErrorPopup
    /// </summary>
    public static void ApplyPatches(Harmony harmony)
    {
        harmony.Patch(
            original: typeof(NErrorPopup).GetMethod("Create", new[] { typeof(NetErrorInfo) }),
            prefix: new HarmonyMethod(typeof(NErrorPopupHandler).GetMethod(nameof(CreatePrefix), BindingFlags.Static | BindingFlags.NonPublic))
        );

        harmony.Patch(
            original: typeof(NErrorPopup).GetMethod("_Ready", BindingFlags.Instance | BindingFlags.Public),
            postfix: new HarmonyMethod(typeof(NErrorPopupHandler).GetMethod(nameof(ReadyPostfix), BindingFlags.Static | BindingFlags.NonPublic))
        );
    }

    private static void CreatePrefix(NetErrorInfo info)
    {
        _lastErrorPopupNetError = info.GetReason();
        Log.Info($"[KKSavePoint] NErrorPopup.Create called with NetError: {_lastErrorPopupNetError}");
    }

    private static async void ReadyPostfix(NErrorPopup __instance)
    {
        Log.Info("[KKSavePoint] NErrorPopup _Ready called!");

        // Check if this is a NetError.Quit popup
        if (_lastErrorPopupNetError == NetError.Quit)
        {
            Log.Info("[KKSavePoint] Detected NetError.Quit popup, waiting for frame then auto-clicking OK...");

            // Wait one frame to ensure popup is fully initialized
            await __instance.ToSignal(__instance.GetTree(), "process_frame");

            if (GodotObject.IsInstanceValid(__instance))
            {
                // Get the _verticalPopup field
                var verticalPopupField = __instance.GetType().GetField("_verticalPopup", BindingFlags.Instance | BindingFlags.NonPublic);
                if (verticalPopupField != null)
                {
                    var verticalPopup = (NVerticalPopup)verticalPopupField.GetValue(__instance);
                    if (verticalPopup != null && GodotObject.IsInstanceValid(verticalPopup))
                    {
                        // Click the YesButton
                        verticalPopup.YesButton.ForceClick();
                        Log.Info("[KKSavePoint] Successfully auto-clicked OK button on NetError.Quit popup!");
                    }
                }
            }

            // Clear the record
            _lastErrorPopupNetError = null;
        }
    }
    [HarmonyPatch(typeof(NErrorPopup), "_Ready")]
    public class NErrorPopupPatch
    {
        private static async void Postfix(NErrorPopup __instance)
        {
            Log.Info("[KKSavePoint] NErrorPopup _Ready called!");
            // 等待一帧确保弹窗完全初始化
            await __instance.ToSignal(__instance.GetTree(), "process_frame");

            if (GodotObject.IsInstanceValid(__instance))
            {
                Log.Info($"[KKSavePoint] NErrorPopup __instance");
                // 获取 _body 字段的值
                var bodyField = __instance.GetType().GetField("_body", BindingFlags.Instance | BindingFlags.NonPublic);
                if (bodyField != null)
                {
                    string body = (string)bodyField.GetValue(__instance);
                    Log.Info($"[KKSavePoint] NErrorPopup bodyField: {bodyField},{body},{bodyField}");
                    // 检查是否是 NETWORK_ERROR.QUIT.body 错误
                    if (body != null)
                    {//&& body.Contains("Host left the game")
                        Log.Info("[KKSavePoint] Detected NETWORK_ERROR.QUIT.body popup, auto-clicking OK button...");

                        // 获取 _verticalPopup 字段
                        var verticalPopupField = __instance.GetType().GetField("_verticalPopup", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (verticalPopupField != null)
                        {
                            var verticalPopup = (NVerticalPopup)verticalPopupField.GetValue(__instance);
                            if (verticalPopup != null && GodotObject.IsInstanceValid(verticalPopup))
                            {
                                // 获取 YesButton 并点击
                                verticalPopup.YesButton.ForceClick();
                                Log.Info("[KKSavePoint] Successfully auto-clicked OK button on NETWORK_ERROR.QUIT popup!");
                            }
                        }
                    }
                }
            }
        }
    }
}

//现在的问题是
//我需要接收消息之前不能neterror
//否则会导致断开连接

