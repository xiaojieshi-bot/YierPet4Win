# YierPet Win — 一二桌宠（Windows）

一只住在你 Windows 桌面上的「一二」宝：散步、跳跃、挥手，喝水与久坐提醒，深夜关怀，摸鱼盯梢，CPU 红温，甩出去还会生气——与 macOS 版 **YierPet** 同款能力，**本仓库自带全部图片素材，可独立克隆与发布**。

纯 **C# + WPF** 原生实现，构建时仅拉取 NuGet（WebP 解码），运行不需额外权限。

## 功能特性

| 能力 | 说明 |
| --- | --- |
| 桌面悬浮 | 无边框透明窗口，置顶，无任务栏主按钮 |
| 四种形象 | 经典一二 / 活力一二 / 活力布布 / 一二布布合体 |
| 9 种动画 | 待机 / 跑 / 挥手 / 跳跃 / 沮丧 / 等待 / 工作中 / 审阅 |
| 自主行为 | 随机散步、跳跃、挥手；贴边掉头 |
| 气泡说话 | 头顶圆角气泡，语料随机 |
| 健康提醒 | 久坐 / 喝水 / 深夜 / 摸鱼（可单独开关） |
| 系统哨兵 | CPU / 内存 / 电量 / 磁盘（可单独开关） |
| 陪伴模式 | 早晨 / 午饭 / 下午茶 / 周五 / 编码久战 |
| 抛掷物理 | 拖甩、重力、反弹；摔狠了生气 |
| 设置 | `%AppData%\YierPetWin\settings.json` |

## 素材目录（仓库内）

```
YierPetWin/
├── Assets/
│   ├── spritesheet.webp      # 经典版 8×9 精灵图集
│   └── Packs/                # yier / bubu / duo 表情包帧序列 + meta.json
├── YierPet/                  # 源代码
├── build.ps1
├── docs/tutorial/index.html  # 安装与使用图文教程
└── README.md
```

换形象：替换 `Assets/spritesheet.webp` 或 `Assets/Packs` 下对应包；图集契约为 8 列 × 9 行（每格 192×208），与 mac 版一致。

## 环境要求

- Windows 10 1809+ 或 Windows 11  
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（构建）  
- 运行需 [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0)

📖 **图文安装教程**（可发给别人或录视频对照）：[`docs/tutorial/index.html`](docs/tutorial/index.html)（双击用浏览器打开即可）。

## 安装运行

```powershell
git clone https://gh-proxy.com/https://github.com/xiaojieshi-bot/YierPet4Win.git
cd YierPet4Win
.\build.ps1
.\build\YierPet.exe
```

仓库：[github.com/xiaojieshi-bot/YierPet4Win](https://github.com/xiaojieshi-bot/YierPet4Win)

> **开机自启**：为 `build\YierPet.exe` 创建快捷方式，放入「启动」文件夹（`Win+R` → `shell:startup`）。

## 使用说明

| 操作 | 效果 |
| --- | --- |
| 拖拽 | 跑步朝向拖动方向 |
| 快速甩出 | 抛掷物理 |
| 单击 | 挥手 / 表情包开心 |
| 右键 | 形象 / 动作 / 提醒 / 退出 |
| Alt + 右键 | 测试提醒菜单 |

## 项目结构

```
YierPetWin/YierPet/
├── App.xaml
├── PetController.cs
├── SpeechBubble.cs
├── SpriteSheet.cs
├── PetState.cs
├── ReminderCenter.cs
├── ActivityMonitor.cs
├── SystemMonitor.cs
└── BitmapUtil.cs
```

## 自定义

- **台词 / 阈值**：`ReminderCenter.cs`  
- **手感**：`PetController.cs` 顶部常量  
- **摸鱼名单**：`ActivityMonitor.SlackProcessNames`（进程名，不含 `.exe`）

## 与 macOS 版差异（实现层）

| 项目 | 说明 |
| --- | --- |
| 依赖 | .NET 8 + ImageSharp（WebP） |
| 内存压力 | 内存负载 ≥92% 视为 critical |
| 摸鱼检测 | Windows 进程名 |

## 许可证

[MIT](LICENSE)

## 发布 Release（维护者）

Windows 项目必须在 **Windows** 上构建（Mac 无法编译 WPF）。推荐两种方式：

### 方式 A：打 Git 标签，自动发布（推荐）

在任意机器上给仓库打版本标签并推送，GitHub Actions 会在 Windows 虚拟机里构建，并把 zip 挂到 Release：

```bash
cd YierPetWin
git tag v1.0.0
git push origin v1.0.0
```

几分钟后打开 [Releases](https://github.com/xiaojieshi-bot/YierPet4Win/releases) 即可看到 **YierPet4Win-win-x64.zip**（自包含版，用户解压即用）。

更新版本时改标签号，例如 `v1.0.1`，再 push 一次 tag。

### 方式 B：本机手动打包上传

在 Windows 电脑上：

```powershell
git clone https://gh-proxy.com/https://github.com/xiaojieshi-bot/YierPet4Win.git
cd YierPet4Win
.\build.ps1 -Package -SelfContained
```

产物在 `dist\YierPet4Win-win-x64.zip`。然后：

1. 打开 GitHub 仓库 → **Releases** → **Draft a new release**
2. 新建标签（如 `v1.0.0`），填写说明
3. 把 `dist\YierPet4Win-win-x64.zip` 拖进附件区 → **Publish release**

或用 GitHub CLI：

```powershell
gh release create v1.0.0 dist\YierPet4Win-win-x64.zip --title "v1.0.0" --notes "首个 Windows 发布包"
```

### 给用户的使用说明

下载 Release 里的 zip → 解压到任意文件夹 → 双击 `YierPet.exe`。自包含包**不需要**再装 .NET 运行时。
