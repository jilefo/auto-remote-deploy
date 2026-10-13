# AutoInstall_PowerShell - PowerShell版自动安装脚本
# 支持 uuyc / AweSun 自动安装、登录、复制识别码、发送邮件（按要求不含火绒）
param(
    [string]$Mode = "all",
    [switch]$LoadOnly
)

$ErrorActionPreference = "Stop"

# 从外部配置文件加载敏感配置（config.ps1 不纳入版本控制）
$ConfigFile = Join-Path $PSScriptRoot "config.ps1"
if (-not (Test-Path $ConfigFile)) {
    Write-Host "错误：配置文件 config.ps1 不存在。"
    Write-Host "请复制 config.example.ps1 为 config.ps1 并填写实际配置后再运行。"
    exit 1
}
. $ConfigFile

$CFG = @{
    Phone     = $Config.Phone
    UuycDir   = $Config.UuycDir
    ImapHost  = $Config.ImapHost
    ImapPort  = $Config.ImapPort
    ImapUser  = $Config.ImapUser
    ImapPwd   = $Config.ImapPwd
    SubjectKw = $Config.SubjectKw
    Record    = $Config.Record
    SmtpHost  = $Config.SmtpHost
    SmtpPort  = $Config.SmtpPort
    SmtpUser  = $Config.SmtpUser
    SmtpPwd   = $Config.SmtpPwd
    MailTo    = $Config.MailTo
    Lang      = "PowerShell"
}

# 根目录探测：脚本目录 → 当前目录 → D:\SysIns
$RootDir = $PSScriptRoot
if (-not (Test-Path "$RootDir\Soft\uuyc_4.42.0.exe")) {
    $RootDir = (Get-Location).Path
}
if (-not (Test-Path "$RootDir\Soft\uuyc_4.42.0.exe")) {
    $RootDir = "D:\SysIns"
}
$SoftDir  = Join-Path $RootDir "Soft"
$ToolsDir = Join-Path $RootDir "Tools"
$LogDir   = Join-Path $RootDir "Logs"
if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir | Out-Null }

$Ffmpeg   = Join-Path $ToolsDir "ffmpeg.exe"
$UuycExe  = Join-Path $SoftDir "uuyc_4.42.0.exe"
$AwsunExe = Join-Path $SoftDir "AweSun_16.6.0.32198.exe"
$Ts       = Get-Date -Format "yyyyMMddHHmmss"
$LogFile  = Join-Path $LogDir "AutoInstall-$Ts.log"
$Script:RecProc = $null

function LOG {
    $Parts = @($args)
    $s = (Get-Date -Format "HH:mm:ss") + "  " + (($Parts | ForEach-Object { [string]$_ }) -join " ")
    Write-Host $s
    Add-Content -Path $LogFile -Value $s -Encoding UTF8
}

function SHOT {
    param([string]$Name)
    try {
        Add-Type -AssemblyName System.Drawing
        $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $bmp = New-Object System.Drawing.Bitmap($b.Width, $b.Height)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
        $bmp.Save((Join-Path $LogDir ($Name + ".png")), [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose()
    } catch { }
}

Add-Type -AssemblyName System.Windows.Forms

Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public class WinAPI {
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lp);
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32.dll")] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr wp, StringBuilder lp);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("gdi32.dll")] public static extern int GetPixel(IntPtr dc, int x, int y);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; [FieldOffset(8)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT {
        public ushort vk; public ushort scan;
        public uint flags; public uint time;
        public IntPtr extra;
    }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT {
        public int dx, dy; public uint data, flags, time; public IntPtr extra;
    }
    [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);

    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lp);

    public static List<object[]> Windows = new List<object[]>();
    public static void AddWin(IntPtr h) {
        var t = new StringBuilder(512); GetWindowText(h, t, 512);
        var c = new StringBuilder(256); GetClassName(h, c, 256);
        RECT r; GetWindowRect(h, out r);
        Windows.Add(new object[] { h, t.ToString(), c.ToString(), r.L, r.T, r.R - r.L, r.B - r.T });
    }
    public static bool EnumCb(IntPtr h, IntPtr lp) {
        if (!IsWindowVisible(h)) return true;
        AddWin(h);
        return true;
    }
    public static bool EnumCbAll(IntPtr h, IntPtr lp) {
        AddWin(h);
        return true;
    }
    public static object[][] GetAllWindows() {
        Windows.Clear();
        EnumWindows(EnumCb, IntPtr.Zero);
        return Windows.ToArray();
    }
    public static object[][] GetChildWindows(IntPtr parent) {
        Windows.Clear();
        EnumChildWindows(parent, EnumCbAll, IntPtr.Zero);
        return Windows.ToArray();
    }
    public static bool ClickAt(int x, int y) {
        int sx = (x * 65535) / (GetSystemMetrics(0) - 1);
        int sy = (y * 65535) / (GetSystemMetrics(1) - 1);
        var mi = new MOUSEINPUT(); mi.dx = sx; mi.dy = sy;
        mi.flags = 0x8000 | 0x0001;
        var inp = new INPUT(); inp.type = 0; inp.mi = mi;
        uint r1 = SendInput(1, new INPUT[]{ inp }, Marshal.SizeOf(typeof(INPUT)));
        System.Threading.Thread.Sleep(120);
        mi.flags = 0x8000 | 0x0002; inp.mi = mi;
        uint r2 = SendInput(1, new INPUT[]{ inp }, Marshal.SizeOf(typeof(INPUT)));
        System.Threading.Thread.Sleep(80);
        mi.flags = 0x8000 | 0x0004; inp.mi = mi;
        uint r3 = SendInput(1, new INPUT[]{ inp }, Marshal.SizeOf(typeof(INPUT)));
        return r1 > 0 && r2 > 0 && r3 > 0;
    }
    public static bool TypeText(string s) {
        foreach (char ch in s) {
            var ki = new KEYBDINPUT(); ki.scan = ch; ki.flags = 4;
            var inp = new INPUT(); inp.type = 1; inp.ki = ki;
            SendInput(1, new INPUT[]{ inp }, Marshal.SizeOf(typeof(INPUT)));
            System.Threading.Thread.Sleep(15);
            ki.flags = 4 | 2; inp.ki = ki;
            SendInput(1, new INPUT[]{ inp }, Marshal.SizeOf(typeof(INPUT)));
            System.Threading.Thread.Sleep(15);
        }
        return true;
    }
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int idx);
}
"@

