// AutoInstall_CSharp - C#版自动安装脚本
// 支持 uuyc / AweSun 自动安装、登录、复制识别码、发送邮件；火绒按要求不安装（仅检测报警）
// 编译: csc /target:exe /reference:System.dll,System.Drawing.dll,System.Windows.Forms.dll,MailKit.dll,MimeKit.dll,BouncyCastle.Cryptography.dll AutoInstall.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MimeKit;

namespace AutoInstall_CSharp
{
    static partial class Cfg
    {
        public static string Root = @"D:\SysIns";
        public static string SoftDir = Root + @"\Soft";
        public static string LogDir = Root + @"\Logs";
        public static string Ffmpeg = Root + @"\Tools\ffmpeg.exe";
        public static string UuExe = SoftDir + @"\uuyc_4.42.0.exe";
        public static string AwsExe = SoftDir + @"\AweSun_16.6.0.32198.exe";
        public static string UuDir = @"D:\SoftApp\GameViewer";
        public static bool SendMail = true;
        public static bool Record = true;
        public static string Lang = "CSharp";
    }

    static class Log
    {
        static string path;
        static readonly object lk = new object();
        public static void Init(string ts)
        {
            Directory.CreateDirectory(Cfg.LogDir);
            path = Cfg.LogDir + @"\AutoInstall-" + ts + ".log";
        }
        public static void W(string s)
        {
            var line = DateTime.Now.ToString("HH:mm:ss") + "  " + s;
            try { Console.WriteLine(line); } catch { }
            lock (lk) { try { File.AppendAllText(path, line + "\r\n", System.Text.Encoding.UTF8); } catch { } }
        }
        public static void W(string fmt, params object[] a) { W(string.Format(fmt, a)); }
    }

