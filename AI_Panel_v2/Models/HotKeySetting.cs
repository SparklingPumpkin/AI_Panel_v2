using Windows.System;

namespace AI_Panel_v2.Models;

public class HotKeySetting
{
    public VirtualKey Key { get; set; } = VirtualKey.Space;

    public bool Ctrl { get; set; } = true;

    public bool Alt { get; set; }

    public bool Shift { get; set; } = true;

    public bool Win { get; set; }
}