function Get-AllWindows { return [WinAPI]::GetAllWindows() }

function Get-WindowRect2 {
    param([IntPtr]$H)
    $r = New-Object WinAPI+RECT
    [WinAPI]::GetWindowRect($H, [ref]$r) | Out-Null
    return @{ X = $r.L; Y = $r.T; W = ($r.R - $r.L); H = ($r.B - $r.T) }
}

function Get-Pixel {
    param([IntPtr]$H, [double]$Rx, [double]$Ry)
    $r = Get-WindowRect2 $H
    $x = [int]($r.X + $r.W * $Rx); $y = [int]($r.Y + $r.H * $Ry)
    $dc = [WinAPI]::GetDC([IntPtr]::Zero)
    $c = [WinAPI]::GetPixel($dc, $x, $y)
    [WinAPI]::ReleaseDC([IntPtr]::Zero, $dc) | Out-Null
    return @{ R = ($c -band 0xFF); G = (($c -shr 8) -band 0xFF); B = (($c -shr 16) -band 0xFF) }
}

function Get-Brightness {
    param($Px)
    return ($Px.R + $Px.G + $Px.B) / 3.0
}

function FORCE_FG {
    param([IntPtr]$H)
    if ([WinAPI]::IsIconic($H)) { [WinAPI]::ShowWindow($H, 9) | Out-Null; Start-Sleep -Milliseconds 200 }
    $tid = 0; [WinAPI]::GetWindowThreadProcessId($H, [ref]$tid) | Out-Null
    $self = [WinAPI]::GetCurrentThreadId()
    [WinAPI]::AttachThreadInput($self, $tid, $true) | Out-Null
    [WinAPI]::SetWindowPos($H, [IntPtr](-1), 0, 0, 0, 0, 0x43) | Out-Null
    [WinAPI]::SetForegroundWindow($H) | Out-Null
    Start-Sleep -Milliseconds 400
    [WinAPI]::AttachThreadInput($self, $tid, $false) | Out-Null
    [WinAPI]::SetWindowPos($H, [IntPtr](-2), 0, 0, 0, 0, 0x43) | Out-Null
    return ([WinAPI]::GetForegroundWindow() -eq $H)
}

function Click-Ratio {
    param([IntPtr]$H, [double]$Rx, [double]$Ry, [int]$Dx = 0, [int]$Dy = 0)
    $r = Get-WindowRect2 $H
    $x = [int]($r.X + $r.W * $Rx) + $Dx
    $y = [int]($r.Y + $r.H * $Ry) + $Dy
    [WinAPI]::ClickAt($x, $y) | Out-Null
}

function Send-Keys {
    param([string]$Text)
    [WinAPI]::TypeText($Text) | Out-Null
}

function Set-ClipboardText {
    param([string]$Text)
    for ($i = 0; $i -lt 8; $i++) {
        try { [System.Windows.Forms.Clipboard]::SetText($Text); return $true } catch { Start-Sleep -Milliseconds 300 }
    }
    LOG "警告：剪贴板写入失败（被其他进程占用）"
    return $false
}

function Get-ClipboardText {
    for ($i = 0; $i -lt 8; $i++) {
        try { return [System.Windows.Forms.Clipboard]::GetText() } catch { Start-Sleep -Milliseconds 300 }
    }
    LOG "警告：剪贴板读取失败（被其他进程占用）"
    return ""
}