    static class Win32
    {
        public delegate bool EnumProc(IntPtr h, IntPtr lp);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lp);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr lp);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
        [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] inp, int sz);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, string l);
        [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO g);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr dc, IntPtr bmp, int s, int n, byte[] buf, ref BITMAPINFOHEADER bi, int u);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int sz, out int cb);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool repaint);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO { public int cbSize; public uint flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public RECT rcCaret; }
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public ushort biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }
        [StructLayout(LayoutKind.Explicit, Size = 32)]
        public struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public HARDWAREINPUT hi; }
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public INPUTUNION u; }

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public const uint SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1;
        public const int SW_RESTORE = 9, SW_SHOW = 5, SW_MINIMIZE = 6;
        public const uint WM_CHAR = 0x0102, WM_SETTEXT = 0x000C, BM_CLICK = 0x00F5, WM_CLOSE = 0x0010;
        public const uint KEYEVENTF_UNICODE = 0x4, KEYEVENTF_KEYUP = 0x2;
    }

    class WinInfo
    {
        public IntPtr H; public string Cls, Title; public uint Pid; public Win32.RECT R;
        public int W { get { return R.Right - R.Left; } }
        public int Ht { get { return R.Bottom - R.Top; } }
    }

    static class Win
    {
        static string Txt(IntPtr h) { var sb = new System.Text.StringBuilder(512); Win32.GetWindowText(h, sb, sb.Capacity); return sb.ToString(); }
        static string Cls(IntPtr h) { var sb = new System.Text.StringBuilder(256); Win32.GetClassName(h, sb, sb.Capacity); return sb.ToString(); }

        public static List<WinInfo> EnumTop(bool onlyVisible)
        {
            var list = new List<WinInfo>();
            Win32.EnumWindows((h, lp) =>
            {
                if (!onlyVisible || Win32.IsWindowVisible(h))
                {
                    Win32.RECT r;
                    if (Win32.GetWindowRect(h, out r))
                    {
                        uint pid; Win32.GetWindowThreadProcessId(h, out pid);
                        list.Add(new WinInfo { H = h, Cls = Cls(h), Title = Txt(h), Pid = pid, R = r });
                    }
                }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static WinInfo Find(Predicate<WinInfo> pred, int timeoutMs)
        {
            var end = Environment.TickCount + timeoutMs;
            do
            {
                foreach (var w in EnumTop(true)) if (pred(w)) return w;
                if (Environment.TickCount >= end) break;
                Thread.Sleep(800);
            } while (true);
            return null;
        }

        public static WinInfo FindClass(string clsPart, int minW, int minH, int timeoutMs)
        {
            return Find(w => w.Cls.IndexOf(clsPart, StringComparison.OrdinalIgnoreCase) >= 0 && w.W >= minW && w.Ht >= minH, timeoutMs);
        }

        public static Win32.RECT DwmRect(IntPtr h)
        {
            Win32.RECT r; int sz;
            if (Win32.DwmGetWindowAttribute(h, 14, out r, Marshal.SizeOf(typeof(Win32.RECT)), out sz) == 0 && r.Right > r.Left) return r;
            Win32.GetWindowRect(h, out r); return r;
        }

        public static Win32.RECT ClientScreenRect(IntPtr h)
        {
            Win32.RECT cr; Win32.GetClientRect(h, out cr);
            var p0 = new Win32.POINT { X = 0, Y = 0 }; var p1 = new Win32.POINT { X = cr.Right, Y = cr.Bottom };
            Win32.ClientToScreen(h, ref p0); Win32.ClientToScreen(h, ref p1);
            return new Win32.RECT { Left = p0.X, Top = p0.Y, Right = p1.X, Bottom = p1.Y };
        }

        static readonly string[] KeepCls = { "Shell_TrayWnd", "Progman", "WorkerW" };

        public static bool ForceFg(IntPtr h)
        {
            if (h == IntPtr.Zero || !Win32.IsWindow(h)) return false;
            for (int i = 0; i < 5; i++)
            {
                if (Win32.IsIconic(h)) Win32.ShowWindow(h, Win32.SW_RESTORE);
                var fg = Win32.GetForegroundWindow();
                uint fgPid; uint fgTid = Win32.GetWindowThreadProcessId(fg, out fgPid);
                uint me = Win32.GetCurrentThreadId();
                if (fgTid != me) Win32.AttachThreadInput(me, fgTid, true);
                Win32.ShowWindow(h, Win32.SW_SHOW); Win32.BringWindowToTop(h);
                Win32.SetWindowPos(h, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE);
                Win32.SetForegroundWindow(h);
                Win32.SetWindowPos(h, Win32.HWND_NOTOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE);
                if (fgTid != me) Win32.AttachThreadInput(me, fgTid, false);
                Thread.Sleep(350);
                if (Win32.GetForegroundWindow() == h) return true;
                Thread.Sleep(200);
            }
            Log.W("警告：窗口置前失败 hwnd={0}", h);
            return false;
        }

        static readonly string[] ProtectedNames = { "GameViewer", "AweSun", "Sunlogin", "Oray", "Hips", "sysdiag", "uuyc", "AutoInstall", "ffmpeg", "SystemSettings" };

        public static void MinimizeDisruptors()
        {
            foreach (var w in EnumTop(true))
            {
                if (w.W < 300 || w.Ht < 200) continue;
                if (KeepCls.Any(c => w.Cls == c)) continue;
                string pname = "";
                try { pname = Process.GetProcessById((int)w.Pid).ProcessName; } catch { }
                if (ProtectedNames.Any(n => pname.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                try { Win32.ShowWindow(w.H, Win32.SW_MINIMIZE); } catch { }
            }
        }

        public static void ClickPoint(int x, int y)
        {
            Win32.SetCursorPos(x, y); Thread.Sleep(120);
            Win32.mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); Thread.Sleep(70);
            Win32.mouse_event(0x0004, 0, 0, 0, IntPtr.Zero); Thread.Sleep(120);
        }

        public static Point? ClickRatio(IntPtr h, double rx, double ry, bool client, string tag)
        {
            if (h == IntPtr.Zero || !Win32.IsWindow(h)) { Log.W("点击中止：窗口无效 {0}", tag); return null; }
            bool fg = ForceFg(h);
            var r = client ? ClientScreenRect(h) : DwmRect(h);
            int x = (int)(r.Left + (r.Right - r.Left) * rx); int y = (int)(r.Top + (r.Bottom - r.Top) * ry);
            if (r.Right - r.Left < 50 || r.Bottom - r.Top < 50 || x < 0 || y < 0) { Log.W("点击中止：矩形异常 {0}", tag); return null; }
            Log.W("点击 {0} ({1},{2}) fg={3}", tag ?? "", x, y, fg);
            ClickPoint(x, y);
            return new Point(x, y);
        }

        public static Point[] FindAwsCodeIcons(IntPtr h)
        {
            var rc = ClientScreenRect(h);
            int w = rc.Right - rc.Left, ht = rc.Bottom - rc.Top;
            if (w < 200 || ht < 200) return null;
            using (var bmp = new Bitmap(w, ht))
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(rc.Left, rc.Top, 0, 0, bmp.Size);
                int bestY = -1, bestCnt = 0;
                for (int y = (int)(ht * 0.25); y < (int)(ht * 0.40); y++)
                {
                    int c = 0;
                    for (int x = (int)(w * 0.30); x < (int)(w * 0.58); x++)
                    { var p = bmp.GetPixel(x, y); if (p.R > 200 && p.G > 200 && p.B > 200) c++; }
                    if (c > bestCnt) { bestCnt = c; bestY = y; }
                }
                if (bestCnt < 8 || bestY < 0) return null;
                int textRight = 0;
                for (int x = (int)(w * 0.58); x > (int)(w * 0.30); x--)
                { var p = bmp.GetPixel(x, bestY); if (p.R > 200 && p.G > 200 && p.B > 200) { textRight = x; break; } }
                if (textRight == 0) return null;
                int eyeX = -1, eyeY = bestY;
                for (int x = textRight + 8; x < textRight + 55; x++)
                {
                    int c = 0; int sumY = 0;
                    for (int dy = -6; dy <= 16; dy++)
                    {
                        int yy = bestY + dy;
                        if (yy < 0 || yy >= ht) continue;
                        var p = bmp.GetPixel(x, yy); int v = (p.R + p.G + p.B) / 3;
                        if (v > 45 && v < 225) { c++; sumY += yy; }
                    }
                    if (c >= 3) { eyeX = x; eyeY = sumY / c; break; }
                }
                if (eyeX < 0) return null;
                int copyX = -1, copyY = eyeY;
                for (int x = eyeX + 18; x < eyeX + 50; x++)
                {
                    int c = 0; int sumY = 0;
                    for (int dy = -6; dy <= 16; dy++)
                    {
                        int yy = bestY + dy;
                        if (yy < 0 || yy >= ht) continue;
                        var p = bmp.GetPixel(x, yy); int v = (p.R + p.G + p.B) / 3;
                        if (v > 45 && v < 225) { c++; sumY += yy; }
                    }
                    if (c >= 3) { copyX = x; copyY = sumY / c; break; }
                }
                if (copyX < 0) { copyX = eyeX + 30; copyY = eyeY; }
                Log.W("定位验证码图标 行y={0} 文字右={1} 眼({2},{3}) 复制({4},{5})", bestY, textRight, eyeX, eyeY, copyX, copyY);
                return new[] { new Point(rc.Left + eyeX, rc.Top + eyeY), new Point(rc.Left + copyX, rc.Top + copyY) };
            }
        }

        public static void TypeWmChar(IntPtr ownerHwnd, string text)
        {
            uint pid; uint tid = Win32.GetWindowThreadProcessId(ownerHwnd, out pid);
            foreach (char ch in text)
            {
                var g = new Win32.GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(Win32.GUITHREADINFO)) };
                Win32.GetGUIThreadInfo(tid, ref g);
                var t = g.hwndFocus != IntPtr.Zero ? g.hwndFocus : ownerHwnd;
                Win32.PostMessage(t, Win32.WM_CHAR, (IntPtr)ch, IntPtr.Zero);
                Thread.Sleep(80);
            }
        }

        public static void TypeUnicode(string text)
        {
            foreach (char ch in text)
            {
                var down = new Win32.INPUT { type = 1 };
                down.u.ki = new Win32.KEYBDINPUT { wVk = 0, wScan = (ushort)ch, dwFlags = Win32.KEYEVENTF_UNICODE };
                var up = new Win32.INPUT { type = 1 };
                up.u.ki = new Win32.KEYBDINPUT { wVk = 0, wScan = (ushort)ch, dwFlags = Win32.KEYEVENTF_UNICODE | Win32.KEYEVENTF_KEYUP };
                Win32.SendInput(1, new[] { down }, Marshal.SizeOf(typeof(Win32.INPUT))); Thread.Sleep(40);
                Win32.SendInput(1, new[] { up }, Marshal.SizeOf(typeof(Win32.INPUT))); Thread.Sleep(60);
            }
        }

        public static void CtrlA()
        {
            Win32.INPUT[] ev = new Win32.INPUT[4];
            ev[0].type = 1; ev[0].u.ki = new Win32.KEYBDINPUT { wVk = 0x11 };
            ev[1].type = 1; ev[1].u.ki = new Win32.KEYBDINPUT { wVk = 0x41 };
            ev[2].type = 1; ev[2].u.ki = new Win32.KEYBDINPUT { wVk = 0x41, dwFlags = 2 };
            ev[3].type = 1; ev[3].u.ki = new Win32.KEYBDINPUT { wVk = 0x11, dwFlags = 2 };
            Win32.SendInput((uint)ev.Length, ev, Marshal.SizeOf(typeof(Win32.INPUT))); Thread.Sleep(200);
        }

        public static string ClipRead()
        {
            for (int i = 0; i < 6; i++)
            {
                try { if (Clipboard.ContainsText()) return Clipboard.GetText(); } catch { }
                Thread.Sleep(250);
            }
            return "";
        }

        public static void ClipWrite(string text)
        {
            for (int i = 0; i < 6; i++)
            {
                try { Clipboard.SetText(text ?? ""); return; } catch { }
                Thread.Sleep(200);
            }
        }

        public static IntPtr FindButton(IntPtr parent, string textPart)
        {
            IntPtr found = IntPtr.Zero;
            Win32.EnumChildWindows(parent, (h, lp) =>
            {
                var sb = new System.Text.StringBuilder(256); Win32.GetClassName(h, sb, sb.Capacity);
                if (sb.ToString().IndexOf("Button", StringComparison.OrdinalIgnoreCase) < 0) return true;
                var tb = new System.Text.StringBuilder(256); Win32.GetWindowText(h, tb, tb.Capacity);
                if (tb.ToString().IndexOf(textPart, StringComparison.OrdinalIgnoreCase) >= 0) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        public static IntPtr FindFirstClass(IntPtr parent, string clsPart)
        {
            IntPtr found = IntPtr.Zero;
            Win32.EnumChildWindows(parent, (h, lp) =>
            {
                var sb = new System.Text.StringBuilder(256); Win32.GetClassName(h, sb, sb.Capacity);
                if (sb.ToString().IndexOf(clsPart, StringComparison.OrdinalIgnoreCase) >= 0) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        public static void BmClick(IntPtr btn) { Win32.SendMessage(btn, Win32.BM_CLICK, IntPtr.Zero, null); }

        public static void DumpWindows(string tag)
        {
            Log.W("--- 顶层窗口快照 {0} ---", tag);
            foreach (var w in EnumTop(true))
                if (w.W > 100 && w.Ht > 60)
                    Log.W("  hwnd={0} pid={1} {2}x{3} cls=[{4}] title=[{5}]", w.H, w.Pid, w.W, w.Ht, w.Cls, w.Title);
        }
    }

    static class Shot
    {
        public static string Screen(string name)
        {
            var path = Cfg.LogDir + "\\" + name + ".png";
            try
            {
                var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                using (var bmp = new Bitmap(bounds.Width, bounds.Height))
                using (var g = Graphics.FromImage(bmp))
                { g.CopyFromScreen(0, 0, 0, 0, bmp.Size); bmp.Save(path, ImageFormat.Png); }
            }
            catch (Exception ex) { Log.W("截图失败 {0}: {1}", name, ex.Message); }
            return path;
        }

        public static int BlueCount(Rectangle rc)
        {
            int cnt = 0;
            try
            {
                using (var bmp = new Bitmap(rc.Width, rc.Height))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(rc.Left, rc.Top, 0, 0, rc.Size);
                    for (int y = 0; y < bmp.Height; y += 2)
                        for (int x = 0; x < bmp.Width; x += 2)
                        { var c = bmp.GetPixel(x, y); if (c.B > 170 && c.R < 130 && c.G > 70) cnt++; }
                }
            }
            catch { }
            return cnt;
        }

        public static int WhiteCount(Rectangle rc)
        {
            int cnt = 0;
            try
            {
                using (var bmp = new Bitmap(rc.Width, rc.Height))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(rc.Left, rc.Top, 0, 0, rc.Size);
                    for (int y = 0; y < bmp.Height; y++)
                        for (int x = 0; x < bmp.Width; x++)
                        { var c = bmp.GetPixel(x, y); if (c.R > 180 && c.G > 180 && c.B > 180) cnt++; }
                }
            }
            catch { }
            return cnt;
        }

        public static int GrayRangeCount(Rectangle rc, int lo, int hi)
        {
            int cnt = 0;
            try
            {
                using (var bmp = new Bitmap(rc.Width, rc.Height))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(rc.Left, rc.Top, 0, 0, rc.Size);
                    for (int y = 0; y < bmp.Height; y++)
                        for (int x = 0; x < bmp.Width; x++)
                        {
                            var c = bmp.GetPixel(x, y);
                            int mx = Math.Max(c.R, Math.Max(c.G, c.B)), mn = Math.Min(c.R, Math.Min(c.G, c.B));
                            if (mx - mn <= 18 && mn >= lo && mx <= hi) cnt++;
                        }
                }
            }
            catch { }
            return cnt;
        }

        public static double WhiteRatio(Rectangle rc)
        {
            int cnt = 0, n = 0;
            try
            {
                using (var bmp = new Bitmap(rc.Width, rc.Height))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(rc.Left, rc.Top, 0, 0, rc.Size);
                    for (int y = 0; y < bmp.Height; y += 3)
                        for (int x = 0; x < bmp.Width; x += 3)
                        { var c = bmp.GetPixel(x, y); n++; if (c.R >= 235 && c.G >= 235 && c.B >= 235) cnt++; }
                }
            }
            catch { }
            return n == 0 ? 0 : cnt / (double)n;
        }

        public static int PinkCount(Rectangle rc)
        {
            int cnt = 0;
            try
            {
                using (var bmp = new Bitmap(rc.Width, rc.Height))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(rc.Left, rc.Top, 0, 0, rc.Size);
                    for (int y = 0; y < bmp.Height; y += 2)
                        for (int x = 0; x < bmp.Width; x += 2)
                        { var c = bmp.GetPixel(x, y); if (c.R > 170 && c.G < 140 && c.B < 170 && c.R - c.G > 45) cnt++; }
                }
            }
            catch { }
            return cnt;
        }

        public static double MeanBrightness(Rectangle rc)
        {
            try
            {
                using (var bmp = new Bitmap(rc.Width, rc.Height))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(rc.Left, rc.Top, 0, 0, rc.Size);
                    long sum = 0; int n = 0;
                    for (int y = 0; y < bmp.Height; y += 4)
                        for (int x = 0; x < bmp.Width; x += 4)
                        { var c = bmp.GetPixel(x, y); sum += (c.R * 30 + c.G * 59 + c.B * 11) / 100; n++; }
                    return n == 0 ? -1 : sum / (double)n;
                }
            }
            catch { return -1; }
        }

        public static Point? BluePopupPoint(IntPtr hwnd)
        {
            var r = Win.DwmRect(hwnd); int w = r.Right - r.Left, h = r.Bottom - r.Top;
            var rc = new Rectangle(r.Left + (int)(0.25 * w), r.Top + (int)(0.25 * h), (int)(0.55 * w), (int)(0.50 * h));
            try
            {
                int n = 0, minX = 99999, minY = 99999, maxX = 0, maxY = 0;
                using (var bmp = new Bitmap(rc.Width, rc.Height))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(rc.Left, rc.Top, 0, 0, rc.Size);
                    for (int y = 0; y < bmp.Height; y += 2)
                        for (int x = 0; x < bmp.Width; x += 2)
                        { var c = bmp.GetPixel(x, y); if (c.B > 160 && c.R < 120 && c.B - c.G > 55) { n++; if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; } }
                }
                if (n < 1500) return null;
                int bw = maxX - minX, bh = maxY - minY;
                if (bw < 250 || bh < 130) return null;
                double dens = (double)n / ((bw / 2 + 1) * (bh / 2 + 1));
                if (dens < 0.30) return null;
                return new Point(rc.Left + maxX - 13, rc.Top + minY + 13);
            }
            catch { return null; }
        }

        public static Point? CancelButtonPoint(IntPtr hwnd)
        {
            var r = Win.DwmRect(hwnd); int w = r.Right - r.Left, h = r.Bottom - r.Top;
            var rc = new Rectangle(r.Left + (int)(0.55 * w), r.Top + (int)(0.79 * h), (int)(0.14 * w), (int)(0.08 * h));
            try
            {
                int n = 0, minX = 99999, minY = 99999, maxX = 0, maxY = 0;
                using (var bmp = new Bitmap(rc.Width, rc.Height))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(rc.Left, rc.Top, 0, 0, rc.Size);
                    for (int y = 0; y < bmp.Height; y++)
                        for (int x = 0; x < bmp.Width; x++)
                        { var c = bmp.GetPixel(x, y); if (c.R > 180 && c.G > 180 && c.B > 180) { n++; if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; } }
                }
                int bw = maxX - minX, bh = maxY - minY;
                if (n < 50 || bw > 110 || bh > 45) return null;
                return new Point(rc.Left + (minX + maxX) / 2, rc.Top + (minY + maxY) / 2);
            }
            catch { return null; }
        }

        public static void EnsureChecked(IntPtr hwnd, double rx, double ry, bool client, string tag)
        {
            var r = client ? Win.ClientScreenRect(hwnd) : Win.DwmRect(hwnd);
            int cx = (int)(r.Left + (r.Right - r.Left) * rx); int cy = (int)(r.Top + (r.Bottom - r.Top) * ry);
            var rc = new Rectangle(cx - 9, cy - 9, 18, 18);
            int blue = BlueCount(rc);
            Log.W("协议勾选检测 {0} blue={1}", tag, blue);
            if (blue < 8) Win.ClickRatio(hwnd, rx, ry, client, tag + "-勾选协议");
            else Log.W("协议已勾选，跳过");
        }
    }

    static class Recorder
    {
        static Process p;
        public static string Ts;

        static string BuildArgs(IntPtr hwnd, string path)
        {
            string geo = "";
            if (hwnd != IntPtr.Zero && Win32.IsWindow(hwnd))
            {
                Win32.RECT r;
                if (Win32.GetWindowRect(hwnd, out r))
                {
                    int w = r.Right - r.Left, h = r.Bottom - r.Top;
                    if (w >= 100 && h >= 100)
                    {
                        w &= ~1; h &= ~1;
                        geo = string.Format("-offset_x {0} -offset_y {1} -video_size {2}x{3} ", r.Left, r.Top, w, h);
                        Log.W("录屏区域: {0},{1} {2}x{3}", r.Left, r.Top, w, h);
                    }
                }
            }
            return string.Format("-y -threads 2 -thread_queue_size 64 -loglevel error -f gdigrab -framerate 5 -draw_mouse 1 {0}-i desktop -pix_fmt yuv420p -an \"{1}\"", geo, path);
        }

        static bool Launch(string args, string path)
        {
            try
            {
                var psi = new ProcessStartInfo { FileName = Cfg.Ffmpeg, Arguments = args, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
                p = Process.Start(psi); Thread.Sleep(1500);
                if (p.HasExited) { Log.W("录屏进程立即退出"); p = null; return false; }
                Log.W("录屏开始 -> {0}", path); return true;
            }
            catch (Exception ex) { Log.W("录屏启动失败: {0}", ex.Message); p = null; return false; }
        }

        public static void Start(IntPtr hwnd, string tag)
        {
            if (!Cfg.Record) return;
            var path = Cfg.LogDir + @"\AutoInstallRec-" + Ts + "-" + tag + "_" + Cfg.Lang + ".mkv";
            var base_ = BuildArgs(hwnd, path);
            Launch(base_.Replace("-pix_fmt", "-c:v libx264 -preset ultrafast -crf 26 -pix_fmt"), path);
        }

        public static void Switch(IntPtr hwnd, string tag)
        {
            if (!Cfg.Record) return;
            Stop(); Start(hwnd, tag);
        }

        public static void Stop()
        {
            if (p == null) return;
            try
            {
                bool killed = false;
                try
                {
                    if (!p.HasExited)
                    {
                        p.StandardInput.Write("q");
                        if (!p.WaitForExit(8000)) killed = true;
                    }
                    else killed = true;
                }
                catch { killed = true; }
                if (killed) foreach (var pr in Process.GetProcessesByName("ffmpeg")) { try { pr.Kill(); } catch { } }
                Thread.Sleep(500); Log.W("录屏结束");
            }
            finally { p = null; }
        }
    }

    static class Resume
    {
        public static string StatePath { get { return Cfg.LogDir + @"\resume_" + Cfg.Lang + ".txt"; } }
        public static bool Pending { get { return File.Exists(StatePath); } }
        public static string TaskName { get { return "SysInsAutoInstall_" + Cfg.Lang; } }

        public static void Save(string mode, ulong tick)
        {
            Directory.CreateDirectory(Cfg.LogDir);
            File.WriteAllText(StatePath, mode + "\n" + tick);
            Log.W("续跑状态已保存: {0} tick={1}", mode, tick);
        }

        public static string ReadMode()
        {
            try { var lines = File.ReadAllLines(StatePath); return lines.Length > 0 ? lines[0].Trim() : null; } catch { return null; }
        }

        public static ulong ReadTick()
        {
            try { var lines = File.ReadAllLines(StatePath); return lines.Length > 1 ? ulong.Parse(lines[1].Trim()) : 0; } catch { return 0; }
        }

        public static void Clear()
        {
            try { if (File.Exists(StatePath)) File.Delete(StatePath); } catch { }
        }

        public static void RegisterTask(string mode)
        {
            var exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var tr = "\"\\\"" + exe + "\\\" -resume\"";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = string.Format("/create /tn {0} /tr {1} /sc onlogon /rl highest /f", TaskName, tr),
                    UseShellExecute = false, CreateNoWindow = true
                };
                var pr = Process.Start(psi); pr.WaitForExit(15000);
                Save(mode, Win32.GetTickCount64());
                Log.W("已创建登录计划任务: {0}", TaskName);
            }
            catch (Exception ex) { Log.W("创建计划任务失败: {0}", ex.Message); }
        }

        public static void RemoveTask()
        {
            try
            {
                var pr = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/delete /tn " + TaskName + " /f",
                    UseShellExecute = false, CreateNoWindow = true
                });
                pr.WaitForExit(10000);
            }
            catch { }
        }

        public static string CheckResume()
        {
            var mode = ReadMode();
            if (mode == null) return null;
            ulong nowTick = Win32.GetTickCount64();
            ulong savedTick = ReadTick();
            if (nowTick < savedTick)
            {
                Log.W("重启已确认（tick {0} < {1}），续跑: {2}", nowTick, savedTick, mode);
                RemoveTask(); Clear();
                return mode;
            }
            Log.W("续跑在关机过程中被触发（tick {0} >= {1}），不执行任何操作，等待真正重启", nowTick, savedTick);
            Environment.Exit(3);
            return null;
        }

        public static void Reboot()
        {
            Log.W("系统即将重启（卸载sysdiag后必须重启才能重装）...");
            Process.Start(new ProcessStartInfo { FileName = "shutdown", Arguments = "/r /t 10 /c \"SysIns: sysdiag已卸载，重启后继续自动安装\"", UseShellExecute = false, CreateNoWindow = true });
        }
    }

    static class Proc
    {
        public static void KillNames(params string[] names)
        {
            foreach (var n in names)
                foreach (var pr in Process.GetProcessesByName(n))
                    try { pr.Kill(); Log.W("结束进程 {0}({1})", n, pr.Id); } catch { }
        }
        public static void Run(string file, string args, int waitMs)
        {
            try
            {
                var pr = Process.Start(new ProcessStartInfo { FileName = file, Arguments = args ?? "", UseShellExecute = false, CreateNoWindow = true });
                if (waitMs > 0) { if (!pr.WaitForExit(waitMs)) try { pr.Kill(); } catch { } }
            }
            catch (Exception ex) { Log.W("命令失败 {0}: {1}", file, ex.Message); }
        }
        public static Process StartExe(string file)
        {
            var psi = new ProcessStartInfo { FileName = file, WorkingDirectory = Path.GetDirectoryName(file), UseShellExecute = true };
            return Process.Start(psi);
        }
    }

    static class Mail
    {
        static ImapClient ic; static IMailFolder inbox; static readonly object lk = new object();

        static void IConnect()
        {
            lock (lk)
            {
                if (ic != null && ic.IsConnected) return;
                ic = new ImapClient(); ic.SslProtocols = SslProtocols.Tls12;
                ic.ServerCertificateValidationCallback = (s, c, ch, e) => true;
                ic.Connect(Cfg.ImapHost, Cfg.ImapPort, true); ic.Authenticate(Cfg.ImapUser, Cfg.ImapPwd);
                inbox = ic.Inbox; inbox.Open(FolderAccess.ReadOnly);
                Log.W("IMAP 已连接，邮件数 {0}", inbox.Count);
            }
        }

        public static uint MaxCodeUid()
        {
            IConnect();
            lock (lk)
            {
                try
                {
                    var uids = inbox.Search(SearchQuery.SubjectContains(Cfg.CodeSubject));
                    uint max = 0; foreach (var u in uids) if (u.Id > max) max = u.Id;
                    return max;
                }
                catch (Exception ex)
                {
                    Log.W("IMAP 搜索异常，重连: {0}", ex.Message);
                    try { ic.Disconnect(true); } catch { } ic = null; IConnect();
                    var uids = inbox.Search(SearchQuery.SubjectContains(Cfg.CodeSubject));
                    uint max = 0; foreach (var u in uids) if (u.Id > max) max = u.Id;
                    return max;
                }
            }
        }

        public static string WaitCode(uint baseUid)
        {
            Log.W("按要求等待 45 秒后再收取验证码...");
            Thread.Sleep(45000);
            var deadline = DateTime.Now.AddSeconds(240);
            while (DateTime.Now < deadline)
            {
                Thread.Sleep(5000); IConnect();
                lock (lk)
                {
                    try
                    {
                        var uids = inbox.Search(SearchQuery.SubjectContains(Cfg.CodeSubject));
                        uint max = 0; UniqueId maxU = default(UniqueId);
                        foreach (var u in uids) if (u.Id > max) { max = u.Id; maxU = u; }
                        if (max > baseUid)
                        {
                            var msg = inbox.GetMessage(maxU);
                            var body = msg.TextBody ?? msg.HtmlBody ?? "";
                            Log.W("收到新邮件: {0}", msg.Subject);
                            var m = Regex.Match(body, @"验证码[^0-9]{0,12}(\d{4,6})");
                            string code = m.Success ? m.Groups[1].Value : Regex.Match(body, @"\d{6}").Value;
                            if (code.Length >= 4) { Log.W("提取验证码: {0}", code); return code; }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.W("IMAP 轮询异常: {0}", ex.Message);
                        try { ic.Disconnect(true); } catch { } ic = null;
                    }
                }
            }
            Log.W("等待验证码超时"); return null;
        }

        public static void Send(string softName, string body, IEnumerable<string> attach)
        {
            if (!Cfg.SendMail) { Log.W("已禁用发信，跳过: {0}", softName); return; }
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress("pcbbs", Cfg.SmtpUser));
            msg.To.Add(new MailboxAddress("yihuos", Cfg.MailTo));
            msg.Subject = "您的软件-" + softName + "安装成功，现附上截图。";
            var b = new BodyBuilder { TextBody = body };
            foreach (var f in attach ?? new string[0]) if (File.Exists(f)) b.Attachments.Add(f);
            msg.Body = b.ToMessageBody();
            using (var s = new SmtpClient())
            {
                s.SslProtocols = SslProtocols.Tls12; s.ServerCertificateValidationCallback = (sd, c, ch, e) => true; s.Timeout = 60000;
                s.Connect(Cfg.SmtpHost, Cfg.SmtpPort, true); s.Authenticate(Cfg.SmtpUser, Cfg.SmtpPwd); s.Send(msg); s.Disconnect(true);
            }
            Log.W("邮件已发送: {0}", msg.Subject);
        }

        public static void Close()
        {
            lock (lk) { if (ic != null) { try { ic.Disconnect(true); } catch { } ic = null; } }
        }
    }

    static class FlowUU
    {
        public static bool LoginOk, InfoOk; public static string Clip; static WinInfo mainWnd;

        static WinInfo FindMain(int timeoutMs)
        {
            return Win.Find(w => w.Cls.IndexOf("QWindowIcon", StringComparison.OrdinalIgnoreCase) >= 0
                && w.Title.Contains("UU远程") && w.W > 700 && w.Ht > 500, timeoutMs);
        }

        static WinInfo FindLogin()
        {
            return Win.Find(w => w.Title.IndexOf("GameViewer", StringComparison.OrdinalIgnoreCase) >= 0
                && w.Cls.IndexOf("QWindowIcon", StringComparison.OrdinalIgnoreCase) < 0 && w.W > 700 && w.Ht > 500, 1000);
        }

        static WinInfo WaitLoginStable()
        {
            var deadline = DateTime.Now.AddSeconds(20); WinInfo last = null; int stable = 0;
            while (DateTime.Now < deadline)
            {
                var w = FindLogin();
                if (w != null)
                {
                    var r = Win.DwmRect(w.H);
                    if (last != null && last.H == w.H && last.R.Left == r.Left && last.R.Top == r.Top
                        && Math.Abs(w.W - last.W) <= 1 && Math.Abs(w.Ht - last.Ht) <= 1)
                    { stable++; if (stable >= 2 && w.W >= 760 && w.Ht >= 560) return w; }
                    else stable = 0;
                    last = w;
                }
                Thread.Sleep(700);
            }
            Log.W("警告：登录窗未达到稳定尺寸，使用当前窗口"); return last;
        }

        static void RestoreApp()
        {
            var m = FindMain(500);
            if (m != null && Win32.IsIconic(m.H)) { Win32.ShowWindow(m.H, Win32.SW_RESTORE); Thread.Sleep(800); }
        }

        static bool LoginClick(double rx, double ry, string tag)
        {
            for (int i = 0; i < 3; i++)
            {
                RestoreApp(); var l = FindLogin();
                if (l == null) { Thread.Sleep(800); continue; }
                if (Win.ClickRatio(l.H, rx, ry, false, tag).HasValue) return true;
                Thread.Sleep(800);
            }
            return false;
        }

        public static bool Install()
        {
            Proc.KillNames("GameViewer", "GameViewerService"); Thread.Sleep(1000);
            Log.W("启动 UU 安装包"); Proc.StartExe(Cfg.UuExe);
            var inst = Win.Find(w => w.Title.Contains("UU远程") && w.W > 300, 60000);
            if (inst == null) { Log.W("错误：找不到 UU 安装窗口"); Win.DumpWindows("uu无安装窗"); Shot.Screen("uu_no_installer"); return false; }
            Log.W("UU 安装窗口出现 cls={0} {1}x{2}", inst.Cls, inst.W, inst.Ht); Thread.Sleep(2000);
            Recorder.Switch(inst.H, "uu-install");
            Shot.Screen("uu_installer_1"); Win.ForceFg(inst.H);
            var btnNext = Win.FindButton(inst.H, "下一步");
            if (btnNext != IntPtr.Zero) { Win.BmClick(btnNext); Log.W("点击下一步"); Thread.Sleep(2000); }
            var ed = Win.FindFirstClass(inst.H, "Edit");
            if (ed == IntPtr.Zero) { Log.W("错误：找不到路径输入框"); Shot.Screen("uu_no_edit"); return false; }
            Win32.SendMessage(ed, Win32.WM_SETTEXT, IntPtr.Zero, Cfg.UuDir); Log.W("安装目录已设置: {0}", Cfg.UuDir);
            Thread.Sleep(800); Shot.Screen("uu_installer_path");
            var btnInst = Win.FindButton(inst.H, "安装");
            if (btnInst == IntPtr.Zero) { Log.W("错误：找不到安装按钮"); return false; }
            Win.BmClick(btnInst); Log.W("已点击安装，等待安装窗口关闭...");
            var deadline = DateTime.Now.AddSeconds(240);
            while (DateTime.Now < deadline) { Thread.Sleep(2000); if (!Win32.IsWindow(inst.H)) break; }
            if (Win32.IsWindow(inst.H)) { Log.W("警告：安装窗口仍在"); Shot.Screen("uu_install_stuck"); }
            mainWnd = FindMain(120000);
            if (mainWnd == null) { Log.W("错误：UU 主窗口未出现"); Win.DumpWindows("uu无主窗"); Shot.Screen("uu_no_main"); return false; }
            Log.W("UU 主窗口出现 {0}x{1}", mainWnd.W, mainWnd.Ht); Recorder.Switch(mainWnd.H, "uu-main"); Win.ForceFg(mainWnd.H); Thread.Sleep(4000); Shot.Screen("uu_main");
            return true;
        }

        static bool IsLoggedOut(IntPtr h)
        {
            var r = Win.DwmRect(h); int w = r.Right - r.Left, ht = r.Bottom - r.Top;
            int x = (int)(r.Left + w * 0.629); int y = (int)(r.Top + ht * 0.763);
            int blue = Shot.BlueCount(new Rectangle(x - 30, y - 16, 60, 32));
            Log.W("UU 登录态检测 蓝色像素={0}", blue); return blue >= 30;
        }

        public static bool EnsureLogin()
        {
            for (int i = 0; i < 6; i++) { var g = FindMain(500); if (g != null) Win.ClickRatio(g.H, 0.698, 0.772, false, "UU跳过引导"); Thread.Sleep(1300); }
            var m = FindMain(3000);
            if (m != null && !IsLoggedOut(m.H)) { Log.W("UU 已是登录态，跳过短信登录"); LoginOk = true; Thread.Sleep(1000); return true; }
            return Login();
        }

        public static bool Login()
        {
            for (int i = 0; i < 6; i++) { var m = FindMain(500); if (m != null) Win.ClickRatio(m.H, 0.698, 0.772, false, "UU跳过引导"); Thread.Sleep(1300); }
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var m = FindMain(500); if (m == null) { Thread.Sleep(1000); continue; }
                Win.ClickRatio(m.H, 0.629, 0.763, false, "UU打开登录"); Thread.Sleep(2500);
                var l = WaitLoginStable(); if (l != null) { Log.W("UU 登录窗稳定 {0}x{1}", l.W, l.Ht); break; }
            }
            var login = WaitLoginStable();
            if (login == null) { Log.W("错误：UU 登录窗未打开"); Win.DumpWindows("uu无登录窗"); Shot.Screen("uu_no_login"); return false; }
            Win.ForceFg(login.H); Thread.Sleep(800); Shot.Screen("uu_login_page");

            if (!LoginClick(0.5458, 0.6628, "UU协议框")) return false; Thread.Sleep(700); Shot.Screen("uu_agree");
            if (!LoginClick(0.7067, 0.3503, "UU手机号框")) return false; Thread.Sleep(500);
            var lf = FindLogin(); if (lf != null) Win.TypeWmChar(lf.H, Cfg.Phone); Thread.Sleep(700); Shot.Screen("uu_phone_in");

            uint baseUid = Mail.MaxCodeUid();
            if (!LoginClick(0.8082, 0.4194, "UU获取验证码")) return false; Thread.Sleep(2500); Shot.Screen("uu_sendcode");
            Log.W("UU 已请求验证码，基线 UID={0}", baseUid);

            string code = Mail.WaitCode(baseUid);
            if (code == null) { Shot.Screen("uu_code_timeout"); return false; }

            if (!LoginClick(0.6324, 0.4243, "UU验证码框")) return false; Thread.Sleep(400);
            lf = FindLogin(); if (lf != null) Win.TypeWmChar(lf.H, code); Thread.Sleep(700); Shot.Screen("uu_code_in");
            LoginClick(0.7092, 0.5822, "UU登录");
            var deadline = DateTime.Now.AddSeconds(25);
            while (DateTime.Now < deadline) { Thread.Sleep(1000); if (FindLogin() == null) { Log.W("UU 登录成功"); Thread.Sleep(3000); Shot.Screen("uu_login_done"); LoginOk = true; return true; } }
            Log.W("警告：UU 登录窗仍在"); Shot.Screen("uu_login_fail"); return false;
        }

        static bool AssistCardVisible(IntPtr h)
        {
            var r = Win.DwmRect(h); int w = r.Right - r.Left, ht = r.Bottom - r.Top;
            int x = (int)(r.Left + w * 0.62); int y = (int)(r.Top + ht * 0.35);
            return Shot.WhiteRatio(new Rectangle(x, y, (int)(w * 0.26), (int)(ht * 0.12))) > 0.82;
        }

        public static bool CopyInfo()
        {
            var m = FindMain(8000); if (m == null) { Log.W("错误：复制阶段找不到主窗"); return false; }
            mainWnd = m; Win.ForceFg(m.H); Thread.Sleep(800);
            Win.ClickRatio(m.H, 0.240, 0.107, false, "UU折叠我的设备"); Thread.Sleep(1200); Shot.Screen("uu_nav_collapsed");
            bool page = false;
            for (int i = 0; i < 4 && !page; i++)
            {
                Win.ClickRatio(m.H, 0.098, 0.226, false, "UU开始协助");
                for (int j = 0; j < 9; j++) { Thread.Sleep(600); var t = FindMain(300); if (t != null && AssistCardVisible(t.H)) { page = true; break; } }
                if (!page) { Win.ClickRatio(m.H, 0.240, 0.172, false, "UU远程协助分组"); Thread.Sleep(1000); }
            }
            if (!page) { Log.W("错误：未进入远程协助页"); Shot.Screen("uu_no_assist"); return false; }
            Log.W("已进入远程协助页"); Thread.Sleep(800); Shot.Screen("uu_info_page");
            Win.ClickRatio(m.H, 0.734, 0.379, false, "UU显示验证码"); Thread.Sleep(1500); Shot.Screen("uu_code_shown");
            string old = Win.ClipRead();
            Win.ClickRatio(m.H, 0.871, 0.375, false, "UU复制并分享");
            string nowS = "";
            for (int i = 0; i < 12; i++) { Thread.Sleep(700); nowS = Win.ClipRead(); if (!string.IsNullOrEmpty(nowS) && nowS != old) break; }
            Shot.Screen("uu_after_copy");
            if (string.IsNullOrEmpty(nowS) || nowS == old) { Log.W("警告：UU 剪贴板未更新"); return false; }
            Clip = nowS.Trim(); Log.W("UU 复制内容: {0}", Clip.Replace("\r", " ").Replace("\n", " ")); InfoOk = true; return true;
        }

        public static void Run()
        {
            try
            {
                bool ok = Install();
                if (ok && EnsureLogin())
                {
                    Log.W("UU 登录设置完成（不复制识别码/验证码，不发邮件）");
                }
            }
            catch (Exception ex) { Log.W("UU 流程异常: {0}", ex); Shot.Screen("uu_exception"); }
        }
    }

    static class FlowAweSun
    {
        public static bool LoginOk, InfoOk; public static string IdClip, CodeClip; static WinInfo mainWnd;

        static bool IsAwsCls(string cls)
        {
            return cls.IndexOf("OrayUI", StringComparison.OrdinalIgnoreCase) >= 0 || cls.IndexOf("FLUTTER_RUNNER", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool Install()
        {
            Proc.KillNames("AweSun", "SunloginClient", "OldAweSun"); Thread.Sleep(1000);
            var before = new HashSet<IntPtr>(Win.EnumTop(true).Select(w => w.H));
            Log.W("启动 AweSun 安装包"); Proc.StartExe(Cfg.AwsExe);
            var inst = Win.Find(w => !before.Contains(w.H) && IsAwsCls(w.Cls) && w.W > 500 && w.Ht > 350, 60000);
            if (inst == null) inst = Win.Find(w => IsAwsCls(w.Cls) && w.W > 500 && w.Ht > 350, 5000);
            if (inst == null) { Log.W("错误：找不到 AweSun 安装窗口"); Win.DumpWindows("awsun无安装窗"); Shot.Screen("awsun_no_installer"); return false; }
            Log.W("AweSun 安装窗口出现 {0}x{1}", inst.W, inst.Ht); Recorder.Switch(inst.H, "awesun-install"); Shot.Screen("awsun_installer");
            Win.ClickRatio(inst.H, 0.500, 0.548, false, "AweSun立即安装");

            var deadline = DateTime.Now.AddSeconds(420); IntPtr stable = IntPtr.Zero; int introClicks = 0;
            while (DateTime.Now < deadline)
            {
                Thread.Sleep(3000);
                var dlg = Win.EnumTop(true).FirstOrDefault(x => IsAwsCls(x.Cls) && x.W > 300 && x.W < 520 && x.Ht > 140 && x.Ht < 260);
                if (dlg != null)
                {
                    Log.W("检测到版本卸载确认弹窗 {0}x{1}，点击确定", dlg.W, dlg.Ht);
                    Win.ClickRatio(dlg.H, 0.29, 0.76, false, "AweSun卸载旧版确定");
                    Thread.Sleep(5000);
                }
                var cands = Win.EnumTop(true).Where(x => IsAwsCls(x.Cls) && x.W > 600 && x.Ht > 400).ToList();
                if (cands.Count == 0) continue;
                WinInfo winIntro = null, winDark = null; double introSize = 0; int darkSize = 0;
                foreach (var x in cands)
                {
                    var rr = Win.DwmRect(x.H);
                    var rc = new Rectangle(rr.Left + (rr.Right - rr.Left) / 4, rr.Top + (rr.Bottom - rr.Top) / 3,
                                           (rr.Right - rr.Left) / 2, (rr.Bottom - rr.Top) / 3);
                    double br = Shot.MeanBrightness(rc); int area = x.W * x.Ht; string pname = "";
                    try { pname = Process.GetProcessById((int)x.Pid).ProcessName; } catch { }
                    bool isMainProc = pname.Equals("AweSun", StringComparison.OrdinalIgnoreCase);
                    if (br > 150 && area > introSize) { introSize = area; winIntro = x; }
                    if (br < 90 && isMainProc && area > darkSize) { darkSize = area; winDark = x; }
                }
                if (winIntro != null)
                {
                    var r = Win.DwmRect(winIntro.H);
                    int bx = r.Left + (int)((r.Right - r.Left) * 0.50); int by = r.Top + (int)((r.Bottom - r.Top) * 0.744);
                    int blue = Shot.BlueCount(new Rectangle(bx - 140, by - 35, 280, 70));
                    Log.W("检测到亮色页面(blue={0})，疑似16欢迎页", blue); Shot.Screen("awsun_intro");
                    if (blue >= 10 && introClicks < 6) { Win.ClickRatio(winIntro.H, 0.50, 0.744, false, "AweSun立即进入"); introClicks++; Thread.Sleep(3000); }
                    else Thread.Sleep(2000);
                }
                if (winDark != null)
                {
                    if (stable == winDark.H)
                    {
                        mainWnd = winDark; Log.W("AweSun 深色主窗稳定出现 {0}x{1}", winDark.W, winDark.Ht);
                        Recorder.Switch(winDark.H, "awesun-main");
                        Win.ForceFg(winDark.H); Thread.Sleep(3000); Shot.Screen("awsun_main"); return true;
                    }
                    stable = winDark.H; Thread.Sleep(1500);
                }
            }
            Log.W("错误：等待 AweSun 主窗口超时"); Win.DumpWindows("awsun无主窗"); Shot.Screen("awsun_no_main"); return false;
        }

        static bool IsLoggedIn()
        {
            var r = Win.DwmRect(mainWnd.H); int w = r.Right - r.Left, h = r.Bottom - r.Top;
            var rc = new Rectangle(r.Left + (int)(0.025 * w) - 20, r.Top + (int)(0.062 * h) - 20, 40, 40);
            return Shot.PinkCount(rc) >= 10;
        }

        static bool NianMiVisible()
        {
            var r = Win.DwmRect(mainWnd.H); int w = r.Right - r.Left, h = r.Bottom - r.Top;
            var rc = new Rectangle(r.Left + (int)(0.22 * w), r.Top + (int)(0.15 * h), (int)(0.56 * w), (int)(0.35 * h));
            return Shot.WhiteRatio(rc) > 0.35;
        }

        static void DismissAds(int rounds)
        {
            for (int i = 0; i < rounds; i++)
            {
                var p = Shot.BluePopupPoint(mainWnd.H);
                if (p.HasValue) { Log.W("检测到蓝色广告弹窗，关闭"); Win.ClickPoint(p.Value.X, p.Value.Y); Thread.Sleep(1500); Shot.Screen("awsun_ad_closed"); continue; }
                if (NianMiVisible()) { Log.W("检测到免密远控弹窗，关闭"); Win.ClickRatio(mainWnd.H, 0.772, 0.100, false, "AweSun关免密弹窗"); Thread.Sleep(1500); Shot.Screen("awsun_nianmi_closed"); continue; }
                p = Shot.CancelButtonPoint(mainWnd.H);
                if (p.HasValue) { Log.W("检测到推广弹窗，点取消 {0},{1}", p.Value.X, p.Value.Y); Win.ClickPoint(p.Value.X, p.Value.Y); Thread.Sleep(1500); Shot.Screen("awsun_promo_closed"); continue; }
                return;
            }
        }

        static bool IdVisible()
        {
            var r = Win.DwmRect(mainWnd.H); int w = r.Right - r.Left, h = r.Bottom - r.Top;
            var rc = new Rectangle(r.Left + (int)(0.30 * w), r.Top + (int)(0.289 * h), (int)(0.205 * w), (int)(0.063 * h));
            return Shot.WhiteCount(rc) >= (int)(rc.Width * rc.Height * 0.085);
        }

        public static bool Login()
        {
            Win.ForceFg(mainWnd.H); Thread.Sleep(1000); DismissAds(2);
            if (IsLoggedIn()) { Log.W("AweSun 已是登录态，跳过短信登录"); Shot.Screen("awsun_logged_in"); DismissPopups(); LoginOk = true; return true; }
            Win.ClickRatio(mainWnd.H, 0.075, 0.070, false, "AweSun登录注册"); Thread.Sleep(3000); Shot.Screen("awsun_login_page");
            Shot.EnsureChecked(mainWnd.H, 0.433, 0.805, false, "AweSun");
            Win.ClickRatio(mainWnd.H, 0.600, 0.347, false, "AweSun手机号框"); Thread.Sleep(500);
            Win.TypeUnicode(Cfg.Phone); Thread.Sleep(700); Shot.Screen("awsun_phone_in");

            uint baseUid = Mail.MaxCodeUid();
            Win.ClickRatio(mainWnd.H, 0.722, 0.434, false, "AweSun获取验证码"); Thread.Sleep(3000); Shot.Screen("awsun_sendcode");
            Log.W("AweSun 已请求验证码，基线 UID={0}", baseUid);

            string code = Mail.WaitCode(baseUid);
            if (code == null) { Shot.Screen("awsun_code_timeout"); return false; }

            Win.ClickRatio(mainWnd.H, 0.540, 0.431, false, "AweSun验证码框"); Thread.Sleep(400);
            Win.TypeUnicode(code); Thread.Sleep(700); Shot.Screen("awsun_code_in");
            Win.ClickRatio(mainWnd.H, 0.590, 0.548, false, "AweSun登录"); Thread.Sleep(10000); Shot.Screen("awsun_after_login");
            DismissPopups(); LoginOk = true; return true;
        }

        static bool UpgradePopupVisible()
        {
            var r = Win.DwmRect(mainWnd.H); int w = r.Right - r.Left, h = r.Bottom - r.Top;
            var rc = new Rectangle(r.Left + (int)(0.895 * w), r.Top + (int)(0.069 * h), (int)(0.015 * w), (int)(0.023 * h));
            return Shot.GrayRangeCount(rc, 80, 150) >= 12;
        }

        static bool CloseUpgradePopup()
        {
            if (!UpgradePopupVisible()) return true;
            Log.W("检测到升级会员弹窗，点击关闭 X");
            Win.ClickRatio(mainWnd.H, 0.903, 0.080, false, "AweSun关升级会员");
            Thread.Sleep(1800);
            if (!UpgradePopupVisible()) { Shot.Screen("awsun_upgrade_closed"); return true; }

            Log.W("弹窗未响应，重启 AweSun 恢复");
            Proc.KillNames("AweSun"); Thread.Sleep(2500);
            Proc.StartExe(@"C:\Program Files\Oray\AweSun\AweSun.exe");
            var w2 = Win.Find(x => IsAwsCls(x.Cls) && x.W > 900 && x.Ht > 500, 30000);
            if (w2 == null) { Log.W("重启后未找到 AweSun 窗口"); return false; }
            mainWnd = w2; Thread.Sleep(2500);
            bool gone = !UpgradePopupVisible();
            Log.W("AweSun 已重启，弹窗{0}", gone ? "已消除" : "仍存在");
            Shot.Screen("awsun_restarted");
            return gone;
        }

        static void DismissPopups()
        {
            bool wasVisible = false; int miss = 0;
            for (int i = 0; i < 8; i++)
            {
                if (NianMiVisible()) { wasVisible = true; miss = 0; Win.ClickRatio(mainWnd.H, 0.772, 0.100, false, "AweSun关免密弹窗"); Thread.Sleep(1800); }
                else { if (wasVisible) break; if (++miss >= 2) break; Thread.Sleep(1500); }
            }
            Thread.Sleep(500); CloseUpgradePopup(); Thread.Sleep(500); Shot.Screen("awsun_popups_closed");
        }

        public static bool SetPassword()
        {
            DismissAds(2); Win.ClickRatio(mainWnd.H, 0.060, 0.950, false, "AweSun设置"); Thread.Sleep(2000); Shot.Screen("awsun_settings");
            DismissAds(2); Win.ClickRatio(mainWnd.H, 0.260, 0.435, false, "AweSun安全"); Thread.Sleep(2000); Shot.Screen("awsun_security_page");
            DismissAds(2);
            var cr = Win.DwmRect(mainWnd.H);
            int cbx = cr.Left + (int)(0.465 * (cr.Right - cr.Left)); int cby = cr.Top + (int)(0.464 * (cr.Bottom - cr.Top));
            int cbBlue = Shot.BlueCount(new Rectangle(cbx - 9, cby - 9, 18, 18)); Log.W("访问密码勾选检测 blue={0}", cbBlue);
            if (cbBlue >= 8) Win.ClickRatio(mainWnd.H, 0.548, 0.464, false, "AweSun修改密码");
            else Win.ClickRatio(mainWnd.H, 0.465, 0.464, false, "AweSun勾选访问密码");
            Thread.Sleep(1500); Shot.Screen("awsun_pwd_fields"); DismissAds(2);
            Win.ClickRatio(mainWnd.H, 0.642, 0.534, false, "AweSun密码1"); Thread.Sleep(500); Win.CtrlA(); Win.TypeUnicode(Cfg.AwsPwd); Thread.Sleep(500);
            Win.ClickRatio(mainWnd.H, 0.642, 0.609, false, "AweSun密码2"); Thread.Sleep(500); Win.CtrlA(); Win.TypeUnicode(Cfg.AwsPwd); Thread.Sleep(600); Shot.Screen("awsun_pwd_in");
            DismissAds(2); Win.ClickRatio(mainWnd.H, 0.519, 0.759, false, "AweSun保存"); Thread.Sleep(2000); Shot.Screen("awsun_pwd_saved"); DismissAds(2); DismissPopups(); return true;
        }

        static string WaitClipChange(string old, double rx, double ry, string tag)
        {
            string cur = old;
            for (int i = 0; i < 10; i++) { Thread.Sleep(600); cur = Win.ClipRead(); if (!string.IsNullOrEmpty(cur) && cur.Trim() != (old ?? "").Trim()) break; if (i == 2) Win.ClickRatio(mainWnd.H, rx, ry, false, tag + "-重试"); }
            return cur;
        }

        public static bool CopyInfo()
        {
            DismissPopups(); DismissAds(3); bool pageReady = false;
            for (int i = 0; i < 12; i++)
            {
                DismissAds(2);
                if (IdVisible()) { pageReady = true; break; }
                if (i == 3 || i == 6) CloseUpgradePopup();
                Win.ClickRatio(mainWnd.H, 0.075, 0.156, false, "AweSun远程协助"); Thread.Sleep(2500);
            }
            Shot.Screen("awsun_info_page"); if (!pageReady) Log.W("警告：未确认远程协助页已就绪，继续尝试复制");

            IdClip = "";
            string idSentinel = "SYSINS_ID_SENTINEL_" + DateTime.Now.Ticks;
            for (int t = 0; t < 4; t++)
            {
                DismissAds(2);
                if (!IdVisible()) { Win.ClickRatio(mainWnd.H, 0.075, 0.156, false, "AweSun远程协助"); Thread.Sleep(2500); DismissAds(2); }
                Win.ClipWrite(idSentinel);
                Win.ClickRatio(mainWnd.H, 0.524, 0.303, false, "AweSun复制识别码");
                for (int i = 0; i < 6; i++) { Thread.Sleep(500); IdClip = Win.ClipRead(); if (Regex.IsMatch(IdClip ?? "", @"\d{10}")) break; }
                if (Regex.IsMatch(IdClip ?? "", @"\d{10}")) break;
                Log.W("识别码未复制，重试 {0}", t + 1);
            }
            CodeClip = "";
            string codeSentinel = "SYSINS_CODE_SENTINEL_" + DateTime.Now.Ticks;
            for (int round = 0; round < 4; round++)
            {
                DismissAds(2);
                if (!IdVisible()) { Win.ClickRatio(mainWnd.H, 0.075, 0.156, false, "AweSun远程协助"); Thread.Sleep(2500); DismissAds(2); }
                var icons = Win.FindAwsCodeIcons(mainWnd.H);
                if (icons == null)
                {
                    Win.ClickRatio(mainWnd.H, 0.716, 0.303, false, "AweSun显示验证码"); Thread.Sleep(1300);
                    if (round == 0) Shot.Screen("awsun_code_shown"); DismissAds(1);
                    icons = Win.FindAwsCodeIcons(mainWnd.H);
                }
                Win.ClipWrite(codeSentinel);
                if (icons != null) { Win.ForceFg(mainWnd.H); Log.W("点击 AweSun复制验证码 动态({0},{1})", icons[1].X, icons[1].Y); Win.ClickPoint(icons[1].X, icons[1].Y); }
                else Win.ClickRatio(mainWnd.H, 0.745, 0.303, false, "AweSun复制验证码");
                for (int i = 0; i < 6; i++) { Thread.Sleep(500); CodeClip = Win.ClipRead(); if ((CodeClip ?? "").Trim() != codeSentinel && (CodeClip ?? "").Trim().Length > 0) break; }
                if ((CodeClip ?? "").Trim() != codeSentinel && (CodeClip ?? "").Trim() != (IdClip ?? "").Trim() && (CodeClip ?? "").Trim().Length > 0) break;
                Log.W("验证码未复制，切换眼睛状态重试 round={0}", round + 1);
                if (icons != null) { Win.ForceFg(mainWnd.H); Log.W("点击 AweSun隐藏验证码 动态({0},{1})", icons[0].X, icons[0].Y); Win.ClickPoint(icons[0].X, icons[0].Y); }
                else Win.ClickRatio(mainWnd.H, 0.716, 0.303, false, "AweSun切回隐藏");
                Thread.Sleep(600);
            }
            Thread.Sleep(500); Shot.Screen("awsun_copied");
            IdClip = (IdClip ?? "").Trim(); CodeClip = (CodeClip ?? "").Trim();
            Log.W("AweSun 识别码: {0}", IdClip); Log.W("AweSun 验证码: {0}", CodeClip);
            if (!Regex.IsMatch(IdClip, @"\d{10}")) { Log.W("警告：识别码未复制成功"); return false; }
            if (CodeClip.Length == 0 || CodeClip == codeSentinel || CodeClip == IdClip) { Log.W("警告：验证码未复制成功"); return false; }
            InfoOk = true; return true;
        }

        public static void Run()
        {
            try
            {
                if (Install() && Login() && SetPassword())
                {
                    Log.W("AweSun 登录设置完成（不复制识别码/验证码，不发邮件）");
                }
            }
            catch (Exception ex) { Log.W("AweSun 流程异常: {0}", ex); Shot.Screen("awsun_exception"); }
        }
    }

    static class Uninstaller
    {
        public static bool SysdiagRemoved;
        static readonly string[] KillList = { "GameViewer", "AweSun", "awesun_guard", "SunloginClient", "OldAweSun", "SunloginClient_Helper",
            "HipsMain", "HipsDaemon", "HipsTray", "HipsLog", "HipsKeeper", "usysdiag", "Sysdiag_setup",
            "sysdiag-all-x64-6.0.12.1-2026.09.22.1", "uuyc_4.42.0", "AweSun_16.6.0.32198" };

        static string[] RegPaths = { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" };

        static bool Match(string name, string kind)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (kind == "uu") return name.IndexOf("UU远程", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("GameViewer", StringComparison.OrdinalIgnoreCase) >= 0;
            if (kind == "aw") return name.IndexOf("向日葵", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("AweSun", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("SunLogin", StringComparison.OrdinalIgnoreCase) >= 0;
            if (kind == "hr") return name.IndexOf("火绒", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Huorong", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Sysdiag", StringComparison.OrdinalIgnoreCase) >= 0;
            return false;
        }

        static IEnumerable<Microsoft.Win32.RegistryKey> Roots() { yield return Microsoft.Win32.Registry.LocalMachine; yield return Microsoft.Win32.Registry.CurrentUser; }

        static void RunUninstallString(string cmd)
        {
            Log.W("执行卸载命令: {0}", cmd);
            try
            {
                var pr = Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = "/c " + cmd, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Cfg.SoftDir });
                pr.WaitForExit(90000);
            }
            catch (Exception ex) { Log.W("卸载命令启动失败: {0}", ex.Message); }
        }

        static void HandleUninstallUi(int seconds)
        {
            var end = DateTime.Now.AddSeconds(seconds);
            string[] okTexts = { "继续卸载", "残忍卸载", "确定", "是", "下一步", "卸载" };
            while (DateTime.Now < end)
            {
                bool acted = false;
                foreach (var w in Win.EnumTop(true))
                {
                    if (w.W < 200 || w.Ht < 100) continue;
                    if (w.Cls == "#32770" || w.Cls.IndexOf("OrayUI", StringComparison.OrdinalIgnoreCase) >= 0
                        || w.Cls.IndexOf("HUORONG", StringComparison.OrdinalIgnoreCase) >= 0
                        || w.Title.IndexOf("卸载", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        foreach (var t in okTexts)
                        {
                            var b = Win.FindButton(w.H, t);
                            if (b != IntPtr.Zero) { Log.W("卸载向导点击: {0}", t); Win.ForceFg(w.H); Win.BmClick(b); acted = true; Thread.Sleep(1500); break; }
                        }
                    }
                }
                if (!acted) Thread.Sleep(1500);
            }
        }

        static void RemoveDir(string p) { if (!Directory.Exists(p)) return; try { Directory.Delete(p, true); Log.W("已删除目录 {0}", p); } catch (Exception ex) { Log.W("删除目录失败 {0}: {1}", p, ex.Message); } }

        public static void Run()
        {
            Log.W("================ 卸载阶段 ================");
            Proc.KillNames(KillList); Thread.Sleep(2000);
            foreach (var kind in new[] { "uu", "aw", "hr" })
                foreach (var root in Roots())
                    foreach (var sub in RegPaths)
                    {
                        Microsoft.Win32.RegistryKey parent; try { parent = root.OpenSubKey(sub); } catch { continue; }
                        if (parent == null) continue;
                        foreach (var name in parent.GetSubKeyNames())
                            try
                            {
                                using (var k = parent.OpenSubKey(name))
                                {
                                    if (k == null) continue; var dn = k.GetValue("DisplayName") as string;
                                    if (!Match(dn, kind)) continue;
                                    var quiet = k.GetValue("QuietUninstallString") as string; var uns = k.GetValue("UninstallString") as string;
                                    Log.W("发现已安装: {0}", dn); string cmd = !string.IsNullOrEmpty(quiet) ? quiet : uns;
                                    if (!string.IsNullOrEmpty(cmd)) { if (cmd.ToLowerInvariant().Contains("unins") && !cmd.Contains("/")) cmd += " /S"; RunUninstallString(cmd); HandleUninstallUi(60); }
                                    if (kind == "hr") { SysdiagRemoved = true; Log.W("sysdiag 已卸载，重装前必须重启系统"); }
                                }
                            }
                            catch { }
                    }
            Proc.Run("sc.exe", "stop AweSunService", 8000); Proc.Run("sc.exe", "delete AweSunService", 8000);
            Proc.Run("sc.exe", "stop SunloginService", 8000); Proc.Run("sc.exe", "delete SunloginService", 8000);
            foreach (var svc in new[] { "HipsDaemon", "HipsKeeper", "HipsService" }) { Proc.Run("sc.exe", "stop " + svc, 5000); Proc.Run("sc.exe", "delete " + svc, 5000); }
            Thread.Sleep(3000); Proc.KillNames(KillList);
            RemoveDir(Cfg.UuDir); RemoveDir(@"C:\Program Files\Oray"); RemoveDir(@"C:\Program Files\Huorong"); Log.W("卸载阶段完成");
        }
    }

    static class Program
    {
        [STAThread]
        static string CheckHuorong()
        {
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    var n = p.ProcessName.ToLowerInvariant();
                    if (n == "hipstray" || n == "hipsmain" || n == "hipsdaemon" || n == "hrconfig") return p.ProcessName + ".exe";
                }
            }
            catch { }
            foreach (var svc in new[] { "HipsDaemon", "sysdiag" })
            {
                try
                {
                    var psi = new ProcessStartInfo("sc.exe", "query " + svc) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
                    using (var pr = Process.Start(psi))
                    {
                        string so = pr.StandardOutput.ReadToEnd();
                        pr.WaitForExit();
                        if (pr.ExitCode == 0 && so.ToLowerInvariant().Contains("running")) return svc;
                    }
                }
                catch { }
            }
            return null;
        }

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                var path = Cfg.Root + @"\Tools\mailkit\" + new System.Reflection.AssemblyName(e.Name).Name + ".dll";
                if (File.Exists(path)) return System.Reflection.Assembly.LoadFrom(path);
                return null;
            };
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
            Win32.SetProcessDPIAware();
            var ts = DateTime.Now.ToString("yyyyMMddHHmmss"); Log.Init(ts); Recorder.Ts = ts;
            string mode = "all";
            foreach (var a in args) { if (a == "-record") Cfg.Record = true; else if (a == "-nomail") Cfg.SendMail = false; else if (a.StartsWith("-")) mode = a.Substring(1); }
            Log.W("================ SysIns C# 自动安装 开始 mode={0} ================", mode);
            Directory.CreateDirectory(Cfg.LogDir);

            if (mode == "resume")
            {
                var pending = Resume.CheckResume();
                if (pending == null) { Log.W("无续跑状态，退出"); return; }
                mode = pending;
            }
            else if (Resume.Pending)
            {
                var pending = Resume.CheckResume();
                if (pending != null) mode = pending;
            }

            if (mode != "uninstall" && mode != "sysdiag")
            {
                var hr = CheckHuorong();
                if (hr != null) { Log.W("检测到火绒安全软件正在运行（{0}），请先手动退出或卸载后再运行本脚本", hr); Environment.Exit(2); }
            }

            if (Cfg.Record) Recorder.Start(IntPtr.Zero, "boot");
            int rc = 0;
            try
            {
                if (mode == "uninstall") Uninstaller.Run();
                else
                {
                    Win.MinimizeDisruptors();
                    if (mode == "sysdiag") RunSysdiagPhase();
                    else if (mode == "sysdiag2") RunPhase2();
                    else if (mode == "phase2") RunPhase2();
                    else if (mode == "uu") FlowUU.Run();
                    else if (mode == "awesun") FlowAweSun.Run();
                    else RunAll();
                }
            }
            catch (Exception ex) { Log.W("主流程异常: {0}", ex); rc = 1; }
            finally
            {
                Mail.Close(); if (Cfg.Record) Recorder.Stop();
                Log.W("================ 结果汇总 ================");
                Log.W("网易UU远程: 安装/登录={0} 识别码={1}", FlowUU.LoginOk, FlowUU.InfoOk);
                Log.W("AweSun:    登录={0} 识别码={1}", FlowAweSun.LoginOk, FlowAweSun.InfoOk);
                Log.W("火绒安全:  按要求跳过（不安装）");
            }
            Thread.Sleep(2000); Environment.Exit(rc);
        }

        static void RunSysdiagPhase()
        {
            Uninstaller.Run();
            if (Uninstaller.SysdiagRemoved)
            {
                Resume.RegisterTask("sysdiag2");
                if (Cfg.Record) Recorder.Stop();
                Resume.Reboot();
                Thread.Sleep(15000); Environment.Exit(0);
            }
        }

        static void RunAll()
        {
            Uninstaller.Run();
            if (Uninstaller.SysdiagRemoved)
            {
                Resume.RegisterTask("phase2");
                if (Cfg.Record) Recorder.Stop();
                Resume.Reboot();
                Thread.Sleep(15000); Environment.Exit(0);
            }
            RunPhase2();
        }

        static void RunPhase2()
        {
            FlowUU.Run(); FlowAweSun.Run();
        }
    }
}
