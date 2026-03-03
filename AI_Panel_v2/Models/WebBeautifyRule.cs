namespace AI_Panel_v2.Models;

public class WebBeautifyRule
{
    public string UrlPattern
    {
        get; set;
    } = string.Empty;

    public string FilePath
    {
        get; set;
    } = string.Empty;

    public bool IsEnabled
    {
        get; set;
    } = true;
}
