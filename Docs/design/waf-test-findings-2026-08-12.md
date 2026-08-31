# WAF 矩阵测试发现报告（2026-08-12）

> **性质**：仅发现 / 修改方案 —— **本轮未改动生产侧 WAF 代码**。  
> **套件**：`ClientAvalonia.Tests/CnCNet/` 下四套约 100 例矩阵。  
> **运行**（Debug，禁用 GitVersion）：

```text
dotnet test ClientAvalonia.Tests/ClientAvalonia.Tests.csproj -c Debug
  -p:DisableGitVersionTask=true -p:GitVersion_MsBuildTask_Disabled=true
  --filter "FullyQualifiedName~WafCapability|FullyQualifiedName~WafFilterContent|FullyQualifiedName~WafSessionIntegration|FullyQualifiedName~WafBlocklistLearn"
```

---

## 1. 新增文件

| 路径 | 作用 |
|---|---|
| `ClientAvalonia.Tests/CnCNet/WafCapabilityMatrixTests.cs` | 设置开关 / 屏蔽名单 CRUD / 策略 / 偏好 / 规则包边界 |
| `ClientAvalonia.Tests/CnCNet/WafFilterContentMatrixTests.cs` | 内容类 × 表面 × 灵敏度的 Evaluate；含注入式挂机农场协议包 |
| `ClientAvalonia.Tests/CnCNet/WafSessionIntegrationMatrixTests.cs` | TempGameRoot 持久化、BlockFromAlert 重载、peek、并发 Evaluate |
| `ClientAvalonia.Tests/CnCNet/WafBlocklistLearnSchemaMatrixTests.cs` | 学习：nick/body/host/ident、紧凑指纹、列表正文 body 键 |
| `Docs/design/waf-test-findings-2026-08-12.md` | 本报告 |

**按要求未改业务逻辑：** `ClientAvalonia.Tests/Lan/LanProtocolAndLoadSupportTests.cs`（仅补了编译所需的 `using`）。

**非 WAF 编译解阻：** IniUi `IUiNavigationHost` 在接口扩展后需实现 `TryLaunchLanGame` / `OpenLoadGameOverlay`。

---

## 2. 数量与结果

每套含 **≥100** 条 Theory 用例，外加 1 个 `*_Has_At_Least_100_Cases` Fact → 每类 **101** 条 xUnit 结果。

| 套件 | 结果数 | 通过 | 失败 |
|---|---:|---:|---:|
| `WafCapabilityMatrixTests` | 101 | 100 | **1** |
| `WafFilterContentMatrixTests` | 101 | 99 | **2** |
| `WafSessionIntegrationMatrixTests` | 101 | 101 | 0 |
| `WafBlocklistLearnSchemaMatrixTests` | 101 | 101 | 0 |
| **合计** | **404** | **401** | **3** |

TRX：`ClientAvalonia.Tests/TestResults/waf-matrix.trx`。

---

## 3. 失败用例（按根因归类）

### A. 低灵敏度 + 纯 URL 得分 → Allow（2 例失败）

| 用例 | 期望 | 实际 |
|---|---|---|
| `cap.settings.sensitivity_0_url_warns` | Warn | Allow |
| `flt.sens.url_0` | Warn | Allow |

**根因：** 默认规则包灵敏度 `0` 的 `warn` 阈值为 **30**，而 `content.url` 单条得分为 **25**。因此仅含 URL 的流量达不到 Low 档阈值，`Evaluate` 返回 Allow。同一文本在中（`1`，warn 25）/ 高（`2`，warn 15）档仍会 Warn。

**含义：** 产品「低灵敏度」会**静默放过**仅含 URL 的大厅/私聊，除非同时命中其它内容类。测试故意期望 Warn，用以暴露该缺口。

### B. 默认包「protocol 为空」≠ HostBot 夹具 Allow（1 例失败）

| 用例 | 期望 | 实际 |
|---|---|---|
| `flt.proto.default_pack_allows_r8` | Allow | Warn |

