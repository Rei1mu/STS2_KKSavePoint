namespace KKSavePoint.Core;

public sealed class FeatureSettings
{
    public bool EnableSavePoint { get; set; }

    public static FeatureSettings EnabledByDefault()
    {
        return new FeatureSettings
        {
            EnableSavePoint = true
        };
    }

    public override string ToString()
    {
        return $"SavePoint={EnableSavePoint}";
    }
}
