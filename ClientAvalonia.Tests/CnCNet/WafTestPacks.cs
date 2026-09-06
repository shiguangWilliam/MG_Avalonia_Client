using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using ClientAvalonia.CnCNet.Waf;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// Explicit rule packs for engine-capability tests.
///
/// rules.default.json intentionally ships WITHOUT protocol fingerprints and
/// host-bot tunnels (they false-positived stock rooms; the description
/// documents re-enabling via override packs). Tests that verify the protocol
/// engine's capabilities therefore must not rely on Default — they pass an
/// explicit pack here.
/// </summary>
public static class WafTestPacks
{
    /// <summary>
    /// Full hang-farm protocol pack: R8 / field-count / tunnel blacklist /
    /// shared hosts / fake players / template fingerprint / invite flood.
    /// </summary>
    public static WafCompiledRulePack HangFarm() => WafRulePackLoader.CompileFromJson(
        """
        {
          "version": 2,
          "description": "hangfarm-test",
          "hostBotTunnels": [ "175.178.174.40:50000" ],
          "protocol": [
            { "id": "proto.game.r8", "score": 40, "reason": "R8" },
            { "id": "proto.game.field_count", "score": 50, "reason": "fields" },
            { "id": "proto.tunnel.blacklist", "score": 80, "reason": "tunnel" },
            { "id": "proto.tunnel.shared_hosts", "score": 45, "threshold": 3, "reason": "shared" },
            { "id": "proto.game.fake_players", "score": 35, "reason": "fake" },
            { "id": "proto.game.template_fingerprint", "score": 40, "threshold": 2, "reason": "tpl" },
            { "id": "proto.invite.flood", "score": 30, "minCount": 3, "windowSeconds": 30, "perExtra": 10, "cap": 80 }
          ],
          "contentClasses": []
        }
        """,
        "hangfarm");

    /// <summary>
    /// Hang-farm protocol rules layered on top of the embedded default's social
    /// content classes — for scenario tests that need BOTH engines active
    /// (protocol fingerprints + promo/abuse content rules). The content set is
    /// loaded from the same embedded default at runtime, so keyword tuning in
    /// rules.default.json stays authoritative here.
    /// </summary>
    public static WafCompiledRulePack HangFarmWithDefaultContent()
    {
        // Re-parse the embedded default document, then inject protocol rules +
        // the known tunnel on top via a JSON-merge (document-level, so regexes
        // and keywords survive untouched).
        string embeddedJson = ReadEmbeddedDefaultJson();
        using JsonDocument doc = JsonDocument.Parse(embeddedJson, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("hostBotTunnels") || prop.NameEquals("protocol"))
                    continue;
                prop.WriteTo(writer);
            }

            writer.WritePropertyName("hostBotTunnels");
            writer.WriteStartArray();
            writer.WriteStringValue("175.178.174.40:50000");
            writer.WriteEndArray();

            writer.WritePropertyName("protocol");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("id", "proto.game.r8");
            writer.WriteNumber("score", 40);
            writer.WriteString("reason", "R8");
            writer.WriteEndObject();
            writer.WriteStartObject();
            writer.WriteString("id", "proto.game.field_count");
            writer.WriteNumber("score", 50);
            writer.WriteString("reason", "fields");
            writer.WriteEndObject();
            writer.WriteStartObject();
            writer.WriteString("id", "proto.tunnel.blacklist");
            writer.WriteNumber("score", 80);
            writer.WriteString("reason", "tunnel");
            writer.WriteEndObject();
            writer.WriteStartObject();
            writer.WriteString("id", "proto.tunnel.shared_hosts");
            writer.WriteNumber("score", 45);
            writer.WriteNumber("threshold", 3);
            writer.WriteString("reason", "shared");
            writer.WriteEndObject();
            writer.WriteStartObject();
            writer.WriteString("id", "proto.game.fake_players");
            writer.WriteNumber("score", 35);
            writer.WriteString("reason", "fake");
            writer.WriteEndObject();
            writer.WriteStartObject();
            writer.WriteString("id", "proto.game.template_fingerprint");
            writer.WriteNumber("score", 40);
            writer.WriteNumber("threshold", 2);
            writer.WriteString("reason", "tpl");
            writer.WriteEndObject();
            writer.WriteStartObject();
            writer.WriteString("id", "proto.invite.flood");
            writer.WriteNumber("score", 30);
            writer.WriteNumber("minCount", 3);
            writer.WriteNumber("windowSeconds", 30);
            writer.WriteNumber("perExtra", 10);
            writer.WriteNumber("cap", 80);
            writer.WriteEndObject();
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        return WafRulePackLoader.CompileFromJson(reader.ReadToEnd(), "hangfarm-default-content");
    }

    private static string ReadEmbeddedDefaultJson()
    {
        Assembly asm = typeof(WafRulePackLoader).Assembly;
        using Stream? stream = asm.GetManifestResourceStream(WafRulePackLoader.EmbeddedResourceName);
        if (stream == null)
            throw new InvalidOperationException($"Embedded resource not found: {WafRulePackLoader.EmbeddedResourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
