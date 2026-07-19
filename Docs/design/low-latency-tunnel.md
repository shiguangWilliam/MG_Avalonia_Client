# 新功能设计：联机默认选延迟最低的 Tunnel 服务器

> **状态**：设计稿，待审批。

## 1. 需求

> 对于多人联机模式，默认选择延迟最低的服务器（可能涉及预启动以及周期性延迟确认、ping 等）。

用户进入"创建游戏"或"加入游戏"时，Tunnel 列表默认按 ping 排序，并自动选中 ping 最低的那个，而不是当前的"选第一个 Official"。

## 2. 现状调研

### 2.1 默认 Tunnel 选择

`GameCreationOverlayBuilder.Build`：

```csharp
int defaultIndex = tunnels.ToList().FindIndex(t => t.Official);
if (defaultIndex < 0 && tunnels.Count > 0)
    defaultIndex = 0;

if (defaultIndex >= 0 && defaultIndex < tunnels.Count)
    context.SelectedTunnel = tunnels[defaultIndex];
```

**问题**：只看 `Official` 标志，**完全不看 ping**。中国用户如果第一个 Official 是欧洲服务器，依然默认选中。

### 2.2 Ping 测量

`CnCNetTunnel.UpdatePing()`：

```csharp
public void UpdatePing()
{
    using var ping = new Ping();
    try
    {
        PingReply reply = ping.Send(IPAddress.Parse(Address), PingTimeoutMilliseconds);
        if (reply.Status == IPStatus.Success)
            PingInMs = Convert.ToInt32(reply.RoundtripTime);
    }
    catch (PingException ex) { /* logged */ }
}
```

**问题**：
- 单次同步 `Ping.Send`，超时 1s。如果有 50 个 tunnel，串行就是 50 秒。
- `PingInMs = -1` 表示"未测量或测量失败"。当前 UI 不区分。

### 2.3 周期性 Tunnel 维护

`CnCNetSession.RunTunnelMaintenance`（每 `CurrentTunnelPingIntervalSeconds` 秒触发一次）：

```csharp
private void RunTunnelMaintenance()
{
    if (_tunnelMaintenanceCycle % CyclesPerTunnelListRefresh == 0)
    {
        _tunnelMaintenanceCycle = 0;
        RefreshTunnelsAsync();    // 每 N 个 cycle 重拉一次 tunnel 列表
    }
    else
    {
        // 中间 cycle：仅给当前 active game room 的 tunnel 重测 ping
        // 不是批量重测所有 tunnel
    }
}
```

**问题**：周期性维护**只 ping 当前房间的 tunnel**，不批量 ping tunnel 列表。所以用户进入"创建游戏"页面时，**大部分 tunnel 的 `PingInMs` 都是 -1**。

### 2.4 用户场景时间线

```
t0: 用户启动 launcher
t1: CnCNet 连接成功
t2: RunTunnelMaintenance 第一次触发（RefreshTunnelsAsync 拉取 tunnel 列表）
    → 此时所有 tunnel 的 PingInMs = -1
t3: 用户点击"CnCNet" → 进入 lobby
t4: 用户点击"创建游戏" → 弹出 GameCreationOverlay
    → 此时 tunnel 列表渲染，PingInMs 大多为 -1
    → 默认选中"第一个 Official"（无论它在哪）

希望的行为：
t4: 默认选中 ping 最低的 tunnel（如果还没测完，则选中第一个 Official 作为占位）
```

## 3. 设计方案

### 3.1 三层处理（推荐）

#### Layer 1：预启动 ping（启动时后台批量测量）

启动后立刻对所有 tunnel 做并发 ping，结果缓存到 `CnCNetTunnel.PingInMs`。

