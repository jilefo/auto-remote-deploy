# SysIns 自动安装工具（PowerShell 版）

Windows 桌面自动化安装脚本，全程无人值守，自动完成两款远程控制软件的安装、登录、信息提取与邮件上报。

> ⚠️ 本工具仅用于合法的自动化运维场景，请遵守相关软件的使用条款。

## 功能

| 软件 | 自动化内容 |
|------|-----------|
| 网易UU远程 | 安装 → 跳过引导 → 短信登录（IMAP 取验证码）→ 复制识别码+验证码 → 邮件上报 |
| AweSun 向日葵 | 安装 → 关广告弹窗 → 短信登录 → 设置访问密码 → 复制识别码+验证码 → 邮件上报 |

- 启动时检测火绒安全软件，检出即报警并以退出码 2 退出
- 安装前自动卸载旧版（杀残留进程 + 目录清理）
- ffmpeg 按窗口区域分阶段录屏
- 广告/遮挡弹窗自动识别关闭
- 全程不依赖硬编码坐标：EnumChildWindows 递归枚举子控件 + 窗口内比例坐标 + 像素扫描
- 剪贴板读写带重试机制
- IMAP/SMTP 经 MailKit/MimeKit 实现

## 环境要求

- Windows 10/11
- PowerShell 5.1+
- .NET Framework 4.5+
- 以**管理员身份**运行

## 快速开始

### 1. 克隆仓库

```bash
git clone <your-repo-url>
cd GitStore
```

### 2. 下载依赖

```powershell
powershell -ExecutionPolicy Bypass -File scripts\download-deps.ps1
```

该脚本会下载：
- `Tools\ffmpeg.exe` — 录屏工具
- `Tools\mailkit\*.dll` — 邮件收发库

### 3. 准备安装包

将以下安装包放入 `Soft\` 目录：
- `uuyc_4.42.0.exe`（网易UU远程）
- `AweSun_16.6.0.32198.exe`（向日葵）

> 安装包需自行从官网下载，仓库不提供。

### 4. 配置

```powershell
copy config.example.ps1 config.ps1
```

编辑 `config.ps1`，填写手机号、IMAP 邮箱配置（接收验证码）、SMTP 邮箱配置（发送结果邮件）。

### 5. 运行

```powershell
# 全流程：卸载 → UU → AweSun → 发邮件
powershell -ExecutionPolicy Bypass -File main.ps1

# 仅装 UU 远程
powershell -ExecutionPolicy Bypass -File main.ps1 -Mode uu

# 仅装 AweSun
powershell -ExecutionPolicy Bypass -File main.ps1 -Mode awesun

# 仅卸载两款软件
powershell -ExecutionPolicy Bypass -File main.ps1 -Mode uninstall

# 仅加载语法检查不执行
powershell -ExecutionPolicy Bypass -File main.ps1 -LoadOnly
```

## 目录结构

```
GitStore/
├── main.ps1              # 主脚本
├── config.example.ps1    # 配置模板（复制为 config.ps1）
├── config.ps1            # 实际配置（⚠️ 不提交到 Git）
├── scripts/
│   └── download-deps.ps1 # 依赖下载脚本
├── Soft/                 # 安装包（自行放置，不提交）
├── Tools/
│   ├── ffmpeg.exe        # 录屏工具
│   └── mailkit/          # 邮件库 DLL
├── Logs/                 # 日志/截图/录屏（运行时生成）
├── README.md
├── LICENSE
└── .gitignore
```

## 录屏说明

按阶段生成独立文件：`AutoInstallRec-{时间戳}-{阶段}_PowerShell.mkv`

阶段：`boot`（全屏）→ `uu-install` / `uu-main` → `awesun-install` / `awesun-main`

抽帧命令：
```bash
ffmpeg -i 录像.mkv -vf "fps=1/10" frames\f_%03d.png
```

## 安全说明

- `config.ps1` 含密码等敏感信息，已被 `.gitignore` 排除，**切勿提交**
- 建议使用邮箱授权码而非登录密码
- 运行前请确认当前系统未安装目标软件，避免重复操作

## License

MIT
