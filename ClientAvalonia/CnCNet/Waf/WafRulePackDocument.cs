using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ClientAvalonia.CnCNet.Waf;

/// <summary>JSON document for <c>WafRules*.json</c> (schema version 2).</summary>
public sealed class WafRulePackDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 2;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("policyAlignment")]
    public List<string> PolicyAlignment { get; set; } = [];

    [JsonPropertyName("hostBotTunnels")]
    public List<string> HostBotTunnels { get; set; } = [];

    [JsonPropertyName("sensitivity")]
    public Dictionary<string, WafSensitivityThresholdDto> Sensitivity { get; set; } = new();

    [JsonPropertyName("protocol")]
    public List<WafProtocolRuleDto> Protocol { get; set; } = [];

    [JsonPropertyName("pm")]
    public WafPmRulesDto? Pm { get; set; }

    [JsonPropertyName("contentClasses")]
    public List<WafContentClassDto> ContentClasses { get; set; } = [];
}

public sealed class WafSensitivityThresholdDto
{
    [JsonPropertyName("warn")]
    public int Warn { get; set; } = 25;

    [JsonPropertyName("hide")]
    public int Hide { get; set; } = 80;

    [JsonPropertyName("drop")]
    public int Drop { get; set; } = 9999;
}

public sealed class WafProtocolRuleDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("threshold")]
    public int? Threshold { get; set; }

    [JsonPropertyName("minCount")]
    public int? MinCount { get; set; }

    [JsonPropertyName("windowSeconds")]
    public int? WindowSeconds { get; set; }

    [JsonPropertyName("perBurst")]
    public int? PerBurst { get; set; }

    [JsonPropertyName("perExtra")]
    public int? PerExtra { get; set; }

    [JsonPropertyName("cap")]
    public int? Cap { get; set; }
}

public sealed class WafPmRulesDto
{
    [JsonPropertyName("burst")]
    public WafPmBurstDto? Burst { get; set; }

    [JsonPropertyName("firstContactPromo")]
    public WafPmFirstContactDto? FirstContactPromo { get; set; }
}

public sealed class WafPmBurstDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "content.pm.burst";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "私信短时刷屏";

    [JsonPropertyName("minCount")]
    public int MinCount { get; set; } = 4;

    [JsonPropertyName("windowSeconds")]
    public int WindowSeconds { get; set; } = 30;

    [JsonPropertyName("baseScore")]
    public int BaseScore { get; set; } = 20;

    [JsonPropertyName("perMessage")]
    public int PerMessage { get; set; } = 10;

    [JsonPropertyName("cap")]
    public int Cap { get; set; } = 70;
}

public sealed class WafPmFirstContactDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "content.pm.first_contact_promo";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "冷私信疑似推广";

    [JsonPropertyName("score")]
    public int Score { get; set; } = 20;

    [JsonPropertyName("minScore")]
    public int MinScore { get; set; } = 25;

    [JsonPropertyName("triggerClasses")]
    public List<string> TriggerClasses { get; set; } = [];
}

public sealed class WafContentClassDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("pmReason")]
    public string? PmReason { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("regexes")]
    public List<string> Regexes { get; set; } = [];

    [JsonPropertyName("keywords")]
    public List<string> Keywords { get; set; } = [];
}
