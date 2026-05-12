
using System.Reflection;
using Godot;
using HarmonyLib;
using KKSavePoint.Core;
using KKSavePoint.src.Features;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Saves;

namespace KKSavePoint.Features;

public partial class SavePointFeature
{
    public static void OnClickMultiplayerSavePoint()
    {
        Log.Info("[KKSavePoint]_k2 Host: Saving save data and sending rollback notification to clients...");
        ShowFeedback("正在发送回档通知给客机...");

        // 发送回档消息通知所有客机并等待确认
        RollbackMessageHandler.SendRollbackMessageWithAck(5000);


        Log.Info("[KKSavePoint]_k3 Host: Save data prepared and rollback notification sent, returning to menu...");
        ShowFeedback("正在返回主菜单，准备重新加载房间...");

        ScheduleAutoNavigateToMultiplayer();


        SavePointManager.DisconnectAndReturnToMainMenu();

        return;
    }
}