# SysIns 自动安装工具（PowerShell 版）

Windows 桌面自动化安装脚本，全程无人值守，自动完成两款软件的安装、登录、信息提取与邮件上报；火绒按要求不安装（仅启动时检测报警）。

## 功能

| 软件 | 安装包 | 自动化内容 |
|------|--------|-----------|
| 网易UU远程 | `uuyc_4.42.0.exe` | 安装 → 跳过引导 → 短信登录（IMAP 取验证码）→ 复制识别码+验证码 → 邮件上报 |
| AweSun 向日葵 | `AweSun_16.6.0.32198.exe` | 安装 → 关广告弹窗 → 短信登录 → 设置访问密码 → 复制识别码+验证码 → 邮件上报 |

- 启动时检测火绒安全软件（4 进程 tasklist + HipsDaemon/sysdiag 服务 RUNNING 状态），检出即中文报警并以退出码 2 退出
- 安装前自动卸载旧版（杀残留进程 + 目录清理 + UU 已运行先杀避免小窗死锁）
- ffmpeg 按窗口区域分阶段录屏（安装窗口与主窗口尺寸分别检测）
- 广告/遮挡弹窗自动识别关闭（OrayUI 弹窗、卸载确认框、目录非空"继续安装"框）
- 全程不依赖硬编码坐标：EnumChildWindows 递归枚举子控件找按钮/输入框 + 窗口内比例坐标 + 像素扫描（蓝色登录态）
- 剪贴板读写带 8×300ms 重试（防其他进程瞬时占用剪贴板导致崩溃）
- IMAP/SMTP 经 Add-Type 加载 MailKit/MimeKit 实现

## 用法

```
powershell -File main.ps1              全流程：卸载 → UU → AweSun → 发邮件
powershell -File main.ps1 -Mode uu     仅装 UU 远程
powershell -File main.ps1 -Mode awesun 仅装 AweSun
powershell -File main.ps1 -Mode uninstall 仅卸载两款软件
-LoadOnly                              仅加载语法检查不执行
```

需以管理员身份运行。

## 目录约定

- 安装包：`D:\SysIns\Soft\`（自动探测：脚本目录 → 上级目录 → `D:\SysIns`）
- 日志/截图/录屏：`D:\SysIns\Logs\`
- 依赖：`D:\SysIns\Tools\ffmpeg.exe`、`D:\SysIns\Tools\mailkit\*.dll`

## 配置

`main.ps1` 顶部 `$CFG` 哈希表：手机号、IMAP（zoho）、SMTP（QQ 邮箱授权码）、UU 安装目录。

验证码流程：点击"发送验证码"后由 FETCH_CODE 以 UID 基线轮询 IMAP 新邮件（MailKit），正则提取 6 位数字；等待窗口内含 45 秒基础延迟（满足 30 秒以上要求）。

## 录屏

按阶段生成独立文件：`AutoInstallRec-{时间戳}-{阶段}_PowerShell.mkv`

阶段：`boot`（全屏）→ `uu-install` / `uu-main` → `awesun-install` / `awesun-main`

窗口区域经 `GetWindowRect` 检测，编码统一 `libx264 -preset ultrafast -crf 26`（hevc_qsv 间歇性中途崩溃弃用）；停止时向 stdin 写入 `q` 优雅收尾（强杀不落盘），超时 8 秒才 `taskkill` 兜底。

抽帧：`ffmpeg -i 录像.mkv -vf "fps=1/10" frames\f_%03d.png`
