# 低延迟 Tunnel 选择 设计文档

> **状态**：v2 — 改用 `PriorityQueue<TElement, TPriority>` 小顶堆（用户 2026-07-19 反馈）。
> 待审批。

## 1. 问题陈述

CnCNet 客户端默认勾选的 tunnel 服务器由 `CnCNetTunnelsUpdateCompleted` 回调里的官方循环选第一个，与用户实际延迟无关。国内/欧/美玩家常被默认指向高延迟官方 tunnel（200ms+），导致：
- 游戏中指令延迟明显
- 频繁切 tunnel 才能找到合适节点
- 新手根本不知道要切

## 2. 目标

- 启动 CnCNet 模式后，**默认选中延迟最低的 tunnel**
- 原始 `ServerList` 顺序**保持不变**（兼容 IRC 列表广播顺序）
- 支持并发预 ping，每个 server 测量完成就立即可读（无需等全部）
- 后续可扩展排序维度（官方优先、地理位置、协议版本）

## 3. 用户提出的核心方案

> 用小顶堆维护：
> 1. 不改变 ServerList 本来顺序
> 2. 可以用小顶堆直接取最小延迟
> 3. 每一个测量完的 server，入堆只需 O(log n)
> 4. 后续新增排序维度可直接扩展为 heapsort
> 5. 堆结构可以实现同步 ping 后快速入列排序，入堆时异步即可
> 6. 减少序列 ping 带来的时常与等待

**采用**。.NET 6+ 标准库 `System.Collections.Generic.PriorityQueue<TElement, TPriority>` 完美匹配（项目目标 `net8.0`）。

## 4. 与原方案的对比

| 维度 | 旧方案（LINQ OrderBy） | 新方案（小顶堆） |
|---|---|---|
| 单次取最优 | O(n log n)（每次重排） | O(1)（Peek） |
| 单 server 入列 | 必须等全部 ping 完才能排序 | O(log n)，完成一个入堆一个 |
| 原 ServerList 顺序 | 被 Sort 破坏 | 完全保留（堆独立） |
| 多维度排序 | 改 lambda 重排 | 改 `TunnelSortKey` 即可 |
| UI 渐进显示 | 不可行 | 入堆触发事件，UI 实时更新 |
| 并发安全 | 全量锁 | 单写线程 + ConcurrentQueue |
| 扩展性 | 每次新维度都改 sort | 只改 `TunnelSortKey.CompareTo` |

## 5. 架构

```
┌──────────────────────────────────────────────────────────────────┐
│                     CnCNet 启动流程                              │
│                                                                  │
│  1. CnCNetSessionService.Connect                                 │
│  2. 收到官方 tunnel 列表（~30 个）                                │
│  3. ★ TunnelPrewarmer.PrewarmAsync(tunnels)  ← 新加              │
│       ├─► ITunnelPinger.PingAsync(tunnel) × N 并发               │
│       │     每个 ping 完成 → TunnelSorter.Update(tunnel, ping)    │
│       │                       ├─► lock(heap) Enqueue O(log n)    │
│       │                       └─► raise BestTunnelChanged        │
│  4. TunnelSorter.TryPeekBest() → 自动选中                        │
│  5. UI：用户看到的 "已选中" 立即是最优 tunnel                     │
└──────────────────────────────────────────────────────────────────┘

                       后台周期性维护
                                   ▼
            ┌────────────────────────────────────────┐
            │  TunnelMaintenanceLoop (5 min)          │
            │   1. 取 top-K (K=5) 已知低延迟 tunnel    │
            │   2. 并发 re-ping                       │
            │   3. 更新 heap                          │
            │   4. 当前选中延迟 +50% 则切换到新最优   │
            └────────────────────────────────────────┘
```

## 6. 类设计

### 6.1 `TunnelSortKey`（堆的优先级）

```csharp
// 新文件：ClientAvalonia/CnCNet/Tunnels/TunnelSortKey.cs
namespace ClientAvalonia.CnCNet.Tunnels;

/// <summary>
/// Sort key for the tunnel priority queue. Encodes every dimension we may want
/// to sort on — adding a new dimension is a matter of adding a field here and
/// updating CompareTo. The heap never needs to change.
/// </summary>
public readonly record struct TunnelSortKey(
    int PingInMs,           // -1 means "not measured yet"; treated as int.MaxValue in comparison
    bool Official,          // official tunnels break ties ahead of community ones
    string Name) : IComparable<TunnelSortKey>
{
    public int CompareTo(TunnelSortKey other)
    {
        // 1) Latency ascending (lower is better). Unmeasured → worst.
        int a = PingInMs < 0 ? int.MaxValue : PingInMs;
        int b = other.PingInMs < 0 ? int.MaxValue : other.PingInMs;
        int cmp = a.CompareTo(b);
        if (cmp != 0) return cmp;

        // 2) Official wins ties (more trustworthy long-term).
        cmp = other.Official.CompareTo(Official);  // true > false, so reverse
        if (cmp != 0) return cmp;

        // 3) Stable alphabetical tiebreak.
        return string.Compare(Name, other.Name, StringComparison.Ordinal);
    }
}
```