**根因：** `rules.default.json` 中 `protocol: []` 且 `hostBotTunnels` 为空，故 R8 / 隧道黑名单等协议启发式关闭。但 `WafAttackFixtures.HostBotGame()` 房名仍为 **「免费代练房」**，在 `CheckListingText` 开启时命中 **列表正文** `content.promo` → 即使无协议规则也会 Warn。

**含义：**「协议层挂机农场关闭」并不等于 HostBot 形态广播一律 Allow；列表/内容规则仍会触发。若只想测协议层 Allow，需同时关闭 listing 检查，或使用中性房名/地图/模式文案。

---

## 4. 已通过、可作回归锚点的覆盖面

以下均已通过，可作为当前行为的回归基准：

- **能力面：** Enabled / 各表面开关；屏蔽名单 CRUD 与键规范化；策略 Off/Warn/Drop；注入挂机农场包后的 `proto.*` 策略列表；规则包边界（非法正则跳过、空 contentClasses）。
- **过滤面：** 辱骂 / 推广 / 联系方式 / 英文推广 / 欺诈 / 色情 / 威胁 / 仇恨 / 自伤 / 儿童安全等 Warn 路径；干净文本 Allow；策略 Drop 静默；注入协议包对 R8 / 隧道 / 假人 / 字段数的 Warn。
- **会话集成：** `TempGameRoot` + `ProgramConstantsSerial`；BlockFromAlert → 异步落盘 → `LoadUserList` 后 Drop；策略偏好 Save/Load；peek + Evaluate；并发 Evaluate 冒烟；UserListStore 往返。
- **学习 / schema：** 封禁 nick 后同昵静默 Drop；body 指纹跨昵称；ZWSP/空格紧凑变体；`host=` / `ident=` 角色键；解封 nick 后 body 仍 Drop；`body=` 跨新实例持久化；列表正文 body 键。

---

## 5. 建议修改方案（后续生产侧，本轮不做）

后续 **生产代码** 修改建议优先级（本轮未实施）：

1. **灵敏度 × 类别得分对齐（P0）**  
   - 要么提高 Low 档下 `content.url`（或 contact）得分，使单一高信号类能达到 `warn`；要么把 Low 明确成「多信号才告警」，并改 Options 文案。  
   - 复核 `rules.default.json` 灵敏度表（`0.warn=30` vs url `score=25`）。

2. **厘清挂机农场 vs 列表文案（P0 文档 / P1 代码）**  
   - 若 bot 用存量 GAME 格式，可继续保持 `protocol: []`，但须文档写明：列表推广仍会 Warn。  
   - 可选：为农场运维提供带 `protocol` + `hostBotTunnels` 的覆盖规则包；测试已可用 `CompileFromJson` 注入。  
   - 将仍假设 Default 包含 `HostBotTunnels` / `proto.*` 的旧测试（如部分 `WafRulePackLoaderTests` / `WafUnitCoverageTests`）与「空 protocol」策略对齐。

3. **夹具卫生（P2 测试）**  
   - 增加「中性房名」HostBot 夹具，专测协议隔离（RoomName 无「代练/加群」）。  
   - 保留带推广文案的 HostBot 夹具，用于 listing + protocol 组合场景。

4. **学习 / 屏蔽名单（已较扎实 — P3 打磨）**  
   - UI 可考虑分别解封 nick 与 body（底层行为已正确）。  
   - 可选：说明紧凑归一可能把不同垃圾模板折成同一指纹。

5. **CI 门禁**  
   - 灵敏度/产品策略敲定后，再把这四套矩阵纳入 CI 硬门禁；在此之前，将失败 A/B 视为**已知策略探针**，而非基础设施抖动。

---

## 6. 小结

四套矩阵（**404** 条结果，每套约 **100** 例）在**不改生产 WAF** 的前提下跑完：**401 通过 / 3 失败**。失败集中在：(1) Low 灵敏度对纯 URL（得分 25）不告警；(2) 默认包 protocol 为空时，HostBot 房名推广仍 Warn。后续宜做**策略/阈值对齐**与**挂机农场 vs 列表**说明，而不是悄悄放宽测试期望。
