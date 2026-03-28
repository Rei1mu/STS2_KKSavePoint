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
        ModBootstrap.Initialize();
    }
}
