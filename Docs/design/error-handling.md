# 错误处理统一策略

> **状态**：实施中（P0）。第一批改造覆盖 `CnCNet/CnCNetHttp.cs`。

## 1. 分类原则

所有 IO / 网络错误按"重试是否有用"分两类：

| 类别 | 例子 | 用户提示 | 调用方动作 |
|---|---|---|---|
| **Network**（瞬时） | DNS 失败、超时、连接拒绝、连接中断 | "网络连接失败，请检查后重试" | 可重试 / 退避 |
| **Business**（永久） | HTTP 4xx / 5xx、URL 格式错误、解码失败 | "服务器返回错误（500）" | 不重试，提示用户 |

判断依据：服务端是否响应过。服务端响应了但内容不对 → Business；连不上 → Network。

## 2. 统一返回类型

`ClientAvalonia.CnCNet.HttpResult<T>` + `HttpError`。

```csharp
HttpResult<string> result = CnCNetHttp.TryDownloadString(url);
if (result.TryGetValue(out string? body))
{
    // 成功路径
}
else
{
    HttpError err = result.Error!;
    if (err.Kind == HttpErrorKind.Network)
        // retry with backoff
    else
        // show user-friendly message, do not retry
}
```

旧 API（`DownloadString` 返回 `string?`）保留以避免大范围重构，内部委托给新 API。

## 3. 各层错误处理约定

| 层 | 失败风格 | 理由 |
|---|---|---|
| INI 解析（`IniDocument` / `IniUiTreeBuilder`） | 抛 `InvalidOperationException` | 解析错误是程序员责任，必须早 fail |
| 顶层调用（`LayoutEngine.LoadWindow`） | 捕获并包成 `Result`，绝不向 UI 抛 | UI 永远拿到结构化错误，不崩 |
| HTTP（`CnCNetHttp`） | 返回 `HttpResult<T>`，绝不抛 | 网络失败常态，调用方需要分类 |
| 注册表 / 配置（`InstallationRegistry`） | `Try*(out error)` 模式 | 与上游 ClientCore 风格一致 |
| 进程启动（`GameProcessLauncher`） | 返回 Result 或抛 | 启动失败必须让用户看到原因 |
| 日志（`Logger`） | 仅 try-catch 内部，不抛 | 日志失败不能影响业务 |

## 4. 各层接入清单

### 4.1 已完成

- `CnCNetHttp.TryDownloadString` / `TryDownloadBytes`（本次提交）

### 4.2 待迁移（按收益排序）

| 文件 | 改造点 |
|---|---|
| `CnCNetTunnelListLoader` | 把 catch-all 改成 `HttpResult<T>`，让 UI 知道 tunnel 列表失败是网络还是业务 |
| `CnCNetOnlineIdentity` | 同上，身份生成 HTTP 失败要区分 |
| `LayoutEngine.LoadWindow` | 捕获 `IniUiTreeBuilder` 的异常，包成 `Result<UiNodeTree, LoadError>` |
| `GameProcessLauncher` | 启动失败返回 `Result<Process, LaunchError>` 而非静默吞 |
| `MainWindow.NavigateTo` | 把 `catch (Exception ex)` 改成显示用户可懂的错误对话框 |
| `Startup.TryDeleteUpdaterTempFolder` | 保留静默（这些是清理失败，不影响启动） |

### 4.3 不要改的

- `Logger.Log` — 永远静默
- `SafePath.DeleteFileIfExists` — 已经返回 bool，调用方判断
- `ClientConfiguration.Instance.X` — 上游 ClientCore API，不在我们管辖范围

## 5. 用户文案

UI 文案层面，按错误类型给提示：

```
Network:
  "网络连接失败，请检查网络后重试。"
  "Connection failed. Please check your network and try again."

Business:
  "服务器返回错误（HTTP 500），请稍后重试。"
  "Server returned an error (HTTP 500). Please try again later."

Unknown:
  "操作失败：{ex.Message}"
  "Operation failed: {ex.Message}"
```

中英混排：日志用英文（便于机器解析），用户可见文案走 i18n 资源（待 i18n 改造，不在本次范围）。

## 6. 测试

`HttpResult<T>` 本身单元测试覆盖：
- `Success` 路径
- `Failure(Network)` 路径
- `Failure(Business)` 路径
- `Map` 操作保留 error

`CnCNetHttp.TryDownload*` 集成测试需要 mock `WebRequest`，未来引入 `HttpMessageHandler` 抽象后再补。
