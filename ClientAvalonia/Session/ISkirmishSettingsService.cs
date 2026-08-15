using System;
using System.IO;
using System.Linq;
using ClientAvalonia.Services;
using ClientCore;
using Rampastring.Tools;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.Session;

/// <summary>
/// 遭遇战设置持久化服务（INI 文件读写）。
/// </summary>
/// <remarks>
/// 设计理由（见 layered-architecture-progress-report.md §9.5 Slice 3）：
/// <list type="bullet">
/// <item>原本 <c>LobbyPlayerState.TryLoadSkirmishSettings</c> / <c>SaveSkirmishSettings</c>
/// 同时承担"读取文件 + 写状态到 Slots"两件事。</item>
/// <item>提取到 Service 后，状态归 <c>LobbyPlayerState</c>，IO 归 Service。
/// 这样 IO 路径可单测、可 mock（不用整盘 <c>AppState.Environment.GamePath</c>）。</item>
/// <item>Service 接收/返回强类型的 <c>SkirmishSettingsDto</c>，
/// 不依赖 <c>LobbyPlayerSlot</c>，便于未来扩展字段。</item>
/// </list>
/// </remarks>
public interface ISkirmishSettingsService
{
    /// <summary>从 INI 加载设置；文件不存在或解析失败返回 null。</summary>
    SkirmishSettingsDto? TryLoad();

    /// <summary>保存设置到 INI 文件。</summary>
    void Save(SkirmishSettingsDto dto);

    /// <summary>当前生效的文件绝对路径（暴露以便测试）。</summary>
    string CurrentPath { get; }
}

/// <summary>遭遇战设置数据传输对象（与 INI 字段一一对应）。</summary>
public sealed class SkirmishSettingsDto
{
    public SkirmishPlayerDto? Human { get; set; }
    public System.Collections.Generic.List<SkirmishPlayerDto> Ais { get; } = new();

    public bool HasContent => Human != null || Ais.Count > 0;
}

public sealed class SkirmishPlayerDto
{
    public string Name { get; set; } = string.Empty;
    public int SideIndex { get; set; }
    public int StartIndex { get; set; }
    public int ColorIndex { get; set; }
    public int TeamIndex { get; set; }
    public int AiLevel { get; set; }
    public bool IsAi { get; set; }
    public int Index { get; set; }

    public override string ToString()
        => string.Join(',', Name, SideIndex, StartIndex, ColorIndex, TeamIndex, AiLevel, IsAi, Index);
}

/// <summary>默认实现：读写 <c>Client/SkirmishSettings.ini</c>。</summary>
public sealed class SkirmishSettingsService : ISkirmishSettingsService
{
    public const string DefaultRelativePath = "Client/SkirmishSettings.ini";

    /// <summary>绝对路径；若为 null 则基于 <see cref="AppState.Environment.GamePath"/> 计算。</summary>
    private readonly string? _absolutePath;
    private readonly string _relativePath;

    /// <summary>构造：使用默认相对路径（基于 <see cref="AppState.Environment.GamePath"/>）。</summary>
    public SkirmishSettingsService() : this(relativePath: DefaultRelativePath, absolutePath: null) { }

    /// <summary>构造：自定义相对路径。</summary>
    public SkirmishSettingsService(string relativePath) : this(relativePath: relativePath, absolutePath: null) { }

    /// <summary>构造：使用绝对路径（测试用，绕开 <see cref="AppState.Environment.GamePath"/>）。</summary>
    public SkirmishSettingsService(string absolutePath, bool absolute) : this(relativePath: DefaultRelativePath, absolutePath: absolutePath) { }

    private SkirmishSettingsService(string relativePath, string? absolutePath)
    {
        _relativePath = relativePath;
        _absolutePath = absolutePath;
    }

    /// <inheritdoc />
    public string CurrentPath => _absolutePath
        ?? SafePath.CombineFilePath(AppState.Environment.GamePath, _relativePath);

    /// <inheritdoc />
    public SkirmishSettingsDto? TryLoad()
    {
        string path = CurrentPath;
        if (!File.Exists(path))
            return null;

        var ini = new IniFile(path);
        var dto = new SkirmishSettingsDto();

        string humanRaw = ini.GetStringValue("Player", "Info", string.Empty);
        if (TryParseLine(humanRaw, out var human) && human != null)
        {
            human.IsAi = false;
            dto.Human = human;
        }

        System.Collections.Generic.List<string>? keys = ini.GetSectionKeys("AIPlayers");
        if (keys != null)
        {
            foreach (string key in keys.OrderBy(k => int.TryParse(k, out int i) ? i : int.MaxValue))
            {
                string raw = ini.GetStringValue("AIPlayers", key, string.Empty);
                if (TryParseLine(raw, out var ai) && ai != null)
                {
                    ai.IsAi = true;
                    dto.Ais.Add(ai);
                }
            }
        }

        return dto.HasContent ? dto : null;
    }

    /// <inheritdoc />
    public void Save(SkirmishSettingsDto dto)
    {
        string path = CurrentPath;
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var ini = new IniFile(path);
        if (dto.Human is { } h)
            ini.SetStringValue("Player", "Info", h.ToString());

        for (int i = 0; i < dto.Ais.Count; i++)
            ini.SetStringValue("AIPlayers", i.ToString(), dto.Ais[i].ToString());

        ini.WriteIniFile();
    }

    /// <summary>从 INI 行解析（与原 <c>LobbyPlayerState.TryParsePlayerLine</c> 一致）。</summary>
    public static bool TryParseLine(string raw, out SkirmishPlayerDto? slot)
    {
        slot = null;
        // 注意：用 None 而非 RemoveEmptyEmptyEntries —— 空字段（如 Name=","）必须保留位置
        string[] parts = raw.Split(',', StringSplitOptions.None);
        if (parts.Length < 7)
            return false;

        var s = new SkirmishPlayerDto
        {
            Name = parts[0],
            SideIndex = int.TryParse(parts[1], out int side) ? side : 0,
            StartIndex = int.TryParse(parts[2], out int start) ? start : 0,
            ColorIndex = int.TryParse(parts[3], out int color) ? color : 0,
            TeamIndex = int.TryParse(parts[4], out int team) ? team : 0,
            AiLevel = int.TryParse(parts[5], out int ai) ? ai : 0,
            IsAi = bool.TryParse(parts[6], out bool isAi) && isAi,
            Index = parts.Length >= 8 && int.TryParse(parts[7], out int idx) ? idx : 0,
        };
        if (string.IsNullOrWhiteSpace(s.Name))
            return false;

        slot = s;
        return true;
    }
}
