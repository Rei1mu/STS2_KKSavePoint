using System.Collections.Generic;
using KKSavePoint.Core;

namespace KKSavePoint.Features.Settings;

internal static class SettingsOptionCatalog
{
    public static IReadOnlyList<SettingsOptionDefinition> All { get; } = new[]
    {
        new SettingsOptionDefinition
        {
            Label = "Enable SavePoint button in top bar",
            LogKey = nameof(FeatureSettings.EnableSavePoint),
            GetValue = settings => settings.EnableSavePoint,
            SetValue = (settings, value) => settings.EnableSavePoint = value
        }
    };
}