```csharp
// 新方法：CnCNetSession.PrewarmTunnelPingsAsync
public async Task PrewarmTunnelPingsAsync(CancellationToken ct = default)
{
    List<CnCNetTunnel> snapshot;
    lock (_sync) snapshot = _tunnels.ToList();
    if (snapshot.Count == 0) return;

    // Concurrent ping with limited parallelism (avoid network flood)
    var options = new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct };
    await Parallel.ForEachAsync(snapshot, options, async (tunnel, token) =>
    {
        await Task.Run(() => tunnel.UpdatePing(), token);
    });

    StateChanged?.Invoke();   // notify UI
}
```

**触发时机**：
- IRC 连接成功后（`OnIrcConnected`）
- Tunnel 列表第一次刷新完成后（`RefreshTunnelsAsync` 末尾）

#### Layer 2：周期性重测（确认延迟稳定性）

复用现有 `RunTunnelMaintenance`，但**增加批量重测分支**：

```csharp
private void RunTunnelMaintenance()
{
    if (_tunnelMaintenanceCycle % CyclesPerTunnelListRefresh == 0)
    {
        _tunnelMaintenanceCycle = 0;
        RefreshTunnelsAsync();   // 拉新列表
        _ = PrewarmTunnelPingsAsync();   // 重新 ping 全部 ← 新增
    }
    else if (_tunnelMaintenanceCycle % CyclesPerTunnelPingRefresh == 0)
    {
        // 中间 cycle：仅重测 ping 最高的 top-K（用于动态调整推荐）
        _ = RefreshTopKTunnelPingsAsync(k: 10);
    }
    else
    {
        // 现有：仅 active game room tunnel
    }
    _tunnelMaintenanceCycle++;
}
```

`CyclesPerTunnelPingRefresh = 4`，假设 base interval 30s，则每 2 分钟批量重测 top-10。

#### Layer 3：进入"创建游戏"时按 ping 排序 + 自动选最低

`GameCreationOverlayBuilder.Build`：

```csharp
// 旧
int defaultIndex = tunnels.ToList().FindIndex(t => t.Official);
if (defaultIndex < 0 && tunnels.Count > 0)
    defaultIndex = 0;

// 新
IReadOnlyList<CnCNetTunnel> sorted = TunnelSorter.SortByRecommended(tunnels);
int defaultIndex = 0;
context.SelectedTunnel = sorted.Count > 0 ? sorted[0] : null;
// 后续渲染用 sorted
```

新类 `TunnelSorter`：

```csharp
// 新文件：ClientAvalonia/CnCNet/TunnelSorter.cs
public static class TunnelSorter
{
    /// <summary>
    /// Sort tunnels by: (1) measured ping ascending (excluding -1),
    ///                   (2) Official/Recommended first when ping is equal or unmeasured,
    ///                   (3) unmeasured (-1) at the end.
    /// </summary>
    public static IReadOnlyList<CnCNetTunnel> SortByRecommended(IReadOnlyList<CnCNetTunnel> tunnels)
    {
        return tunnels
            .OrderBy(t => t.PingInMs < 0 ? int.MaxValue : t.PingInMs)
            .ThenByDescending(t => t.Official ? 2 : t.Recommended ? 1 : 0)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Best-effort pick of the lowest-latency tunnel; null if list is empty.</summary>
    public static CnCNetTunnel? PickBest(IReadOnlyList<CnCNetTunnel> tunnels)
    {
        IReadOnlyList<CnCNetTunnel> sorted = SortByRecommended(tunnels);
        return sorted.Count > 0 ? sorted[0] : null;
    }
}
```

### 3.2 异步刷新 UI

`GameCreationOverlayBuilder` 渲染 tunnel 行时，如果 `PingInMs = -1` 显示 "..."，否则显示 "{ping} ms"。后台 ping 完成后通过 `CnCNetSessionService.Instance.StateChanged` 事件触发重渲染：

```csharp
// MainWindow 监听
CnCNetSessionService.Instance.StateChanged += OnCnCNetStateChanged;

private void OnCnCNetStateChanged()
{
    if (IsFloatingOverlayOpen && _floatingOverlayWindow == GameCreationOverlayHost.WindowName)
    {
        // Refresh tunnel rows in-place (don't rebuild entire overlay)
        _gameCreationOverlay?.RefreshTunnelPings();
    }
}
```

