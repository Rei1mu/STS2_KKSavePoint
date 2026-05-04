using System.Reflection;
using System.Linq;
using KKSavePoint.Core;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace KKSavePoint;

[ModInitializer(nameof(OnModLoaded))]
public static class ModEntry
{
    public static void OnModLoaded()
    {
        Log.Info("[KKSavePoint] ModEntry.OnModLoaded invoked by STS2.");
        DumpSaveManagerInfo();
        ModBootstrap.Initialize();
    }

    private static void DumpSaveManagerInfo()
    {
        try
        {
            var saveManagerType = Type.GetType("MegaCrit.Sts2.Core.Saves.SaveManager, sts2");
            if (saveManagerType == null)
            {
                Log.Error("[KKSavePoint] Can't find SaveManager type!");
                return;
            }

            Log.Info($"[KKSavePoint] SaveManager found! Type: {saveManagerType.AssemblyQualifiedName}");

            var instanceProp = saveManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var saveManagerInstance = instanceProp?.GetValue(null);
            if (saveManagerInstance == null)
            {
                Log.Error("[KKSavePoint] Can't get SaveManager.Instance!");
                return;
            }

            Log.Info("[KKSavePoint] SaveManager.Instance found!");

            // 打印 _saveStore 的所有成员
            var saveStoreField = saveManagerType.GetField("_saveStore", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (saveStoreField != null)
            {
                var saveStore = saveStoreField.GetValue(saveManagerInstance);
                if (saveStore != null)
                {
                    Log.Info($"[KKSavePoint] _saveStore found! Type: {saveStore.GetType().AssemblyQualifiedName}");
                    
                    // 获取 LocalStore（GodotFileIo）
                    var localStoreProp = saveStore.GetType().GetProperty("LocalStore", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (localStoreProp != null)
                    {
                        var localStore = localStoreProp.GetValue(saveStore);
                        if (localStore != null)
                        {
                            Log.Info($"[KKSavePoint] LocalStore found! Type: {localStore.GetType().AssemblyQualifiedName}");
                            
                            Log.Info("[KKSavePoint] --- LocalStore Fields ---");
                            var lsFields = localStore.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                            foreach (var field in lsFields)
                            {
                                var valueStr = "n/a";
                                try
                                {
                                    var val = field.GetValue(localStore);
                                    valueStr = val?.ToString() ?? "null";
                                }
                                catch { }
                                Log.Info($"[KKSavePoint] LS Field: {field.Name} (Type: {field.FieldType}) = {valueStr}");
                            }

                            Log.Info("[KKSavePoint] --- LocalStore Properties ---");
                            var lsProps = localStore.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                            foreach (var prop in lsProps)
                            {
                                var valueStr = "n/a";
                                try
                                {
                                    if (prop.GetMethod != null)
                                    {
                                        var val = prop.GetValue(localStore);
                                        valueStr = val?.ToString() ?? "null";
                                    }
                                }
                                catch { }
                                Log.Info($"[KKSavePoint] LS Property: {prop.Name} (Type: {prop.PropertyType}) = {valueStr}");
                            }
                            
                            Log.Info("[KKSavePoint] --- LocalStore Methods ---");
                            var lsMethods = localStore.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                            foreach (var method in lsMethods)
                            {
                                Log.Info($"[KKSavePoint] LS Method: {method.Name} (Returns: {method.ReturnType}, Params: {string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[KKSavePoint] DumpSaveManagerInfo failed: {ex}");
        }
    }
}
