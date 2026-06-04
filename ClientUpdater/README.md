# ClientUpdater（更新器库）

## 这是什么
`ClientUpdater` 是客户端的更新/自检库，负责：
- 从更新服务器拉取 `version` 文件并解析
- 对比本地 `version` 与服务器 `version`，计算需要下载的文件列表
- 支持多个下载镜像（mirrors）并按配置排序
- 下载文件（支持归档 `.lzma`）并做完整性校验
- 支持“自定义组件”（AddOns/可选内容）的版本检查与下载
- 通过事件向 UI/调用方汇报进度、状态变化与失败原因

项目引用：`ClientCore`（路径/扩展/工具等）与 `Microsoft.AspNet.WebApi.Client`。

## 目录结构概览
- `Updater.cs`：更新器核心流程（版本检查、下载、校验、事件回调）
- `UpdaterFileInfo.cs`：单个文件的元信息（文件名、大小、标识符、是否归档）
- `VersionState.cs`：版本状态枚举（up-to-date/outdated/进行中等）
- `UpdateMirror.cs`：镜像站点信息（URL/名称/地理位置）
- `CustomComponent.cs`：自定义组件（AddOn）的下载与校验逻辑
- `Compression/`：LZMA 压缩/解压实现与 SevenZip 相关代码

## 核心流程（Updater.cs）
### 初始化
- `Initialize(gamePath, resourcePath, settingsIniName, localGame, callingExecutableFileName)`
  - 设置路径与基础信息
  - 读取更新器配置（镜像列表、自定义组件等）
  - 根据 `DownloadMirrors` 配置对镜像排序（配置优先，其余追加）

### 版本检查 & 本地校验
- `CheckForUpdates()`：触发异步版本检查（避免重入）
- `CheckLocalFileVersions()`：读取本地 `version`，解析 `FileVersions`/`ArchivedFiles` 列表生成 `LocalFileInfos`
  - 其中 `UpdaterFileInfo.Archived` 用于判定是否存在归档下载（`.lzma`）

### 下载、进度与事件
更新器内部使用 `HttpClient + ProgressMessageHandler` 追踪下载进度，并通过事件对外广播：
- `UpdateProgressChanged(currFileName, currFilePercentage, totalPercentage)`：当前文件与总体进度
- `OnFileDownloadCompleted(archiveName)`：单个归档下载完成
- `OnVersionStateChanged()`：`VersionState` 更新
- `OnUpdateCompleted()` / `OnUpdateFailed(Exception ex)`：更新完成/失败
- `OnLocalFileVersionsChecked()`：本地 version 解析完成
- `OnCustomComponentsOutdated()`：检测到自定义组件过期

## 自定义组件（CustomComponent.cs）
`CustomComponent` 用于处理“非主版本文件列表”的附加内容（AddOns），特点：
- 先下载 `version`（写入临时文件 `version_cc`），从中读取 `AddOns` 段落的条目与归档信息
- 支持下载 URL 绝对/相对路径、以及控制是否追加归档扩展名
- 下载完成后通过“文件唯一标识符”（与 version 中记录的 Identifier）校验，失败会重试（有上限）
- 若为归档文件：先校验归档标识符，再用 `CompressionHelper.DecompressFileAsync` 解压到临时文件
- 通过 `DownloadProgressChanged` 事件上报组件下载百分比

## 压缩（Compression/）
- `CompressionHelper.cs`
  - 提供 `CompressFileAsync` / `DecompressFileAsync`
  - 使用 LZMA（SevenZip）格式：写入 coder properties + 原始文件长度，再进行编码/解码
- 其余子目录（`LZ/`、`LZMA/`、`RangeCoder/` 等）为底层算法实现与接口

## 注意点（实现层面的约束）
- 代码区分 `NETFRAMEWORK` 与非 `NETFRAMEWORK`：
  - 二阶段更新器文件名（`.exe` vs `.dll`）
  - 默认二进制目录（`Binaries` vs `BinariesNET8`）
  - `HttpClientHandler` vs `SocketsHttpHandler`（解压/连接池策略差异）

## 文件速查（按路径）
### 顶层
- `Updater.cs`：更新主流程与状态机；版本检查、文件对比、下载/校验、事件回调。
- `UpdaterFileInfo.cs`：单文件元信息（Identifier/ArchiveIdentifier/Size…）。
- `VersionState.cs`：更新器状态枚举。
- `UpdateMirror.cs`：下载镜像信息（URL/名称/位置）。
- `CustomComponent.cs`：自定义组件（AddOn）下载、进度上报、校验与解压。

### Compression/
- `CompressionHelper.cs`：对外的 LZMA 压缩/解压入口。
- `ICoder.cs`：SevenZip 编码/解码接口与异常定义（自动生成代码）。

#### Compression/Common/
- `CRC.cs`：CRC 校验实现。
- `InBuffer.cs` / `OutBuffer.cs`：流缓冲读写工具。
- `CommandLineParser.cs`：通用参数解析辅助（主要服务于压缩实现的通用代码）。

#### Compression/LZ/
- `IMatchFinder.cs`：匹配查找器接口。
- `LzInWindow.cs` / `LzOutWindow.cs`：LZ 输入/输出窗口缓冲。
- `LzBinTree.cs`：二叉树匹配查找实现。

#### Compression/LZMA/
- `LzmaBase.cs`：LZMA 基础常量与共享逻辑。
- `LzmaEncoder.cs`：LZMA 编码器。
- `LzmaDecoder.cs`：LZMA 解码器。

#### Compression/RangeCoder/
- `RangeCoder.cs`：范围编码核心。
- `RangeCoderBit.cs` / `RangeCoderBitTree.cs`：按 bit 与 bit-tree 的范围编码辅助。