`GameCreationOverlayContext.RefreshTunnelPings()`：

```csharp
public void RefreshTunnelPings()
{
    foreach (Border row in TunnelRows)
    {
        if (row.DataContext is CnCNetTunnel tunnel)
        {
            var pingText = (TextBlock)row.FindControl("PingText")!;
            pingText.Text = tunnel.PingInMs < 0 ? "..." : $"{tunnel.PingInMs} ms";
        }
    }
    // 如果当前选中的 tunnel 不再是 ping 最低的，可选提示用户重新选择（不强制改）
}
```

### 3.3 边界情况

| 情况 | 处理 |
|---|---|
| 用户网络无法 ping（ICMP 被禁） | `PingInMs` 一直 -1，按 Official 兜底（与现状一致） |
| 用户在 ping 完成前就点了 Confirm | 走当前 SelectedTunnel（仍是官方或第一个） |
| Tunnel 列表为空 | 显示"No tunnel servers available."（现状） |
| Tunnel 全部不可达 | 排序兜底为按 Name 字典序 |
| ping 测量噪声（首次抖动） | 用 Layer 2 周期性重测平滑 |

## 4. 类设计清单

### 4.1 新增类

| 类 | 路径 | 职责 |
|---|---|---|
| `TunnelSorter` | `ClientAvalonia/CnCNet/TunnelSorter.cs` | 排序 + 选最佳 |
| `TunnelPingPrewarmService` | `ClientAvalonia/CnCNet/TunnelPingPrewarmService.cs` | 批量并发 ping |

### 4.2 改造现有类

| 类 | 改造点 |
|---|---|
| `CnCNetSession` | 增加 `PrewarmTunnelPingsAsync`、`RefreshTopKTunnelPingsAsync` |
| `GameCreationOverlayBuilder.Build` | 用 `TunnelSorter.SortByRecommended` 替代 `FindIndex(t => t.Official)` |
| `GameCreationOverlayContext` | 增加 `RefreshTunnelPings` 方法 |
| `MainWindow.OnCnCNetStateChanged` | tunnel overlay 打开时调 `RefreshTunnelPings` |

## 5. 测试策略

### 5.1 `TunnelSorter` 单元测试（无网络）

```csharp
public sealed class TunnelSorterTests
{
    [Fact]
    public void Sort_MeasuredPingAscending_First()
    {
        var tunnels = new List<CnCNetTunnel>
        {
            new() { Name = "EU", PingInMs = 200, Official = true },
            new() { Name = "CN", PingInMs = 30,  Official = false },
            new() { Name = "US", PingInMs = 150, Official = true },
        };

        IReadOnlyList<CnCNetTunnel> sorted = TunnelSorter.SortByRecommended(tunnels);

        sorted[0].Name.Should().Be("CN");
        sorted[1].Name.Should().Be("US");
        sorted[2].Name.Should().Be("EU");
    }

    [Fact]
    public void Sort_Unmeasured_Goes_Last()
    {
        var tunnels = new List<CnCNetTunnel>
        {
            new() { Name = "A", PingInMs = -1 },
            new() { Name = "B", PingInMs = 200 },
        };

        IReadOnlyList<CnCNetTunnel> sorted = TunnelSorter.SortByRecommended(tunnels);

        sorted[0].Name.Should().Be("B");
        sorted[1].Name.Should().Be("A");
    }

    [Fact]
    public void Sort_AllUnmeasured_OfficialFirst()
    {
        var tunnels = new List<CnCNetTunnel>
        {
            new() { Name = "X", PingInMs = -1, Official = false },
            new() { Name = "Y", PingInMs = -1, Official = true },
        };

        IReadOnlyList<CnCNetTunnel> sorted = TunnelSorter.SortByRecommended(tunnels);

        sorted[0].Name.Should().Be("Y");
    }

    [Fact]
    public void PickBest_Empty_Returns_Null()
    {
        TunnelSorter.PickBest(new List<CnCNetTunnel>()).Should().BeNull();
    }
}
```