### 6.2 `TunnelSorter`（小顶堆封装）

```csharp
// 新文件：ClientAvalonia/CnCNet/Tunnels/TunnelSorter.cs
namespace ClientAvalonia.CnCNet.Tunnels;

/// <summary>
/// Min-heap of CnCNet tunnels keyed by <see cref="TunnelSortKey"/>.
/// 
/// Why a heap (not LINQ OrderBy):
///  - O(1) peek of current best — needed every UI tick
///  - O(log n) incremental update — ping results arrive one by one
///  - preserves the original ServerList order (the heap is a separate index)
///  - multi-dimension sort is a struct CompareTo change, not a pipeline change
/// </summary>
public sealed class TunnelSorter
{
    private readonly PriorityQueue<CnCNetTunnel, TunnelSortKey> _heap = new();
    private readonly object _lock = new();
    private CnCNetTunnel? _currentBest;

    /// <summary>
    /// Raised on the UI thread whenever the best tunnel changes
    /// (either newly added with lower ping, or current one re-pinged worse).
    /// </summary>
    public event EventHandler<CnCNetTunnel>? BestTunnelChanged;

    /// <summary>Insert or refresh a tunnel's measurement. O(log n).</summary>
    public void Update(CnCNetTunnel tunnel, int pingInMs)
    {
        lock (_lock)
        {
            // PriorityQueue has no efficient update; we accept duplicates and
            // skip stale entries on Peek. With ~30 tunnels re-pinged every 5 min,
            // total memory overhead is trivial (a few hundred entries max).
            _heap.Enqueue(tunnel, new TunnelSortKey(pingInMs, tunnel.Official, tunnel.Name));
            ReevaluateBest();
        }
    }

    /// <summary>Current best tunnel, or null if no measurements yet. O(1).</summary>
    public CnCNetTunnel? TryPeekBest()
    {
        lock (_lock)
        {
            PurgeStalePeek();
            return _heap.TryPeek(out var tunnel, out _) ? tunnel : null;
        }
    }

    /// <summary>Force a re-peek (used by maintenance loop after mass re-ping).</summary>
    public void RefreshBest()
    {
        lock (_lock) ReevaluateBest();
    }

    /// <summary>Clear all entries (e.g. on tunnel list reload from IRC).</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _heap.Clear();
            _currentBest = null;
        }
    }

    private void ReevaluateBest()
    {
        PurgeStalePeek();
        if (!_heap.TryPeek(out var newBest, out _))
        {
            if (_currentBest != null)
            {
                _currentBest = null;
                RaiseBestChanged(null);
            }
            return;
        }

        if (!ReferenceEquals(newBest, _currentBest))
        {
            _currentBest = newBest;
            RaiseBestChanged(newBest);
        }
    }

    /// <summary>
    /// Pop entries whose latency is stale (i.e. a more recent measurement exists
    /// for the same tunnel) until we reach a live entry at the top. O(k log n)
    /// where k = stale entries purged; k is tiny in practice.
    /// </summary>
    private void PurgeStalePeek()
    {
        // Stale detection: if the top entry's recorded ping differs from the tunnel
        // object's current Ping value, it's an outdated heap entry.
        while (_heap.TryPeek(out var top, out var key))
        {
            if (top.Ping == key.PingInMs) break;
            _heap.Dequeue();   // discard stale
        }
    }

    private void RaiseBestChanged(CnCNetTunnel? newBest)
    {
        Dispatcher.UIThread.Post(() => BestTunnelChanged?.Invoke(this, newBest!));
    }
}
```

### 6.3 `ITunnelPinger`（测量器接口）

