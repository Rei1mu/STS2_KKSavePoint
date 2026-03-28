using System;
using KKSavePoint.Core;

namespace KKSavePoint.Features.Settings;

internal sealed class SettingsOptionDefinition
{
    public required string Label { get; init; }

    public required string LogKey { get; init; }

    public required Func<FeatureSettings, bool> GetValue { get; init; }

    public required Action<FeatureSettings, bool> SetValue { get; init; }
}
