# ClientAvalonia：CnCNet 频道解析漏斗

| 项 | 内容 |
|----|------|
| 状态 | 已落地 |
| 目标 | MG / LNOD 等多 mod 在 Collection 不全时仍能接入 CnCNet |
| 实机基线 | LNOD DX `client.log`：`JOIN #cncnet-lnod-games` → `JOIN #cncnet-lnod ra1-derp` → `JOIN #cncnet` |

---

## 1. 评估结论

| 场景 | Collection | ClientDefinitions 频道键 | 漏斗结果 | 能否接入 |
|------|------------|--------------------------|----------|----------|
| MG | 有 `#yuanming-*` | 无 | CustomGames | 能 |
| LNOD（实机） | 空 | 无 | **LocalGame 约定** `#cncnet-lnod` / `#cncnet-lnod-games` | 能（对齐 LNOD DX） |
| 显式覆盖 | 任意 | 有 `CnCNetChatChannel` 等 | ClientDefinitions | 能 |
| 内置 yr/dta… | 内置表 | — | Built-in | 能 |

`RA2MD.ini` / `RA2MG.ini` 的 `[Channels] Xxx=True` **不参与定名**，只影响是否额外 JOIN 其它游戏的广播房（与 DX `IsGameFollowed` 一致）。

---

## 2. 漏斗优先级（定名）

```text
1. Built-in GameCollection 表（dta/ti/mo/yr/…）
2. Resources/GameCollectionConfig.ini → [CustomGames] ChatChannel / GameBroadcastChannel
3. ClientDefinitions.ini → CnCNetChatChannel / CnCNetGameBroadcastChannel
      （只填一侧时另一侧 mirror）
4. LocalGame 约定（LNOD DX）：
      Chat      = #cncnet-{LocalGame}
      Broadcast = #cncnet-{LocalGame}-games
5. 仍失败 → 拒绝连接并提示配置
```

实现入口：

- `CnCNetLocalGameChannelResolver` — 第 3–4 层纯逻辑
- `CnCNetGameCollection.TryAddImplicitLocalGame` — 在 1–2 层未命中时调用 resolver 注入隐式条目
- `CnCNetSession.ConnectIfNeeded` — 仍要求 `GetLocalGame()` 非空

---

## 3. 关注层（非定名）

连接成功后：

- 始终 JOIN 本机 `ChatChannel` + `GameBroadcastChannel` + `#cncnet`
- 其它游戏的 broadcast：仅当 Settings（`SettingsFile`，如 `RA2MD.ini`）`[Channels] {INTERNALNAME}=True` 时 JOIN

---

## 4. 测试

- `CnCNetLocalGameChannelResolverTests` — 约定频道与优先级
- `CnCNetGameCollectionImplicitLocalGameTests` — LNOD 合成 / ClientDefinitions 覆盖 / MG CustomGames 优先
- `CnCNetWelcomeChannelPlanTests` — welcome JOIN 顺序（chat → `#cncnet` → broadcast）
- **Integration** `CnCNetMgAndLnodJoinIntegrationTests` — 模拟 MG / LNOD 磁盘布局，断言两端均可形成完整大厅 JOIN 计划

```bash
dotnet test ClientAvalonia.Tests --filter "FullyQualifiedName~CnCNetMgAndLnodJoin" `
  -p:DisableGitVersionTask=true -p:GitVersion_MsBuildTask_Disabled=true
```

---

## 5. 与实机 JOIN 对照

| 端 | chat | broadcast | general |
|----|------|-----------|---------|
| MG（Collection） | `#yuanming-games` | `#yuanming-cg-games` | `#cncnet` |
| LNOD（约定，对齐 DX log） | `#cncnet-lnod` | `#cncnet-lnod-games` | `#cncnet` |