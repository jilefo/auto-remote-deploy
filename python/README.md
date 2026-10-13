# SysIns 自动安装工具（Python 版）

Windows 桌面自动化安装脚本，全程无人值守，自动完成两款软件的安装、登录、信息提取与邮件上报；火绒按要求不安装（仅启动时检测报警）。

## 功能

| 软件 | 安装包 | 自动化内容 |
|------|--------|-----------|
| 网易UU远程 | `uuyc_4.42.0.exe` | 安装 → 跳过引导 → 短信登录（IMAP 取验证码）→ 复制识别码+验证码 → 邮件上报 |
| AweSun 向日葵 | `AweSun_16.6.0.32198.exe` | 安装 → 关广告弹窗 → 短信登录 → 设置访问密码 → 复制识别码+验证码 → 邮件上报 |

- 启动时检测火绒安全软件（4 进程 tasklist + HipsDaemon/sysdiag 服务 RUNNING 状态），检出即中文报警并以退出码 2 退出
- 安装前自动卸载旧版（winreg 扫描 + 卸载向导按钮自动点击 + 服务/目录清理；机器上已有火绒会一并卸载，卸载后重启续跑但不再安装火绒）
- ffmpeg 按窗口区域分阶段录屏（安装窗口与主窗口尺寸分别检测）
- 广告/遮挡弹窗自动识别关闭（蓝色广告、免密远控弹窗、推广弹窗）
- 全程不依赖硬编码坐标：窗口内按比例坐标 + 像素扫描（蓝色登录态/白色弹窗/粉色登录态）

## 依赖

```
pip install pyautogui pyscreeze pillow pywin32
```

运行时还需：`D:\SysIns\Tools\ffmpeg.exe`

## 用法

```
python main.py              全流程：卸载 → UU → AweSun → 发邮件
python main.py -uninstall   仅卸载两款软件（含已有火绒卸载）
python main.py -uu          仅装 UU 远程
python main.py -awesun      仅装 AweSun
python main.py -sysdiag     卸载旧版（含火绒），卸载火绒后重启续跑（不安装火绒）
python main.py -resume      续跑模式（重启后由 schtasks 自动调用，勿手动）
-nomail                     不发邮件
```

需以管理员身份运行。

## 重启续跑机制

卸载阶段若发现机器上已装火绒，卸载后必须重启才能重装其他软件：

1. 写入状态文件 `D:\SysIns\Logs\resume_Python.txt`（内容 `phase2` 或 `sysdiag2`）
2. 计划任务 `SysInsAutoInstall_Python`（登录触发）写入 `"python.exe" "main.py" -resume` 自启
3. `shutdown /r /t 10` 重启
4. 重启登录后程序自动运行，读取状态文件继续未完成流程（只装 UU/AweSun，不装火绒），完成后删除状态文件

## 目录约定

- 安装包：`D:\SysIns\Soft\`
- 日志/截图/录屏/续跑状态：`D:\SysIns\Logs\`
- 依赖：`D:\SysIns\Tools\ffmpeg.exe`

## 配置

`main.py` 顶部 `CFG` 字典：手机号、IMAP（zoho）、SMTP（QQ 邮箱授权码）、AweSun 访问密码、UU 安装目录。

验证码流程：点击"发送验证码"后先等 45 秒（满足 30 秒以上要求），再以 UID 基线轮询 IMAP 新邮件，正则提取 6 位数字，最长等 240 秒。

## 录屏

按阶段生成独立文件：`AutoInstallRec-{时间戳}-{阶段}_Python.mkv`

阶段：`boot`（全屏）→ `uu-install` / `uu-main` → `awesun-install` / `awesun-main`

窗口区域经 `GetWindowRect` 检测，编码统一 `libx264 -preset ultrafast -crf 26`（hevc_qsv 间歇性中途崩溃弃用）；停止时向 stdin 写入 `q` 优雅收尾（强杀不落盘），超时 8 秒才 `taskkill` 兜底。

抽帧：`ffmpeg -i 录像.mkv -vf "fps=1/10" frames\f_%03d.png`