function KILL_STALE {
    $names = @("GameViewer.exe","SunloginClient.exe","AweSun.exe","usysdiag.exe",
        "uuyc_4.42.0.exe","AweSun_16.6.0.32198.exe")
    foreach ($n in $names) { Start-Process -FilePath "taskkill" -ArgumentList "/f /im $n" -WindowStyle Hidden -Wait -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 2
}

function START_REC {
    param([IntPtr]$H, [string]$Tag)
    if (-not $CFG.Record) { return }
    if (-not (Test-Path $Ffmpeg)) { LOG "ffmpeg 不存在，跳过录屏"; return }
    $geo = @()
    if ($H -ne [IntPtr]::Zero) {
        $r = Get-WindowRect2 $H
        if ($r.W -ge 100 -and $r.H -ge 100) {
            $w = $r.W - ($r.W % 2); $h = $r.H - ($r.H % 2)
            $geo = @("-offset_x", "$($r.X)", "-offset_y", "$($r.Y)", "-video_size", "${w}x${h}")
            LOG "录屏区域: $($r.X),$($r.Y) ${w}x${h}"
        }
    }
    $path = Join-Path $LogDir "AutoInstallRec-$Ts-$Tag`_$($CFG.Lang).mkv"
    $base = @("-y","-threads","2","-thread_queue_size","64","-loglevel","error",
        "-f","gdigrab","-framerate","5","-draw_mouse","1") + $geo + @("-i","desktop")
    $all = ($base + @("-c:v","libx264","-preset","ultrafast","-crf","26") + @("-pix_fmt","yuv420p","-an", "`"$path`"")) -join " "
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Ffmpeg
    $psi.Arguments = $all
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    Start-Sleep -Milliseconds 1500
    if (-not $p.HasExited) {
        LOG "录屏开始 ->" $path
        $Script:RecProc = $p
        return
    }
    LOG "录屏启动失败"
}

function STOP_REC {
    if ($Script:RecProc) {
        if (-not $Script:RecProc.HasExited) {
            try { $Script:RecProc.StandardInput.Write("q") } catch {}
        }
        if (-not $Script:RecProc.WaitForExit(8000)) {
            Start-Process -FilePath "taskkill" -ArgumentList "/f /im ffmpeg.exe" -WindowStyle Hidden -Wait
        }
        Start-Sleep -Milliseconds 500
        $Script:RecProc = $null
        LOG "录屏结束"
    }
}

function SWITCH_REC {
    param([IntPtr]$H, [string]$Tag)
    if (-not $CFG.Record) { return }
    STOP_REC
    START_REC $H $Tag
}

function CHECK_HUORONG {
    $procs = @("hipstray.exe","hipsmain.exe","hipsdaemon.exe","hrconfig.exe")
    $out = (& tasklist.exe 2>$null | Out-String).ToLower()
    foreach ($p in $procs) { if ($out.Contains($p)) { return $p } }
    foreach ($svc in @("HipsDaemon","sysdiag")) {
        $so = (& sc.exe query $svc 2>$null | Out-String).ToLower()
        if ($LASTEXITCODE -eq 0 -and $so.Contains("running")) { return $svc }
    }
    return $null
}

function Find-WindowBy {
    param([scriptblock]$Match)
    foreach ($w in (Get-AllWindows)) {
        $o = @{ Hwnd = $w[0]; Title = $w[1]; Class = $w[2]; X = $w[3]; Y = $w[4]; W = $w[5]; H = $w[6] }
        if (& $Match $o) { return $o }
    }
    return $null
}

function Find-ChildButton {
    param([IntPtr]$Parent, [string]$Text)
    foreach ($c in [WinAPI]::GetChildWindows($Parent)) {
        if ($c[2] -eq "Button" -and ([string]$c[1]).Contains($Text)) { return $c[0] }
    }
    return [IntPtr]::Zero
}

function Find-ChildEdit {
    param([IntPtr]$Parent)
    foreach ($c in [WinAPI]::GetChildWindows($Parent)) {
        if (([string]$c[2]).Contains("Edit")) { return $c[0] }
    }
    return [IntPtr]::Zero
}

function BM_CLICK { param([IntPtr]$H) [WinAPI]::PostMessage($H, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
function WM_SETTEXT { param([IntPtr]$H, [string]$Text)
    $sb = New-Object System.Text.StringBuilder($Text)
    [WinAPI]::SendMessage($H, 0x000C, [IntPtr]::Zero, $sb) | Out-Null
}

function INSTALL_UU {
    KILL_STALE
    LOG "启动 UU 安装包:" $UuycExe
    Start-Process -FilePath $UuycExe | Out-Null
    $hwnd = $null
    $tk = [DateTime]::Now
    while (((Get-Date) - $tk).TotalSeconds -lt 60) {
        $hwnd = Find-WindowBy { param($w) $w.Title.Contains("网易UU远程 安装") }
        if ($hwnd) { break }
        Start-Sleep -Seconds 1
    }
    if (-not $hwnd) { LOG "错误：找不到 UU 安装窗口"; return $null }
    LOG "安装窗口出现"
    $hwndI = $hwnd.Hwnd
    SWITCH_REC $hwndI "uu-install"
    FORCE_FG $hwndI | Out-Null
    $btnNext = Find-ChildButton $hwndI "下一步"
    if ($btnNext -ne [IntPtr]::Zero) { BM_CLICK $btnNext; Start-Sleep -Seconds 2 }
    $ed = Find-ChildEdit $hwndI
    if ($ed -eq [IntPtr]::Zero) { LOG "错误：找不到路径输入框"; return $null }
    WM_SETTEXT $ed $CFG.UuycDir
    Start-Sleep -Milliseconds 500
    $btnInst = Find-ChildButton $hwndI "安装"
    if ($btnInst -eq [IntPtr]::Zero) { LOG "错误：找不到安装按钮"; return $null }
    BM_CLICK $btnInst
    LOG "已点击安装，等待安装完成..."
    $tk = [DateTime]::Now
    while (((Get-Date) - $tk).TotalSeconds -lt 180) {
        $still = Find-WindowBy { param($w) $w.Hwnd -eq $hwndI }
        if (-not $still) { break }
        Start-Sleep -Seconds 2
    }
    $main = $null
    $tk = [DateTime]::Now
    while (((Get-Date) - $tk).TotalSeconds -lt 120) {
        $main = Find-WindowBy { param($w) $w.Title.Contains("网易UU远程") -and $w.Class.Contains("Qt") -and $w.W -gt 300 }
        if ($main) { break }
        Start-Sleep -Seconds 1
    }
    if (-not $main) { LOG "错误：UU 主窗口未出现"; return $null }
    LOG "UU 主窗口已启动"
    SWITCH_REC $main.Hwnd "uu-main"
    FORCE_FG $main.Hwnd | Out-Null
    Start-Sleep -Seconds 4
    return $main.Hwnd
}

function SKIP_GUIDE {
    for ($i = 0; $i -lt 6; $i++) {
        $gv = Find-WindowBy { param($w) $w.Title.Contains("GameViewer") -and $w.Class.Contains("Qt") -and $w.W -gt 100 -and $w.H -gt 100 }
        if (-not $gv) { break }
        Click-Ratio $gv.Hwnd 0.698 0.772
        Start-Sleep -Milliseconds 1500
    }
}

function OPEN_LOGIN {
    param([IntPtr]$Main)
    Click-Ratio $Main 0.629 0.763
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 1
        $gv = Find-WindowBy { param($w) $w.Title.Contains("GameViewer") -and $w.Class.Contains("Qt") -and $w.W -gt 100 -and $w.H -gt 100 }
        if ($gv) { return $gv.Hwnd }
    }
    return [IntPtr]::Zero
}

function IS_UU_LOGINED {
    param([IntPtr]$Main)
    $px = Get-Pixel $Main 0.629 0.763
    $blue = ($px.B -gt 180 -and $px.R -lt 120)
    return (-not $blue)
}

function DO_LOGIN {
    param([IntPtr]$Login, [IntPtr]$Main)
    FORCE_FG $Login | Out-Null
    Click-Ratio $Login 0.5458 0.6628
    Start-Sleep -Milliseconds 800
    Click-Ratio $Login 0.7067 0.3503
    Start-Sleep -Milliseconds 600
    Send-Keys $CFG.Phone
    Start-Sleep -Milliseconds 800
    SHOT "login_phone"
    Click-Ratio $Login 0.8082 0.4194
    Start-Sleep -Milliseconds 2500
    SHOT "login_sendcode"
    LOG "已请求验证码"
    $code = FETCH_CODE
    if (-not $code) { LOG "错误：未获取到验证码"; return $false }
    LOG "收到验证码:" $code
    FORCE_FG $Login | Out-Null
    Click-Ratio $Login 0.6324 0.4243
    Start-Sleep -Milliseconds 500
    Send-Keys $code
    Start-Sleep -Milliseconds 1000
    SHOT "login_codein"
    Click-Ratio $Login 0.7092 0.5822
    for ($i = 0; $i -lt 15; $i++) {
        Start-Sleep -Seconds 1
        $still = Find-WindowBy { param($w) $w.Hwnd -eq $Login }
        if (-not $still) { LOG "登录成功"; SHOT "login_done"; return $true }
    }
    LOG "登录窗口仍在，登录可能失败"
    SHOT "login_fail"
    return $false
}

function COPY_UU_INFO {
    param([IntPtr]$Main)
    try {
        FORCE_FG $Main | Out-Null
        Set-ClipboardText "__SYSINS_SENTINEL__" | Out-Null
        Start-Sleep -Milliseconds 300
        Click-Ratio $Main ((100/920)) ((74/680))
        Start-Sleep -Milliseconds 1500
        Click-Ratio $Main ((100/920)) ((153/680))
        Start-Sleep -Milliseconds 2500
        SHOT "uu_info_page"
        Click-Ratio $Main ((789/920)) ((256/680))
        Start-Sleep -Milliseconds 2000
        SHOT "uu_after_copy"
        $c = Get-ClipboardText
        if ($c -eq "__SYSINS_SENTINEL__") { $c = $null }
        if ($c) { LOG "UU 复制结果:" $c } else { LOG "UU 复制结果: (空)" }
        return $c
    }
    catch { LOG "UU 复制异常:" $_.Exception.Message; return $null }
}

function FIND_AWSUN_MAIN {
    return Find-WindowBy { param($w) $w.Title.Contains("向日葵") -and $w.Class.Contains("FLUTTER") }
}

function DEAL_AWESUN_MODAL {
    foreach ($w in (Get-AllWindows)) {
        $isAtl = $w[2].StartsWith("ATL") -and $w[5] -gt 200 -and $w[5] -lt 700 -and $w[6] -gt 100 -and $w[6] -lt 500
        if ($isAtl) {
            LOG "检测到已安装版本确认框，点击确定"
            $h = $w[0]; FORCE_FG $h | Out-Null
            Click-Ratio $h 0.32 0.77
            Start-Sleep -Seconds 3
            return $true
        }
        $isOray = $w[2].Contains("OrayUI") -and $w[5] -gt 300 -and $w[5] -lt 700 -and $w[6] -gt 120 -and $w[6] -lt 300
        if ($isOray) {
            LOG "检测到 OrayUI 卸载确认框:" $w[1]
            $h = $w[0]; FORCE_FG $h | Out-Null
            Click-Ratio $h 0.29 0.78
            Start-Sleep -Seconds 3
            return $true
        }
    }
    return $false
}

function INSTALL_AWSUN {
    LOG "启动 AweSun 安装包:" $AwsunExe
    Start-Process -FilePath $AwsunExe | Out-Null
    $main = $null; $inst = $null
    $tk = [DateTime]::Now
    while (((Get-Date) - $tk).TotalSeconds -lt 60) {
        $main = FIND_AWSUN_MAIN
        if ($main) { break }
        $inst = Find-WindowBy { param($w) $w.Class.Contains("OrayUI") -and $w.W -gt 300 -and $w.H -gt 200 }
        if ($inst) { break }
        DEAL_AWESUN_MODAL | Out-Null
        Start-Sleep -Seconds 1
    }
    if ($main) {
        LOG "AweSun 已在运行，直接使用主窗口"
        SWITCH_REC $main.Hwnd "awesun-main"
        FORCE_FG $main.Hwnd | Out-Null
        Start-Sleep -Seconds 8
        SHOT "awsun_main"
        return $main.Hwnd
    }
    if (-not $inst) { LOG "错误：找不到 AweSun 安装窗口"; SHOT "awsun_no_installer"; return [IntPtr]::Zero }
    SHOT "awsun_installer"
    SWITCH_REC $inst.Hwnd "awesun-install"
    FORCE_FG $inst.Hwnd | Out-Null
    Click-Ratio $inst.Hwnd 0.500 0.521
    LOG "已点击立即安装，等待安装完成..."
    $main = $null; $entered = $false
    $tk = [DateTime]::Now
    while (((Get-Date) - $tk).TotalSeconds -lt 300) {
        Start-Sleep -Seconds 3
        DEAL_AWESUN_MODAL | Out-Null
        $main = FIND_AWSUN_MAIN
        if ($main) { break }
        $still = Find-WindowBy { param($w) $w.Hwnd -eq $inst.Hwnd }
        if ($still) {
            $px = Get-Pixel $inst.Hwnd 0.12 0.13
            if ($still.W -gt 800 -and $still.H -gt 500 -and ((Get-Brightness $px) -gt 200)) {
                LOG "检测到安装完成欢迎页，点击立即进入"
                if (-not $entered) { SWITCH_REC $inst.Hwnd "awesun-install"; $entered = $true }
                Click-Ratio $inst.Hwnd 0.5 0.57
                Start-Sleep -Seconds 8
            }
        }
        else { break }
    }
    if (-not $main) {
        $tk = [DateTime]::Now
        while (((Get-Date) - $tk).TotalSeconds -lt 30) {
            DEAL_AWESUN_MODAL | Out-Null
            $main = FIND_AWSUN_MAIN
            if ($main) { break }
            Start-Sleep -Seconds 1
        }
    }
    if (-not $main) { LOG "错误：找不到 AweSun 主窗口"; SHOT "awsun_no_main"; return [IntPtr]::Zero }
    LOG "AweSun 主窗口出现"
    SWITCH_REC $main.Hwnd "awesun-main"
    FORCE_FG $main.Hwnd | Out-Null
    Start-Sleep -Seconds 8
    SHOT "awsun_main"
    return $main.Hwnd
}

function LOGIN_AWSUN {
    param([IntPtr]$Main)
    FORCE_FG $Main | Out-Null
    Click-Ratio $Main 0.075 0.047
    Start-Sleep -Milliseconds 2500
    SHOT "awsun_login_page"
    if ((Get-Brightness (Get-Pixel $Main 0.60 0.30)) -lt 180) {
        LOG "AweSun 已登录（检测到账号页特征），跳过登录流程"
        Click-Ratio $Main 0.975 0.045
        Start-Sleep -Seconds 1
        $m2 = FIND_AWSUN_MAIN
        if ($m2) { FORCE_FG $m2.Hwnd | Out-Null } else { FORCE_FG $Main | Out-Null }
        return $true
    }
    Click-Ratio $Main 0.433 0.822
    Start-Sleep -Milliseconds 800
    Click-Ratio $Main 0.578 0.334
    Start-Sleep -Milliseconds 600
    Send-Keys $CFG.Phone
    Start-Sleep -Milliseconds 800
    SHOT "awsun_phone_in"
    Click-Ratio $Main 0.706 0.419
    Start-Sleep -Seconds 3
    SHOT "awsun_sendcode"
    LOG "已请求 AweSun 验证码"
    $code = FETCH_CODE
    if (-not $code) { LOG "错误：AweSun 未获取到验证码"; return $false }
    LOG "收到 AweSun 验证码:" $code
    Click-Ratio $Main 0.549 0.419
    Start-Sleep -Milliseconds 500
    Send-Keys $code
    Start-Sleep -Milliseconds 1000
    SHOT "awsun_code_in"
    Click-Ratio $Main 0.587 0.533
    Start-Sleep -Seconds 10
    SHOT "awsun_after_login"
    Click-Ratio $Main 0.785 0.191
    Start-Sleep -Milliseconds 1500
    Click-Ratio $Main 0.701 0.377
    Start-Sleep -Milliseconds 1500
    SHOT "awsun_popups_closed"
    return $true
}

function COPY_AWSUN_INFO {
    param([IntPtr]$Main)
    try {
    FORCE_FG $Main | Out-Null
    Click-Ratio $Main 0.090 0.155
    Start-Sleep -Seconds 2
    SHOT "awsun_info_page"
    $sentinel = "__SYSINS_SENTINEL__"
    $offs = @(,@(0,0)) + @(,@(0,-14)) + @(,@(0,14)) + @(,@(-16,0)) + @(,@(16,0))
    Set-ClipboardText $sentinel
    Start-Sleep -Milliseconds 300
    $idClip = $null
    foreach ($o in $offs) {
        if ($idClip) { break }
        Click-Ratio $Main 0.522 0.305 $o[0] $o[1]
        Start-Sleep -Milliseconds 1500
        $c = Get-ClipboardText
        if ($c -ne $sentinel -and $c) { $idClip = $c }
    }
    Set-ClipboardText $sentinel
    Start-Sleep -Milliseconds 300
    $codeClip = $null
    Click-Ratio $Main 0.712 0.294
    Start-Sleep -Milliseconds 1500
    $c = Get-ClipboardText
    if ($c -ne $sentinel -and $c) { $codeClip = $c }
    if (-not $codeClip) {
        foreach ($o in $offs) {
            if ($codeClip) { break }
            Click-Ratio $Main 0.755 0.300 $o[0] $o[1]
            Start-Sleep -Milliseconds 1500
            $c = Get-ClipboardText
            if ($c -ne $sentinel -and $c) { $codeClip = $c }
        }
    }
    SHOT "awsun_copied"
    if ($idClip) { LOG "AweSun 识别码剪贴板:" $idClip } else { LOG "AweSun 识别码剪贴板: (空)" }
    if ($codeClip) { LOG "AweSun 验证码剪贴板:" $codeClip } else { LOG "AweSun 验证码剪贴板: (空)" }
    return @($idClip, $codeClip)
    }
    catch { LOG "AweSun 复制异常:" $_.Exception.Message; return @($null, $null) }
}

function LOAD_MAILKIT {
    $mailDir = Join-Path $ToolsDir "mailkit"
    foreach ($d in @("BouncyCastle.Cryptography","System.Buffers","System.Formats.Asn1","System.Memory",
        "System.Numerics.Vectors","System.Runtime.CompilerServices.Unsafe","System.Threading.Tasks.Extensions",
        "System.ValueTuple","MimeKit","MailKit")) {
        $dll = Join-Path $mailDir "$d.dll"
        if (Test-Path $dll) { try { Add-Type -Path $dll -ErrorAction SilentlyContinue | Out-Null } catch { } }
    }
}

function FETCH_CODE {
    LOAD_MAILKIT
    $client = $null
    try {
        $client = New-Object MailKit.Net.Imap.ImapClient
        $client.SslProtocols = [System.Security.Authentication.SslProtocols]::Tls12
        $client.ServerCertificateValidationCallback = { $true }
        $client.Connect($CFG.ImapHost, $CFG.ImapPort, $true)
        $client.Authenticate($CFG.ImapUser, $CFG.ImapPwd)
        $inbox = $client.Inbox
        $inbox.Open([MailKit.FolderAccess]::ReadOnly)
        LOG "MailKit IMAP 已连接，收件箱邮件数:" $inbox.Count
        $query = [MailKit.Search.SearchQuery]::SubjectContains($CFG.SubjectKw)
        $baseResults = $inbox.Search($query)
        $baseId = 0
        foreach ($uid in $baseResults) { if ($uid.Id -gt $baseId) { $baseId = $uid.Id } }
        LOG "等待新验证码邮件，基线 UID:" $baseId
        $code = $null
        for ($attempt = 0; $attempt -lt 30; $attempt++) {
            Start-Sleep -Seconds 5
            try {
                $results = $inbox.Search($query)
                $newId = 0; $newUid = [MailKit.UniqueId]::new([uint32]0)
                foreach ($uid in $results) { if ($uid.Id -gt $newId) { $newId = $uid.Id; $newUid = $uid } }
                if ($newId -gt $baseId) {
                    $msg = $inbox.GetMessage($newUid)
                    $body = $msg.TextBody
                    if (-not $body) { $body = $msg.HtmlBody }
                    LOG "新邮件:" $msg.Subject
                    if ($body) {
                        $m = [regex]::Match($body, "\d{6}")
                        if ($m.Success) { $code = $m.Value }
                    }
                    if (-not $code) { $baseId = $newId }
                }
            }
            catch {
                LOG "IMAP 轮询异常，重连:" $_.Exception.Message
                try { $client.Disconnect($true) } catch { }
                $client = New-Object MailKit.Net.Imap.ImapClient
                $client.SslProtocols = [System.Security.Authentication.SslProtocols]::Tls12
                $client.ServerCertificateValidationCallback = { $true }
                $client.Connect($CFG.ImapHost, $CFG.ImapPort, $true)
                $client.Authenticate($CFG.ImapUser, $CFG.ImapPwd)
                $inbox = $client.Inbox
                $inbox.Open([MailKit.FolderAccess]::ReadOnly)
            }
            if ($code) { break }
        }
    }
    catch {
        LOG "错误：MailKit 连接失败" $_.Exception.Message
        if ($client) { try { $client.Disconnect($true) } catch { } }
        return $null
    }
    if ($client) { try { $client.Disconnect($true) } catch { } }
    if ($code) { LOG "提取到验证码:" $code; return $code }
    LOG "等待验证码超时"
    return $null
}

function SEND_MAIL {
    param([string]$SoftName, [string]$Body, [string]$AttachPath)
    LOG "发送邮件:" $SoftName
    try {
        LOAD_MAILKIT
        $msg = New-Object MimeKit.MimeMessage
        $msg.From.Add((New-Object MimeKit.MailboxAddress("pcbbs", $CFG.SmtpUser)))
        $msg.To.Add((New-Object MimeKit.MailboxAddress("yihuos", $CFG.MailTo)))
        $msg.Subject = "您的软件-$($SoftName)安装成功，现附上截图。"
        $builder = New-Object MimeKit.BodyBuilder
        $builder.TextBody = $Body
        if ($AttachPath -and (Test-Path $AttachPath)) { $builder.Attachments.Add($AttachPath) | Out-Null }
        $msg.Body = $builder.ToMessageBody()
        $smtp = New-Object MailKit.Net.Smtp.SmtpClient
        $smtp.SslProtocols = [System.Security.Authentication.SslProtocols]::Tls12
        $smtp.Timeout = 60000
        $smtp.ServerCertificateValidationCallback = { $true }
        $smtp.Connect($CFG.SmtpHost, $CFG.SmtpPort, $true)
        $smtp.Authenticate($CFG.SmtpUser, $CFG.SmtpPwd)
        $smtp.Send($msg)
        $smtp.Disconnect($true)
        LOG "邮件已发送: 您的软件-$($SoftName)安装成功，现附上截图。"
        return $true
    }
    catch {
        LOG "邮件发送失败:" $_.Exception.Message
        return $false
    }
}

function UNINSTALL_MATCH {
    param([string]$Name, [string]$Kind)
    if (-not $Name) { return $false }
    $low = $Name.ToLower()
    if ($Kind -eq "uu") { return ($low.Contains("uu远程") -or $low.Contains("gameviewer")) }
    if ($Kind -eq "aw") { return ($Name.Contains("向日葵") -or $low.Contains("awesun") -or $low.Contains("sunlogin")) }
    return $false
}

function UNINSTALL_ALL {
    LOG "================ 卸载阶段 ================"
    KILL_STALE
    $regPaths = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
    )
    foreach ($kind in @("uu","aw")) {
        foreach ($rp in $regPaths) {
            if (-not (Test-Path $rp)) { continue }
            foreach ($sub in (Get-ChildItem $rp -ErrorAction SilentlyContinue)) {
                $dn = (Get-ItemProperty $sub.PSPath -ErrorAction SilentlyContinue).DisplayName
                if (-not (UNINSTALL_MATCH $dn $kind)) { continue }
                LOG "发现已安装:" $dn
                $ip = Get-ItemProperty $sub.PSPath
                $cmd = $ip.QuietUninstallString
                if (-not $cmd) { $cmd = $ip.UninstallString }
                if (-not $cmd) { continue }
                if ($cmd -match "unins" -and ($cmd -notmatch "/")) { $cmd = $cmd + " /S" }
                LOG "执行卸载命令:" $cmd
                Start-Process -FilePath "cmd.exe" -ArgumentList "/c $cmd" -WindowStyle Hidden
                Start-Sleep -Seconds 90
                $tk = [DateTime]::Now
                while (((Get-Date) - $tk).TotalSeconds -lt 60) {
                    $acted = $false
                    foreach ($w in (Get-AllWindows)) {
                        if ($w[2] -eq "#32770" -or $w[2].Contains("OrayUI") -or $w[1].Contains("卸载")) {
                            $btn = Find-ChildButton $w[0] "继续卸载"
                            if ($btn -eq [IntPtr]::Zero) { $btn = Find-ChildButton $w[0] "残忍卸载" }
                            if ($btn -eq [IntPtr]::Zero) { $btn = Find-ChildButton $w[0] "确定" }
                            if ($btn -ne [IntPtr]::Zero) {
                                LOG "卸载向导点击"
                                FORCE_FG $w[0] | Out-Null
                                BM_CLICK $btn
                                $acted = $true; Start-Sleep -Seconds 2; break
                            }
                        }
                    }
                    if (-not $acted) { Start-Sleep -Seconds 2 }
                }
            }
        }
    }
    foreach ($svc in @("SunloginService","HipsDaemon","HipsKeeper","HipsService")) {
        Start-Process -FilePath "sc.exe" -ArgumentList "stop $svc" -WindowStyle Hidden
        Start-Process -FilePath "sc.exe" -ArgumentList "delete $svc" -WindowStyle Hidden
    }
    Start-Sleep -Seconds 3
    KILL_STALE
    foreach ($dir in @($CFG.UuycDir, "C:\Program Files\Oray")) {
        if (Test-Path $dir) {
            Start-Process -FilePath "cmd.exe" -ArgumentList "/c rmdir /s /q `"$dir`"" -WindowStyle Hidden -Wait
            LOG "已删除目录:" $dir
        }
    }
    LOG "卸载阶段完成"
}

# ============ 主流程 ============
Write-Host "================ SysIns PowerShell 自动安装 开始 ================"
LOG "根目录:" $RootDir

if ($LoadOnly) { Write-Host "LoadOnly: 函数已加载，跳过主流程"; return }

if ($Mode -notin @("all","uu","awesun","uninstall")) { $Mode = "all" }

$hr = CHECK_HUORONG
if ($hr) {
    LOG "检测到火绒安全软件正在运行（$hr），请先手动退出或卸载后再运行本脚本"
    exit 2
}

START_REC ([IntPtr]::Zero) "boot"

if ($Mode -eq "uninstall") {
    UNINSTALL_ALL
    STOP_REC
    exit 0
}

if ($Mode -eq "uu" -or $Mode -eq "all") {
    $mainHwnd = INSTALL_UU
    $loginOk = $false; $uuClip = $null
    if ($mainHwnd -and $mainHwnd -ne [IntPtr]::Zero) {
        SKIP_GUIDE; Start-Sleep -Seconds 1
        if (IS_UU_LOGINED $mainHwnd) {
            LOG "UU 已登录，跳过登录流程"
            $loginOk = $true
        }
        else {
            $login = OPEN_LOGIN $mainHwnd
            if ($login -ne [IntPtr]::Zero) { $loginOk = DO_LOGIN $login $mainHwnd }
            else { LOG "错误：登录窗口未打开"; $loginOk = $false }
        }
        if ($loginOk) { Start-Sleep -Seconds 3; $uuClip = COPY_UU_INFO $mainHwnd }
    }
    else { LOG "UU 安装失败，跳过登录步骤" }
}

if ($Mode -eq "awesun" -or $Mode -eq "all") {
    $awMain = INSTALL_AWSUN
    $awLoginOk = $false; $awId = $null; $awCode = $null
    if ($awMain -and $awMain -ne [IntPtr]::Zero) {
        $awLoginOk = LOGIN_AWSUN $awMain
        if ($awLoginOk) { $r = COPY_AWSUN_INFO $awMain; $awId = $r[0]; $awCode = $r[1] }
    }
    else { LOG "AweSun 安装失败，跳过登录步骤" }
}

if ($Mode -eq "all") {
    if ($loginOk) {
        $body = "网易UU远程已自动安装并登录成功。`r`n识别码信息: $(if ($uuClip) { $uuClip } else { '(未复制到)' })`r`n截图见附件。"
        SEND_MAIL "网易UU远程" $body (Join-Path $LogDir "uu_after_copy.png") | Out-Null
    }
    if ($awLoginOk) {
        $body = "AweSun向日葵已自动安装并登录成功。`r`n识别码: $(if ($awId) { $awId } else { '(未复制到)' })`r`n验证码: $(if ($awCode) { $awCode } else { '(未复制到)' })`r`n截图见附件。"
        SEND_MAIL "AweSun向日葵" $body (Join-Path $LogDir "awsun_copied.png") | Out-Null
    }
    Write-Host "================ 安装结果汇总 ================"
    Write-Host "网易UU远程: $(if ($loginOk) { '成功' } else { '失败' })  识别码复制: $(if ($uuClip) { '成功' } else { '未复制' })"
    Write-Host "AweSun向日葵: $(if ($awLoginOk) { '成功' } else { '失败' })  识别码: $(if ($awId) { '成功' } else { '未复制' })"
    Write-Host "火绒安全: 按要求跳过（不安装）"
    Write-Host "日志目录: $LogDir"
    Write-Host "================================================"
}

STOP_REC
