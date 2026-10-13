using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal class InstallHuorong
{
    const string InstallerName = "sysdiag-all-x64-6.0.12.1-2026.09.22.1.exe";
    const string P1 = @"C:\Program Files\Huorong\Sysdiag\bin\HipsMain.exe";
    const string P2 = @"C:\Program Files\Huorong\Sysdiag\bin\HipsDaemon.exe";

    static string _root;
    static string _installer;
    static string _logDir;
    static string _logFile;

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lp);
    delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lp);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32.dll")] static extern IntPtr PostMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int idx);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int L, T, R, B; }

    [StructLayout(LayoutKind.Explicit)]
    struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint data, flags, time; public IntPtr extra; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);

    static void LOG(string s)
    {
        string line = DateTime.Now.ToString("HH:mm:ss") + "  " + s;
        Console.WriteLine(line);
        try { File.AppendAllText(_logFile, line + "\r\n", Encoding.UTF8); } catch { }
    }
    static void LOGF(string fmt, params object[] args) { LOG(string.Format(fmt, args)); }

    static void SHOT(string name)
    {
        try
        {
            Rectangle b = Screen.PrimaryScreen.Bounds;
            using (Bitmap bmp = new Bitmap(b.Width, b.Height))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(0, 0, 0, 0, bmp.Size);
                bmp.Save(Path.Combine(_logDir, name + ".png"), ImageFormat.Png);
            }
        }
        catch { }
    }

    static void ForceFg(IntPtr h)
    {
        if (IsIconic(h)) { ShowWindow(h, 9); Thread.Sleep(200); }
        uint tid; GetWindowThreadProcessId(h, out tid);
        uint self = GetCurrentThreadId();
        AttachThreadInput(self, tid, true);
        SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x43);
        SetForegroundWindow(h);
        Thread.Sleep(400);
        AttachThreadInput(self, tid, false);
        SetWindowPos(h, new IntPtr(-2), 0, 0, 0, 0, 0x43);
    }

    static IntPtr FindHrWindow(int timeoutMs)
    {
        int start = Environment.TickCount;
        do
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h)) return true;
                StringBuilder c = new StringBuilder(256);
                GetClassName(h, c, 256);
                if (c.ToString().Contains("HUORONGINSTALLERWND"))
                {
                    RECT r;
                    GetWindowRect(h, out r);
                    if (r.R - r.L >= 500 && r.B - r.T >= 300) { found = h; return false; }
                }
                return true;
            }, IntPtr.Zero);
            if (found != IntPtr.Zero) return found;
            if (timeoutMs > 0 && Environment.TickCount - start > timeoutMs) return IntPtr.Zero;
            Thread.Sleep(800);
        } while (true);
    }

    static bool TestOrange(int r, int g, int b)
    {
        return r >= 200 && r <= 255 && g >= 95 && g <= 170 && b >= 0 && b <= 80;
    }

    static bool TestBlue(int r, int g, int b)
    {
        return b >= 100 && r <= 130 && g <= 150 && b > r + 20 && b > g + 20;
    }

    class BtnRect { public int X, Y, W, H; }

    static List<BtnRect> FindButtons(IntPtr hwnd)
    {
        RECT rr;
        GetWindowRect(hwnd, out rr);
        int W = rr.R - rr.L;
        int H = rr.B - rr.T;
        List<BtnRect> result = new List<BtnRect>();
        if (W < 10 || H < 10) return result;

        using (Bitmap bmp = new Bitmap(W, H))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(rr.L, rr.T, 0, 0, new Size(W, H));
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int n = W * H;
            byte[] buf = new byte[n * 4];
            Marshal.Copy(bd.Scan0, buf, 0, n * 4);
            bmp.UnlockBits(bd);

            byte[] seen = new byte[n];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    if (seen[i] == 1) continue;
                    int p = i * 4;
                    bool isBtn = TestOrange(buf[p + 2], buf[p + 1], buf[p]) || TestBlue(buf[p + 2], buf[p + 1], buf[p]);
                    if (!isBtn) continue;
                    Stack<int> st = new Stack<int>();
                    st.Push(i);
                    seen[i] = 1;
                    int cnt = 0;
                    int minX = x, maxX = x, minY = y, maxY = y;
                    while (st.Count > 0)
                    {
                        int cur = st.Pop();
                        cnt++;
                        int cx = cur % W;
                        int cy = cur / W;
                        if (cx < minX) minX = cx;
                        if (cx > maxX) maxX = cx;
                        if (cy < minY) minY = cy;
                        if (cy > maxY) maxY = cy;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int ny = cy + dy;
                            if (ny < 0 || ny >= H) continue;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = cx + dx;
                                if (nx < 0 || nx >= W) continue;
                                int ni = ny * W + nx;
                                if (seen[ni] == 1) continue;
                                int q = ni * 4;
                                if (TestOrange(buf[q + 2], buf[q + 1], buf[q]) || TestBlue(buf[q + 2], buf[q + 1], buf[q]))
                                {
                                    seen[ni] = 1;
                                    st.Push(ni);
                                }
                            }
                        }
                    }
                    int bw = maxX - minX + 1;
                    int bh = maxY - minY + 1;
                    if (bw >= 80 && bw <= 500 && bh >= 25 && bh <= 100 && cnt > bw * bh * 0.4)
                    {
                        result.Add(new BtnRect { X = minX, Y = minY, W = bw, H = bh });
                    }
                }
            }
        }
        return result;
    }

    static bool ClickAt(int x, int y)
    {
        int sx = (x * 65535) / (GetSystemMetrics(0) - 1);
        int sy = (y * 65535) / (GetSystemMetrics(1) - 1);
        INPUT inp = new INPUT();
        inp.type = 0;
        inp.mi.dx = sx;
        inp.mi.dy = sy;
        inp.mi.flags = 0x8000 | 0x0001;
        uint r1 = SendInput(1, new INPUT[] { inp }, Marshal.SizeOf(typeof(INPUT)));
        Thread.Sleep(120);
        inp.mi.flags = 0x8000 | 0x0002;
        uint r2 = SendInput(1, new INPUT[] { inp }, Marshal.SizeOf(typeof(INPUT)));
        Thread.Sleep(80);
        inp.mi.flags = 0x8000 | 0x0004;
        uint r3 = SendInput(1, new INPUT[] { inp }, Marshal.SizeOf(typeof(INPUT)));
        return r1 > 0 && r2 > 0 && r3 > 0;
    }

    class ChildInfo { public IntPtr H; public string Text; public string Cls; public RECT R; }

    static List<ChildInfo> GetChildren(IntPtr parent)
    {
        List<ChildInfo> list = new List<ChildInfo>();
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l)
        {
            StringBuilder t = new StringBuilder(256);
            GetWindowText(h, t, 256);
            StringBuilder c = new StringBuilder(256);
            GetClassName(h, c, 256);
            RECT r;
            GetWindowRect(h, out r);
            list.Add(new ChildInfo { H = h, Text = t.ToString(), Cls = c.ToString(), R = r });
            return true;
        }, IntPtr.Zero);
        return list;
    }

    [STAThread]
    static int Main()
    {
        try
        {
            WindowsPrincipal wp = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            if (!wp.IsInRole(WindowsBuiltInRole.Administrator))
            {
                ProcessStartInfo si = new ProcessStartInfo();
                si.FileName = Application.ExecutablePath;
                si.UseShellExecute = true;
                si.Verb = "runas";
                try { Process.Start(si); } catch { return 2; }
                return 0;
            }

            _root = @"D:\SysIns";
            _installer = Path.Combine(_root, "Soft", InstallerName);
            if (!File.Exists(_installer))
            {
                _root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
                _installer = Path.Combine(_root, "Soft", InstallerName);
            }
            _logDir = Path.Combine(_root, "Logs");
            Directory.CreateDirectory(_logDir);
            _logFile = Path.Combine(_logDir, "InstallHuorong-csharp-" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".log");

            SetProcessDPIAware();

            LOG("================ 火绒安全自动安装(C#) 开始 ================");
            LOG("安装包: " + _installer);

            if (File.Exists(P1))
            {
                LOG("火绒安全已安装，无需重复安装");
                return 0;
            }
            bool svcExist = false;
            try
            {
                using (Process sc = new Process())
                {
                    sc.StartInfo.FileName = "sc.exe";
                    sc.StartInfo.Arguments = "query HipsDaemon";
                    sc.StartInfo.UseShellExecute = false;
                    sc.StartInfo.RedirectStandardOutput = true;
                    sc.StartInfo.CreateNoWindow = true;
                    sc.Start();
                    string outp = sc.StandardOutput.ReadToEnd();
                    sc.WaitForExit(5000);
                    if (outp.Contains("SERVICE_NAME")) svcExist = true;
                }
            }
            catch { }
            if (svcExist)
            {
                LOG("火绒安全已安装，无需重复安装");
                return 0;
            }

            ProcessStartInfo kill = new ProcessStartInfo();
            kill.FileName = "taskkill.exe";
            kill.Arguments = "/f /im " + InstallerName;
            kill.UseShellExecute = false;
            kill.CreateNoWindow = true;
            try { Process.Start(kill).WaitForExit(3000); } catch { }

            Process.Start(_installer);

            IntPtr hwnd = FindHrWindow(90000);
            if (hwnd == IntPtr.Zero)
            {
                LOG("错误：找不到火绒安装窗口");
                SHOT("huorong_no_window_csharp");
                return 1;
            }
            LOG("安装窗口已出现");
            Thread.Sleep(3000);

            BtnRect best = null;
            int scanStart = Environment.TickCount;
            while (Environment.TickCount - scanStart < 30000)
            {
                ForceFg(hwnd);
                List<BtnRect> btns = FindButtons(hwnd);
                if (btns.Count > 0)
                {
                    foreach (BtnRect b in btns)
                    {
                        if (best == null || b.W > best.W) best = b;
                    }
                    LOGF("检测到安装按钮: {0}x{1}", best.W, best.H);
                    break;
                }
                Thread.Sleep(3000);
            }

            if (best == null)
            {
                LOG("错误：未检测到极速安装按钮");
                SHOT("huorong_no_button_csharp");
                return 1;
            }

            RECT wr;
            GetWindowRect(hwnd, out wr);
            int cx = wr.L + best.X + best.W / 2;
            int cy = wr.T + best.Y + best.H / 2;
            ForceFg(hwnd);
            ClickAt(cx, cy);
            LOG("已点击极速安装，等待安装完成...");
            SHOT("huorong_clicked_csharp");

            bool ok = false;
            int deadline = Environment.TickCount + 300000;
            while (Environment.TickCount < deadline)
            {
                Thread.Sleep(5000);
                if (File.Exists(P1) && File.Exists(P2)) { ok = true; break; }
            }

            if (!ok)
            {
                LOG("错误：安装超时，未检测到火绒主程序");
                SHOT("huorong_timeout_csharp");
                return 1;
            }
            LOG("检测到火绒主程序文件");
            Thread.Sleep(15000);

            ProcessStartInfo kill2 = new ProcessStartInfo();
            kill2.FileName = "taskkill.exe";
            kill2.Arguments = "/f /im " + InstallerName;
            kill2.UseShellExecute = false;
            kill2.CreateNoWindow = true;
            try { Process.Start(kill2).WaitForExit(3000); } catch { }
            Thread.Sleep(3000);

            IntPtr done = FindHrWindow(8000);
            if (done != IntPtr.Zero)
            {
                LOG("检测到安装完成窗口，点击完成并关闭");
                double[] btnCenter = null;
                foreach (ChildInfo ch in GetChildren(done))
                {
                    if (ch.Cls.Contains("Button") && (ch.Text.Contains("完成") || ch.Text.Contains("确定")))
                    {
                        btnCenter = new double[] { ch.R.L + (ch.R.R - ch.R.L) / 2.0, ch.R.T + (ch.R.B - ch.R.T) / 2.0 };
                        break;
                    }
                }
                if (btnCenter == null)
                {
                    ForceFg(done);
                    List<BtnRect> btns2 = FindButtons(done);
                    if (btns2.Count > 0)
                    {
                        BtnRect fb = btns2[0];
                        foreach (BtnRect b in btns2) { if (b.Y > fb.Y) fb = b; }
                        RECT dr;
                        GetWindowRect(done, out dr);
                        btnCenter = new double[] { dr.L + fb.X + fb.W / 2.0, dr.T + fb.Y + fb.H / 2.0 };
                        LOGF("像素扫描定位完成按钮: {0}x{1}", fb.W, fb.H);
                    }
                }
                if (btnCenter != null)
                {
                    ForceFg(done);
                    ClickAt((int)btnCenter[0], (int)btnCenter[1]);
                }
                else
                {
                    LOG("警告：未定位到完成按钮，发送 WM_CLOSE");
                }
                Thread.Sleep(2000);
                PostMessage(done, 0x0010, IntPtr.Zero, IntPtr.Zero);
                Thread.Sleep(2000);
                if (FindHrWindow(2000) == IntPtr.Zero)
                {
                    LOG("安装完成窗口已关闭");
                }
                else
                {
                    PostMessage(done, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    Thread.Sleep(2000);
                }
            }

            SHOT("huorong_done_csharp");
            if (File.Exists(P1) && File.Exists(P2))
            {
                LOG("火绒安全安装成功");
                return 0;
            }
            LOG("警告：主程序文件缺失，请检查");
            return 1;
        }
        catch (Exception ex)
        {
            string err = "[异常] " + ex.GetType().Name + ": " + ex.Message + " 位置: " + ex.StackTrace;
            Console.WriteLine(err);
            try { File.AppendAllText(_logFile ?? "InstallHuorong-error.log", err + "\r\n", Encoding.UTF8); } catch { }
            return 2;
        }
    }
}
