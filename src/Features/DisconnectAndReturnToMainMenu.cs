using System;
using System.Reflection;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer;
using KKSavePoint.Core;
using MegaCrit.Sts2.Core.Nodes;
using KKSavePoint.Features;
using System.Threading.Tasks;

namespace KKSavePoint.src.Features;
public class SavePointManager
{
    public static async Task DisconnectAndReturnToMainMenu()
    {
        // try
        // {
        //     Log.Info("[KKSavePoint] Disconnecting Steam connection before returning to menu...");

        //     var netService = SavePointFeature.GetCachedNetService();
        //     if (netService != null)
        //     {
        //         var disconnectMethods = netService.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
        //             .Where(m => m.Name == "Disconnect")
        //             .ToArray();

        //         if (disconnectMethods.Length > 0)
        //         {
        //             Log.Info($"[KKSavePoint] Found {disconnectMethods.Length} Disconnect method(s):");
        //             foreach (var method in disconnectMethods)
        //             {
        //                 var paramsDesc = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name));
        //                 Log.Info($"[KKSavePoint]   - {method.Name}({paramsDesc}) : {method.ReturnType.Name}");
        //             }

        //             var noParamMethod = disconnectMethods.FirstOrDefault(m => m.GetParameters().Length == 0);
        //             if (noParamMethod != null)
        //             {
        //                 Log.Info("[KKSavePoint] Calling Disconnect() with no parameters...");
        //                 noParamMethod.Invoke(netService, null);
        //                 Log.Info("[KKSavePoint] Successfully disconnected Steam connection");
        //             }
        //             else
        //             {
        //                 foreach (var method in disconnectMethods)
        //                 {
        //                     try
        //                     {
        //                         var parameters = method.GetParameters();
        //                         object[] args = new object[parameters.Length];
        //                         for (int i = 0; i < parameters.Length; i++)
        //                         {
        //                             Log.Info($"[KKSavePoint] Param {i}: Type={parameters[i].ParameterType.FullName}");
        //                             if (parameters[i].ParameterType.FullName == "MegaCrit.Sts2.Core.Entities.Multiplayer.NetError")
        //                             {
        //                                 args[i] = NetError.Quit;
        //                                 Log.Info($"[KKSavePoint]   - Set to Quit");
        //                             }
        //                             else if (parameters[i].ParameterType.IsEnum)
        //                             {
        //                                 args[i] = Activator.CreateInstance(parameters[i].ParameterType);
        //                                 Log.Info($"[KKSavePoint]   - Enum default: {args[i]}");
        //                             }
        //                             else if (parameters[i].ParameterType == typeof(bool))
        //                             {
        //                                 args[i] = true;
        //                                 Log.Info($"[KKSavePoint]   - Set to true");
        //                             }
        //                             else
        //                             {
        //                                 args[i] = null;
        //                                 Log.Info($"[KKSavePoint]   - Set to null");
        //                             }
        //                         }
        //                         var argsDesc = string.Join(", ", args.Select(a => a?.ToString() ?? "null"));
        //                         Log.Info($"[KKSavePoint] Calling Disconnect({argsDesc})...");
        //                         method.Invoke(netService, args);
        //                         Log.Info("[KKSavePoint] Successfully disconnected Steam connection");
        //                         break;
        //                     }
        //                     catch (Exception ex)
        //                     {
        //                         Log.Warn($"[KKSavePoint] Failed to call Disconnect overload: {ex.Message}");
        //                     }
        //                 }
        //             }
        //         }
        //         else
        //         {
        //             Log.Warn("[KKSavePoint] Disconnect method not found, skipping disconnect...");
        //         }
        //     }
        //     else
        //     {
        //         Log.Warn("[KKSavePoint] NetService not available, skipping disconnect...");
        //     }
        // }
        // catch (Exception ex)
        // {
        //     Log.Warn($"[KKSavePoint] Failed to disconnect: {ex.Message}");
        // }

        await Task.Delay(200);

        await CloseToMenu();
    }

    public static async Task CloseToMenu()
    {
        try
        {
            Log.Info("[KKSavePoint] CloseToMenu: Preparing to return to main menu...");

            Log.Info("[KKSavePoint] CloseToMenu: Calling NGame.Instance.ReturnToMainMenu()...");
            await NGame.Instance.ReturnToMainMenu();
            Log.Info("[KKSavePoint] CloseToMenu: Successfully called ReturnToMainMenu");
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Error in CloseToMenu: {ex}");
        }
    }

    public static void ReturnToMainMenu()
    {
        try
        {
            Log.Info("[KKSavePoint] Returning to main menu...");

            if (NGame.Instance == null)
            {
                Log.Error("[KKSavePoint] NGame.Instance is null");
                return;
            }

            var returnToMenuMethod = NGame.Instance.GetType().GetMethod("ReturnToMainMenu");
            if (returnToMenuMethod != null)
            {
                Log.Info("[KKSavePoint] Found ReturnToMainMenu method");

                if (returnToMenuMethod.IsPublic && returnToMenuMethod.ReturnType == typeof(Task))
                {
                    _ = (Task)returnToMenuMethod.Invoke(NGame.Instance, null);
                    Log.Info("[KKSavePoint] Successfully called ReturnToMainMenu");
                }
                else
                {
                    Log.Warn($"[KKSavePoint] ReturnToMainMenu method has wrong signature: IsPublic={returnToMenuMethod.IsPublic}, ReturnType={returnToMenuMethod.ReturnType}");
                }
            }
            else
            {
                Log.Error("[KKSavePoint] ReturnToMainMenu method not found in NGame");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] Failed to return to main menu: {ex}");
        }
    }
}