```csharp
// 新文件：ClientAvalonia/CnCNet/Tunnels/ITunnelPinger.cs
namespace ClientAvalonia.CnCNet.Tunnels;

/// <summary>
/// Abstraction for pinging a tunnel server. Default impl uses
/// System.Net.NetworkInformation.Ping; tests inject a fake.
/// </summary>
public interface ITunnelPinger
{
    /// <summary>
    /// Returns the round-trip latency in milliseconds, or -1 on failure
    /// (timeout, DNS failure, ICMP blocked, etc.).
    /// </summary>
    Task<int> PingAsync(CnCNetTunnel tunnel, CancellationToken ct = default);
}

internal sealed class IcmpTunnelPinger : ITunnelPinger
{
    public async Task<int> PingAsync(CnCNetTunnel tunnel, CancellationToken ct = default)
    {
        try
        {
            using var p = new System.Net.NetworkInformation.Ping();
            // 3 samples, take min — matches CnCNetTunnel.UpdatePing semantics.
            int best = -1;
            for (int i = 0; i < 3; i++)
            {
                var reply = await p.SendPingAsync(tunnel.Address, 1500).WaitAsync(ct);
                if (reply.Status == IPStatus.Success)
                {
                    int ms = (int)reply.RoundtripTime;
                    if (best == -1 || ms < best) best = ms;
                }
            }
            return best;
        }
        catch
        {
            return -1;
        }
    }
}
```

### 6.4 `TunnelPrewarmer`（启动预 ping）

```csharp
// 新文件：ClientAvalonia/CnCNet/Tunnels/TunnelPrewarmer.cs
namespace ClientAvalonia.CnCNet.Tunnels;

/// <summary>
/// Pings all tunnels concurrently on CnCNet startup; each result enters the
/// heap immediately so the UI can pick a best tunnel as soon as the fastest
/// one responds — no need to wait for slow/timed-out servers.
/// </summary>
public sealed class TunnelPrewarmer
{
    private readonly ITunnelPinger _pinger;
    private readonly TunnelSorter _sorter;
    private readonly int _concurrency;

    public TunnelPrewarmer(ITunnelPinger pinger, TunnelSorter sorter, int concurrency = 8)
    {
        _pinger = pinger;
        _sorter = sorter;
        _concurrency = concurrency;
    }

    public async Task PrewarmAsync(IReadOnlyList<CnCNetTunnel> tunnels, CancellationToken ct = default)
    {
        // Parallel async iteration: each task completes independently and pushes
        // its result into the heap. The heap raises BestTunnelChanged on the first
        // (and any subsequent) best-tunnel change, so the UI updates incrementally.
        await Parallel.ForEachAsync(
            tunnels,
            new ParallelOptions { MaxDegreeOfParallelism = _concurrency, CancellationToken = ct },
            async (tunnel, token) =>
            {
                int ping = await _pinger.PingAsync(tunnel, token);
                tunnel.Ping = ping;   // also surface on the tunnel object for UI list display
                _sorter.Update(tunnel, ping);
            });
    }
}
```

### 6.5 `TunnelMaintenanceLoop`（周期维护）

```csharp
// 新文件：ClientAvalonia/CnCNet/Tunnels/TunnelMaintenanceLoop.cs
namespace ClientAvalonia.CnCNet.Tunnels;

/// <summary>
/// Periodically re-pings the top-K known low-latency tunnels (cheaper than full
/// re-ping) and auto-switches the user's selected tunnel if the current one
/// degrades by more than 50%.
/// </summary>
public sealed class TunnelMaintenanceLoop : IDisposable
{
    private readonly ITunnelPinger _pinger;
    private readonly TunnelSorter _sorter;
    private readonly Func<IReadOnlyList<CnCNetTunnel>> _getAllTunnels;
    private readonly Func<CnCNetTunnel?> _getSelected;
    private readonly Action<CnCNetTunnel> _setSelected;
    private readonly Timer _timer;
    private const int TopK = 5;
    private const double SwitchHysteresis = 1.5;  // current must be 1.5× worse than best to switch

    public TunnelMaintenanceLoop(
        ITunnelPinger pinger,
        TunnelSorter sorter,
        Func<IReadOnlyList<CnCNetTunnel>> getAllTunnels,
        Func<CnCNetTunnel?> getSelected,
        Action<CnCNetTunnel> setSelected,
        TimeSpan? interval = null)
    {
        _pinger = pinger;
        _sorter = sorter;
        _getAllTunnels = getAllTunnels;
        _getSelected = getSelected;
        _setSelected = setSelected;
        _timer = new Timer(_ => _ = TickAsync(), null, interval ?? TimeSpan.FromMinutes(5), interval ?? TimeSpan.FromMinutes(5));
    }

    private async Task TickAsync()
    {
        var tunnels = _getAllTunnels();
        // Re-ping top-K + currently selected (in case it's outside top-K but still preferred)
        var toReping = PickTopK(tunnels, TopK);
        var selected = _getSelected();
        if (selected != null && !toReping.Contains(selected)) toReping.Add(selected);

        await Parallel.ForEachAsync(toReping, async (t, ct) =>
        {
            int ping = await _pinger.PingAsync(t, ct);
            t.Ping = ping;
            _sorter.Update(t, ping);
        });

        // Auto-switch if current is significantly worse than the best.
        var best = _sorter.TryPeekBest();
        if (best != null && selected != null && selected != best
            && best.Ping > 0 && selected.Ping > 0
            && selected.Ping > best.Ping * SwitchHysteresis)
        {
            _setSelected(best);
        }
    }

    private static List<CnCNetTunnel> PickTopK(IReadOnlyList<CnCNetTunnel> all, int k)
        => all.Where(t => t.Ping > 0).OrderBy(t => t.Ping).Take(k).ToList();

    public void Dispose() => _timer.Dispose();
}
```

