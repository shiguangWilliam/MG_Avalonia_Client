namespace ClientAvalonia.CnCNet.Waf;

/// <summary>Live WAF toggles. Production reads <see cref="ClientCore.UserINISettings"/>; tests inject a snapshot.</summary>
public sealed class WafSettings
{
    public bool Enabled { get; init; } = true;

    public bool CheckProtocol { get; init; } = true;

    public bool CheckListingText { get; init; } = true;

    public bool CheckChannelChat { get; init; } = true;

    public bool CheckPrivateChat { get; init; } = true;

    /// <summary>0=Low, 1=Medium, 2=High.</summary>
    public int Sensitivity { get; init; } = 1;

    public bool AutoHideHighRisk { get; init; }

    public bool AllowHeuristicDrop { get; init; }

    public static WafSettings FromUserIni()
    {
        try
        {
            var ini = ClientCore.UserINISettings.Instance;
            return new WafSettings
            {
                Enabled = ini.WafEnabled.Value,
                CheckProtocol = ini.WafCheckProtocol.Value,
                CheckListingText = ini.WafCheckListingText.Value,
                CheckChannelChat = ini.WafCheckChannelChat.Value,
                CheckPrivateChat = ini.WafCheckPrivateChat.Value,
                Sensitivity = ini.WafSensitivity.Value,
                AutoHideHighRisk = ini.WafAutoHideHighRisk.Value,
                AllowHeuristicDrop = ini.WafAllowHeuristicDrop.Value,
            };
        }
        catch (InvalidOperationException)
        {
            return new WafSettings();
        }
    }
}
