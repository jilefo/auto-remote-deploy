# SysIns 自动安装工具

Windows 桌面自动化安装脚本，全程无人值守，自动完成两款远程控制软件的安装、登录、信息提取与邮件上报。

> ⚠️ 本工具仅用于合法的自动化运维场景，请遵守相关软件的使用条款。

## 支持的语言版本

| 语言 | 目录 | 运行方式 |
|------|------|---------|
| PowerShell | [powershell/](powershell/) | `powershell -File main.ps1` |
| Python | [python/](python/) | `python main.py` |
| C# | [csharp/](csharp/) | 编译后 `AutoInstall.exe` |
| Aardio | [aardio/](aardio/) | aardio IDE 运行或发布 exe |

四个版本功能一致，实现方式各有特点。

## 功能

| 软件 | 自动化内容 |
|------|-----------|
| 网易UU远程 | 安装 → 跳过引导 → 短信登录（IMAP 取验证码）→ 复制识别码+验证码 → 邮件上报 |
| AweSun 向日葵 | 安装 → 关广告弹窗 → 短信登录 → 设置访问密码 → 复制识别码+验证码 → 邮件上报 |

- 启动时检测火绒安全软件，检出即报警并以退出码 2 退出
- 安装前自动卸载旧版（杀残留进程 + 目录清理）
- ffmpeg 按窗口区域分阶段录屏
- 广告/遮挡弹窗自动识别关闭
- 全程不依赖硬编码坐标
- IMAP/SMTP 经 MailKit/MimeKit 实现
- 支持重启续跑（卸载火绒后需重启）

## 环境要求

- Windows 10/11
- 以**管理员身份**运行
- 各语言额外要求见对应目录的 README

## 快速开始

1. 克隆仓库
2. 运行 `scripts/download-deps.ps1` 下载 ffmpeg 和 MailKit DLL
3. 将安装包放入 `Soft/` 目录
4. 复制对应语言的 `config.example.*` 为 `config.*`，填写配置
5. 运行对应语言的脚本

详见各语言目录下的 README。

## 目录结构

```
auto-remote-deploy/
├── powershell/          # PowerShell 版
│   ├── main.ps1
│   ├── config.example.ps1
│   └── README.md
├── python/              # Python 版
│   ├── main.py
│   ├── install_huorong.py
│   ├── config.example.py
│   └── README.md
├── csharp/              # C# 版
│   ├── AutoInstall.cs
│   ├── InstallHuorong.cs
│   ├── Config.example.cs
│   ├── compile.bat
│   └── README.md
├── aardio/              # Aardio 版
│   ├── main.aardio
│   ├── InstallHuorong.aardio
│   ├── config.example.aardio
│   └── README.md
├── scripts/
│   └── download-deps.ps1
├── Soft/                # 安装包（自行放置，不提交）
├── Tools/               # ffmpeg + mailkit（运行脚本下载）
├── Logs/                # 日志（运行时生成）
├── README.md
├── LICENSE
└── .gitignore
```

## 安全说明

- 各语言的 `config.*` 文件含密码等敏感信息，已被 `.gitignore` 排除，**切勿提交**
- 建议使用邮箱授权码而非登录密码

## License

MIT