### 6.6 装配（`CnCNetSessionService`）

```csharp
// 在 CnCNetSessionService.cs 中
public sealed class CnCNetSessionService
{
    public TunnelSorter TunnelSorter { get; } = new();
    private ITunnelPinger _pinger = new IcmpTunnelPinger();
    private TunnelPrewarmer? _prewarmer;
    private TunnelMaintenanceLoop? _maintenance;

    private async Task OnCnCNetTunnelsUpdateCompleted(IReadOnlyList<CnCNetTunnel> tunnels)
    {
        TunnelSorter.Clear();
        _prewarmer = new TunnelPrewarmer(_pinger, TunnelSorter);
        await _prewarmer.PrewarmAsync(tunnels);

        // Subscribe: when best tunnel changes during prewarm or maintenance,
        // auto-select it (unless the user has manually picked one).
        TunnelSorter.BestTunnelChanged += OnBestTunnelChanged;
        _maintenance = new TunnelMaintenanceLoop(
            _pinger, TunnelSorter,
            getAllTunnels: () => _currentTunnels,
            getSelected: () => SelectedTunnel,
            setSelected: t => SelectedTunnel = t);
    }

    private void OnBestTunnelChanged(object? sender, CnCNetTunnel best)
    {
        if (!_userManuallySelectedTunnel)
            SelectedTunnel = best;
    }
}
```

## 7. UI 集成

`MainWindow.axaml.cs` 中现有 tunnel dropdown：

```csharp
// 改造前：用户必须手动点 dropdown 选 tunnel
// 改造后：dropdown 默认显示 sorter 推荐的最优 tunnel

private void OnCnCNetConnected()
{
    // 订阅 sorter 事件：UI 自动跟随
    _cncnetSession.TunnelSorter.BestTunnelChanged += (_, best) =>
    {
        ddTunnel.SelectedItem = best;   // 单次赋值，O(1)，无重排
        lblTunnelPing.Text = $"{best.Ping} ms";
    };
}
```

UI 列表渲染（如显示全部 tunnel 的列表）继续用原始 `ServerList` 顺序，**不被堆打乱**——这是堆方案的核心优势之一。

## 8. 测试策略

### 8.1 `TunnelSortKey` 排序正确性

```csharp
[Fact]
public void Lower_Ping_Wins()
{
    var a = new TunnelSortKey(50, Official: false, "A");
    var b = new TunnelSortKey(200, Official: true, "B");
    a.CompareTo(b).Should().BeLessThan(0);
}

[Fact]
public void Official_Breaks_Tie()
{
    var official = new TunnelSortKey(50, Official: true, "X");
    var community = new TunnelSortKey(50, Official: false, "Y");
    official.CompareTo(community).Should().BeLessThan(0);
}

[Fact]
public void Unmeasured_Treated_As_Worst()
{
    var unmeasured = new TunnelSortKey(-1, Official: true, "Official");
    var measured = new TunnelSortKey(999, Official: false, "Slow");
    measured.CompareTo(unmeasured).Should().BeLessThan(0);
}
```

### 8.2 `TunnelSorter` 增量更新

```csharp
[Fact]
public async Task Update_Raises_BestTunnelChanged_On_New_Min()
{
    var sorter = new TunnelSorter();
    CnCNetTunnel? raised = null;
    sorter.BestTunnelChanged += (_, t) => raised = t;

    sorter.Update(Tunnel("A", 200), 200);
    sorter.Update(Tunnel("B", 50), 50);   // becomes new best
    sorter.Update(Tunnel("C", 100), 100); // not better than B

    await DispatcherTestHelper.FlushAsync();
    raised!.Name.Should().Be("B");
    sorter.TryPeekBest()!.Name.Should().Be("B");
}

[Fact]
public async Task Update_Reping_Better_Updates_Best()
{
    var sorter = new TunnelSorter();
    var a = Tunnel("A", 200);
    sorter.Update(a, 200);

    sorter.Update(a, 30);  // A re-pinged, much better

    await DispatcherTestHelper.FlushAsync();
    sorter.TryPeekBest()!.Ping.Should().Be(30);
}
```

