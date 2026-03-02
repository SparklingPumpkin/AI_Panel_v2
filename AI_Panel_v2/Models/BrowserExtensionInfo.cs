namespace AI_Panel_v2.Models;

public class BrowserExtensionInfo
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool IsEnabled { get; init; }

    public string? OptionsUrl { get; init; }

    public string? PopupUrl { get; init; }
}