### 5.2 Prewarm 集成测试（mock ICMP）

`PrewarmTunnelPingsAsync` 内部依赖 `System.Net.NetworkInformation.Ping`，需要抽象才能测试：

```csharp
public interface ITunnelPinger
{
    int Measure(IPAddress address);
}

internal sealed class SystemPingTunnelPinger : ITunnelPinger
{
    public int Measure(IPAddress address)
    {
        using var ping = new Ping();
        PingReply reply = ping.Send(address, 1000);
        return reply.Status == IPStatus.Success ? Convert.ToInt32(reply.RoundtripTime) : -1;
    }
}

// 测试用 mock
internal sealed class MockTunnelPinger : ITunnelPinger
{
    public Func<IPAddress, int> Stub { get; set; } = _ => -1;
    public int Measure(IPAddress address) => Stub(address);
}
```

`CnCNetTunnel.UpdatePing()` 改造为依赖 `ITunnelPinger`（构造注入）：

```csharp
public void UpdatePing(ITunnelPinger? pinger = null)
{
    pinger ??= new SystemPingTunnelPinger();
    try { PingInMs = pinger.Measure(IPAddress.Parse(Address)); }
    catch (Exception ex) { Logger.Log($"...{ex.Message}"); }
}
```

## 6. 工作量预估

| 步骤 | 工时 |
|---|---|
| `TunnelSorter` 类 + 单测 | 1h |
| `ITunnelPinger` 抽象 + 改造 `UpdatePing` | 2h |
| `PrewarmTunnelPingsAsync` + `RefreshTopK` | 2h |
| `RunTunnelMaintenance` 接入 | 1h |
| `GameCreationOverlayBuilder` 接入 `SortByRecommended` | 1h |
| UI 异步刷新（`RefreshTunnelPings`） | 2h |
| 集成测试 | 2h |
| 手动验证（真实 IRC） | 1h |
| **总计** | **~12h**（1.5 个工作日） |

## 7. 风险

| 风险 | 缓解 |
|---|---|
| 批量 ping 占用网络（用户正在下载更新） | `MaxDegreeOfParallelism = 8` 限流；启动 30s 后才开始 |
| 中国 ICMP 被运营商劫持/丢弃 | ping 失败 → PingInMs=-1 → 按 Official 兜底，与现状一致 |
| Tunnel 列表很大（>100） | 分批 ping（每批 10 个），UI 渐进显示 |
| 用户手动切换 tunnel 后又被自动切回 | 仅在 `SelectedTunnel == null` 时自动选；用户选过后保留 |
| ping 单次抖动 | Layer 2 周期重测，多次测量平滑（中位数/最小值） |

## 8. 与其他设计的关系

- 依赖 [auto-refresh-design.md](auto-refresh-design.md) 中描述的统一 refresh（ping 完成后需要刷新 UI）。但 tunnel overlay 不在 lobby 范围内，可以独立实现。
- 与 [global-state-refactor.md](global-state-refactor.md) 中 `ICnCNetSession` 接口的 `Tunnels` 属性对接。

## 9. 后续扩展（不在本次范围）

1. **Tunnel 健康度评分**：综合 ping + 历史成功率 + 当前 client 数。
2. **地理就近**：根据用户 IP 推断地理位置，优先推荐同区域 tunnel。
3. **用户偏好持久化**：记录用户上次选的 tunnel，下次默认选中。

---

**请确认**：
1. 是否同意三层架构（prewarm + 周期性 + UI 排序）？
2. `MaxDegreeOfParallelism = 8` 是否合理（考虑用户带宽）？
3. 是否需要用户偏好持久化（"记住我上次选的 tunnel"）？
4. `ITunnelPinger` 抽象是否同意引入（便于单测）？