### 8.3 `TunnelPrewarmer` 并发与渐进

```csharp
[Fact]
public async Task Prewarm_Pings_All_Tunnels_And_Updates_Sorter()
{
    var fakePinger = new FakePinger
    {
        Responses = { ["A"] = 200, ["B"] = 50, ["C"] = 100 }
    };
    var sorter = new TunnelSorter();
    var prewarmer = new TunnelPrewarmer(fakePinger, sorter);

    await prewarmer.PrewarmAsync(new[] { Tunnel("A"), Tunnel("B"), Tunnel("C") });

    sorter.TryPeekBest()!.Name.Should().Be("B");
}

[Fact]
public async Task Prewarm_Raises_Changed_As_Soon_As_First_Responds()
{
    var slowPinger = new FakePinger
    {
        // A responds instantly, B/C delay 500ms
        Responses = { ["A"] = 200 },
        Delays = { ["B"] = 500, ["C"] = 500 }
    };
    var sorter = new TunnelSorter();
    int changeCount = 0;
    sorter.BestTunnelChanged += (_, _) => changeCount++;

    var prewarmer = new TunnelPrewarmer(slowPinger, sorter);
    var task = prewarmer.PrewarmAsync(new[] { Tunnel("A"), Tunnel("B"), Tunnel("C") });

    // Within 100ms A should have entered the heap and raised an event.
    await Task.Delay(100);
    (await DispatcherTestHelper.FlushAsync(), changeCount).Should().Be(1); // approximate

    await task;
}
```

### 8.4 `TunnelMaintenanceLoop` 自动切换

```csharp
[Fact]
public async Task AutoSwitches_When_Current_Degrades()
{
    var tunnels = new[] { Tunnel("A", 50), Tunnel("B", 100) };
    CnCNetTunnel selected = tunnels[1];   // start with B

    var fakePinger = new FakePinger();
    var sorter = new TunnelSorter();
    sorter.Update(tunnels[0], 50);
    sorter.Update(tunnels[1], 100);

    var loop = new TunnelMaintenanceLoop(
        fakePinger, sorter,
        getAllTunnels: () => tunnels,
        getSelected: () => selected,
        setSelected: t => selected = t,
        interval: TimeSpan.FromMilliseconds(50));

    // B's ping jumps to 200; on next tick should switch to A
    fakePinger.Responses["B"] = 200;
    await Task.Delay(200);

    selected.Name.Should().Be("A");
}
```

## 9. 兼容性与回退

- `ITunnelPinger` 默认 `IcmpTunnelPinger`，失败返回 -1（视作最差）
- 若用户手动选过 tunnel（`_userManuallySelectedTunnel = true`），`BestTunnelChanged` 事件不再覆盖
- 若所有 tunnel ping 都失败（全 -1），sorter.Peek 返回 null → 回退到原有"取第一个官方 tunnel"逻辑
- 若 `PriorityQueue` 在某些极端环境不可用（不可能，net8.0 标配），可降级为 `SortedDictionary`

## 10. 工时预估

| 阶段 | 工时 |
|---|---|
| `TunnelSortKey` + `TunnelSorter` + 单测 | 3h |
| `ITunnelPinger` + `IcmpTunnelPinger` 实现 | 1.5h |
| `TunnelPrewarmer` + 单测 | 2h |
| `TunnelMaintenanceLoop` + 单测 | 2h |
| `CnCNetSessionService` 装配 + UI 订阅 | 2h |
| 集成测试（实网 ping） | 2h |
| **总计** | **~12.5h**（1.5-2 工作日） |

## 11. 关键决策记录

| 决策 | 理由 |
|---|---|
| 用 `PriorityQueue<T,E>`（用户方案） | O(1) peek、O(log n) 增量更新、并发友好 |
| 堆内允许重复 entry，惰性清除 | `PriorityQueue` 不支持高效 update；30 个 server 每 5 分钟一次重 ping，总 entry 量 < 1000，无内存压力 |
| `BestTunnelChanged` 事件化 | 让 UI 渐进刷新，无需"等所有 ping 完" |
| 默认开自动选，用户手动选后让位 | 与"用户至上"一致；避免反复抢用户选择 |
| TopK=5 re-ping | 平衡精度与流量；避免每 5 分钟全量 ICMP 风暴 |
| 切换阈值 1.5× | 避免乒乓切换 |
