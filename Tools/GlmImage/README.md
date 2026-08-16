# GLM-Image 本地生成工具

调用智谱 `open.bigmodel.cn` 的 GLM-Image API。API key 放在本地 `config.json`（已 gitignore）。

## 配置

```powershell
Copy-Item config.example.json config.json
# 编辑 apiKey
```

## 用法

```powershell
.\generate.ps1 -Prompt (Get-Content .\prompts\world_map_holographic.txt -Raw) -Size "1728x960" -Out ".\output\world_map_raw.png"
```

可选 `-ImagePath` 做图生图。**世界地图不要附带「网络连线」参考图**——参考会把 mesh/plexus 带进结果；全息感用提示词描述即可。

## 世界地图

固化提示词：`prompts/world_map_holographic.txt`

目标：等距圆柱、全息青轮廓 + 柔和剪影填充、城市点、无连线/无三角网格，色调对齐启动器 `#2EE6C5` / `#050709`。

生成后人工验收，确认无水印/无网格，再复制到 `ClientAvalonia/Assets/Glm/world_map.png`。底部「AI生成」徽标可用 `process_assets.ps1` **仅裁切底边**（不做像素级修补）。
