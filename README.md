# NotifyRelay Overlay

桌面端超级岛与通知的叠加层渲染库（Windows / Direct2D）。

由 JVM 桌面端以**伴生进程**方式使用：`OverlayHost.exe` 承载渲染，两者经 TCP 回环 + UTF-8 JSON 行通信，进程隔离保证渲染异常不影响主程序。

## 结构

| 路径 | 内容 |
|---|---|
| `Services/Overlay/` | 渲染服务、窗口管理、看门狗、数据契约（`IOverlayDataSink`） |
| `Services/Overlay/UI/` | 绘制节点（布局、叶子、控件、岛屿卡片与模板） |
| `Models/Render/` | 渲染数据模型（弹幕样式、超级岛状态、ParamV2 解析） |
| `OverlayHost/` | 宿主进程：通道服务 + 默认设置适配 + 程序入口 |

## 构建

```bash
dotnet build OverlayHost/OverlayHost.csproj -c Release
```

需要 .NET 10 SDK 与 Windows 10 2004+（Direct2D）。

## 运行

```bash
OverlayHost/bin/Release/net10.0-windows10.0.26100/OverlayHost.exe [端口]
```

端口默认 `45678`。

## 通道协议

TCP 回环，逐行 UTF-8 JSON，按 `type` 分派：

| type | 字段 |
|---|---|
| `danmaku` | `appName` `title` `body` `iconPng`(base64) `deviceName` `sourceKey` |
| `media_update` | `deviceId` `deviceName` `title` `artist` `coverPng`(base64) `isPlaying` |
| `media_remove` | `deviceId` |
| `style_update` | `danmaku.{fontSizePercent,speed,opacityPercent,displayAreaPercent,density,...}` |
| `superisland_update` / `superisland_remove` | 预留，尚未接线 |

`danmaku.speed` 语义为 `×60 px/s`（默认 3 ≈ 180 px/s，取值范围 1~20）。

## 说明

- 罗技电池小工具（`LogiBatteryElement`）的数据来自宿主推送；Rust 实现 `logi-battery` 不随本仓库分发，缺失 DLL 不影响编译与其他元素。
- 超级岛目前使用本库自带模板绘制；后续改由 `:superislandui` 渲染接入。
