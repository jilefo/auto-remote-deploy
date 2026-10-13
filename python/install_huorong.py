import ctypes
import os
import sys
import time
import subprocess
from ctypes import wintypes
from collections import deque

user32 = ctypes.windll.user32
gdi32 = ctypes.windll.gdi32
kernel32 = ctypes.windll.kernel32
shell32 = ctypes.windll.shell32

WNDENUMPROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

user32.IsUserAnAdmin = shell32.IsUserAnAdmin
user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]


class RECT(ctypes.Structure):
    _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                ("right", ctypes.c_long), ("bottom", ctypes.c_long)]


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [("biSize", wintypes.DWORD), ("biWidth", ctypes.c_long),
                ("biHeight", ctypes.c_long), ("biPlanes", wintypes.WORD),
                ("biBitCount", wintypes.WORD), ("biCompression", wintypes.DWORD),
                ("biSizeImage", wintypes.DWORD), ("biXPelsPerMeter", ctypes.c_long),
                ("biYPelsPerMeter", ctypes.c_long), ("biClrUsed", wintypes.DWORD),
                ("biClrImportant", wintypes.DWORD)]


class BITMAPINFO(ctypes.Structure):
    _fields_ = [("bmiHeader", BITMAPINFOHEADER), ("bmiColors", wintypes.DWORD * 3)]


class MOUSEINPUT(ctypes.Structure):
    _fields_ = [("dx", ctypes.c_long), ("dy", ctypes.c_long),
                ("mouseData", wintypes.DWORD), ("dwFlags", wintypes.DWORD),
                ("time", wintypes.DWORD), ("dwExtraInfo", ctypes.c_void_p)]


class KEYBDINPUT(ctypes.Structure):
    _fields_ = [("wVk", wintypes.WORD), ("wScan", wintypes.WORD),
                ("dwFlags", wintypes.DWORD), ("time", wintypes.DWORD),
                ("dwExtraInfo", ctypes.c_void_p)]


class INPUT_UNION(ctypes.Union):
    _fields_ = [("mi", MOUSEINPUT), ("ki", KEYBDINPUT)]


class INPUT(ctypes.Structure):
    _fields_ = [("type", wintypes.DWORD), ("u", INPUT_UNION)]


if not shell32.IsUserAnAdmin():
    script = os.path.abspath(sys.argv[0])
    shell32.ShellExecuteW(None, "runas", sys.executable, '"' + script + '"', None, 1)
    sys.exit(0)

ROOT = r"D:\SysIns"
INSTALLER_NAME = "sysdiag-all-x64-6.0.12.1-2026.09.22.1.exe"
installer = os.path.join(ROOT, "Soft", INSTALLER_NAME)
if not os.path.exists(installer):
    ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    installer = os.path.join(ROOT, "Soft", INSTALLER_NAME)

LOG_DIR = os.path.join(ROOT, "Logs")
os.makedirs(LOG_DIR, exist_ok=True)
LOG_FILE = os.path.join(LOG_DIR, "InstallHuorong-python-" + time.strftime("%Y%m%d%H%M%S") + ".log")

P1 = r"C:\Program Files\Huorong\Sysdiag\bin\HipsMain.exe"
P2 = r"C:\Program Files\Huorong\Sysdiag\bin\HipsDaemon.exe"


def log(msg):
    line = time.strftime("%H:%M:%S  ") + str(msg)
    try:
        print(line)
    except Exception:
        pass
    with open(LOG_FILE, "a", encoding="utf-8") as f:
        f.write(line + "\r\n")


def shot(name):
    try:
        w = user32.GetSystemMetrics(0)
        h = user32.GetSystemMetrics(1)
        data = grab_region(0, 0, w, h)
        if data:
            save_bmp(os.path.join(LOG_DIR, name + ".bmp"), w, h, data)
    except Exception:
        pass


def save_bmp(path, w, h, data):
    file_header_size = 14
    info_header_size = 40
    stride = w * 4
    pixel_offset = file_header_size + info_header_size
    total = pixel_offset + stride * h
    buf = bytearray(total)
    buf[0:2] = b"BM"
    buf[2:6] = total.to_bytes(4, "little")
    buf[10:14] = pixel_offset.to_bytes(4, "little")
    buf[14:18] = info_header_size.to_bytes(4, "little")
    buf[18:22] = w.to_bytes(4, "little", signed=True)
    buf[22:26] = (h * 2).to_bytes(4, "little", signed=True)
    buf[26:28] = (1).to_bytes(2, "little")
    buf[28:30] = (32).to_bytes(2, "little")
    pos = pixel_offset
    for y in range(h):
        buf[pos:pos + stride] = bytes(data[y * stride:(y + 1) * stride])
        pos += stride
    with open(path, "wb") as f:
        f.write(buf)


def grab_region(x, y, w, h):
    hdc_screen = user32.GetDC(0)
    hdc_mem = gdi32.CreateCompatibleDC(hdc_screen)
    hbmp = gdi32.CreateCompatibleBitmap(hdc_screen, w, h)
    gdi32.SelectObject(hdc_mem, hbmp)
    gdi32.BitBlt(hdc_mem, 0, 0, w, h, hdc_screen, x, y, 0x00CC0020)

    bmi = BITMAPINFO()
    bmi.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    bmi.bmiHeader.biWidth = w
    bmi.bmiHeader.biHeight = -h
    bmi.bmiHeader.biPlanes = 1
    bmi.bmiHeader.biBitCount = 32
    buf = (ctypes.c_ubyte * (w * h * 4))()
    got = gdi32.GetDIBits(hdc_mem, hbmp, 0, h, buf, ctypes.byref(bmi), 0)

    gdi32.DeleteObject(hbmp)
    gdi32.DeleteDC(hdc_mem)
    user32.ReleaseDC(0, hdc_screen)
    if got == 0:
        return None
    return buf


def get_rect(hwnd):
    r = RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(r))
    return r.left, r.top, r.right - r.left, r.bottom - r.top


def force_fg(hwnd):
    if user32.IsIconic(hwnd):
        user32.ShowWindow(hwnd, 9)
        time.sleep(0.2)
    pid = wintypes.DWORD()
    user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    self_tid = kernel32.GetCurrentThreadId()
    user32.AttachThreadInput(self_tid, pid.value, True)
    HWND_TOPMOST = ctypes.c_void_p(-1)
    HWND_NOTOPMOST = ctypes.c_void_p(-2)
    user32.SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, 0x43)
    user32.SetForegroundWindow(hwnd)
    time.sleep(0.4)
    user32.AttachThreadInput(self_tid, pid.value, False)
    user32.SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, 0x43)


def find_hr_window(timeout_ms=0):
    start = kernel32.GetTickCount()
    found = []

    def cb(hwnd, lparam):
        if user32.IsWindowVisible(hwnd):
            cls = ctypes.create_unicode_buffer(256)
            user32.GetClassNameW(hwnd, cls, 256)
            if "HUORONGINSTALLERWND" in cls.value:
                x, y, w, h = get_rect(hwnd)
                if w >= 500 and h >= 300:
                    found.append(hwnd)
                    return False
        return True

    proc = WNDENUMPROC(cb)
    while True:
        found.clear()
        user32.EnumWindows(proc, 0)
        if found:
            return found[0]
        if timeout_ms and kernel32.GetTickCount() - start > timeout_ms:
            return None
        time.sleep(0.8)


def is_orange(r, g, b):
    return 200 <= r <= 255 and 95 <= g <= 170 and 0 <= b <= 80


def is_blue(r, g, b):
    return b >= 100 and r <= 130 and g <= 150 and b > r + 20 and b > g + 20


def find_buttons(hwnd):
    x0, y0, w, h = get_rect(hwnd)
    if w < 10 or h < 10:
        return []
    data = grab_region(x0, y0, w, h)
    if not data:
        return []

    seen = bytearray(w * h)
    result = []
    for y in range(h):
        row = y * w
        for x in range(w):
            idx = row + x
            if seen[idx]:
                continue
            p = idx * 4
            r = data[p + 2]
            g = data[p + 1]
            b = data[p]
            if not (is_orange(r, g, b) or is_blue(r, g, b)):
                continue
            st = deque([idx])
            seen[idx] = 1
            cnt = 0
            min_x = max_x = x
            min_y = max_y = y
            while st:
                cur = st.popleft()
                cnt += 1
                cx = cur % w
                cy = cur // w
                if cx < min_x:
                    min_x = cx
                if cx > max_x:
                    max_x = cx
                if cy < min_y:
                    min_y = cy
                if cy > max_y:
                    max_y = cy
                for ddy in (-1, 0, 1):
                    ny = cy + ddy
                    if ny < 0 or ny >= h:
                        continue
                    nrow = ny * w
                    for ddx in (-1, 0, 1):
                        nx = cx + ddx
                        if nx < 0 or nx >= w:
                            continue
                        ni = nrow + nx
                        if seen[ni]:
                            continue
                        q = ni * 4
                        if is_orange(data[q + 2], data[q + 1], data[q]) or is_blue(data[q + 2], data[q + 1], data[q]):
                            seen[ni] = 1
                            st.append(ni)
            bw = max_x - min_x + 1
            bh = max_y - min_y + 1
            if 80 <= bw <= 500 and 25 <= bh <= 100 and cnt > bw * bh * 0.4:
                result.append((min_x, min_y, bw, bh))
    return result


def click_at(x, y):
    sm_x = user32.GetSystemMetrics(0)
    sm_y = user32.GetSystemMetrics(1)
    sx = int(x * 65535 / (sm_x - 1))
    sy = int(y * 65535 / (sm_y - 1))

    def send(flags):
        inp = INPUT()
        inp.type = 0
        inp.u.mi.dx = sx
        inp.u.mi.dy = sy
        inp.u.mi.dwFlags = 0x8000 | flags
        return user32.SendInput(1, ctypes.byref(inp), ctypes.sizeof(inp))

    r1 = send(0x0001)
    time.sleep(0.12)
    r2 = send(0x0002)
    time.sleep(0.08)
    r3 = send(0x0004)
    return r1 and r2 and r3


def get_children(parent):
    items = []

    def cb(hwnd, lparam):
        cls = ctypes.create_unicode_buffer(256)
        user32.GetClassNameW(hwnd, cls, 256)
        txt = ctypes.create_unicode_buffer(256)
        user32.GetWindowTextW(hwnd, txt, 256)
        x, y, w, h = get_rect(hwnd)
        items.append((hwnd, cls.value, txt.value, x, y, w, h))
        return True

    proc = WNDENUMPROC(cb)
    user32.EnumChildWindows(parent, proc, 0)
    return items


def service_exists():
    try:
        r = subprocess.run(["sc.exe", "query", "HipsDaemon"], capture_output=True,
                           text=True, encoding="gbk", errors="ignore",
                           timeout=8, creationflags=0x08000000)
        return "SERVICE_NAME" in r.stdout
    except Exception:
        return False


def main():
    user32.SetProcessDPIAware()
    log("================ 火绒安全自动安装(Python) 开始 ================")
    log("安装包: " + installer)

    if os.path.exists(P1):
        log("火绒安全已安装，无需重复安装")
        return 0
    if service_exists():
        log("火绒安全已安装，无需重复安装")
        return 0

    subprocess.run(["taskkill.exe", "/f", "/im", INSTALLER_NAME],
                   capture_output=True, creationflags=0x08000000)

    os.startfile(installer)

    hwnd = find_hr_window(90000)
    if not hwnd:
        log("错误：找不到火绒安装窗口")
        shot("huorong_no_window_python")
        return 1
    log("安装窗口已出现")
    time.sleep(3)

    best = None
    scan_start = kernel32.GetTickCount()
    while kernel32.GetTickCount() - scan_start < 30000:
        force_fg(hwnd)
        buttons = find_buttons(hwnd)
        if buttons:
            best = max(buttons, key=lambda b: b[2])
            log("检测到安装按钮: %dx%d" % (best[2], best[3]))
            break
        time.sleep(3)

    if not best:
        log("错误：未检测到极速安装按钮")
        shot("huorong_no_button_python")
        return 1

    wx, wy, ww, wh = get_rect(hwnd)
    cx = int(wx + best[0] + best[2] / 2)
    cy = int(wy + best[1] + best[3] / 2)
    force_fg(hwnd)
    click_at(cx, cy)
    log("已点击极速安装，等待安装完成...")
    shot("huorong_clicked_python")

    ok = False
    deadline = kernel32.GetTickCount() + 300000
    while kernel32.GetTickCount() < deadline:
        time.sleep(5)
        if os.path.exists(P1) and os.path.exists(P2):
            ok = True
            break

    if not ok:
        log("错误：安装超时，未检测到火绒主程序")
        shot("huorong_timeout_python")
        return 1
    log("检测到火绒主程序文件")
    time.sleep(15)

    subprocess.run(["taskkill.exe", "/f", "/im", INSTALLER_NAME],
                   capture_output=True, creationflags=0x08000000)
    time.sleep(3)

    done = find_hr_window(8000)
    if done:
        log("检测到安装完成窗口，点击完成并关闭")
        btn_center = None
        for ch_hwnd, ch_cls, ch_txt, ch_x, ch_y, ch_w, ch_h in get_children(done):
            if "Button" in ch_cls and ("完成" in ch_txt or "确定" in ch_txt):
                btn_center = (ch_x + ch_w / 2, ch_y + ch_h / 2)
                break
        if not btn_center:
            force_fg(done)
            buttons2 = find_buttons(done)
            if buttons2:
                fb = max(buttons2, key=lambda b: b[1])
                dx, dy, dw, dh = get_rect(done)
                btn_center = (dx + fb[0] + fb[2] / 2, dy + fb[1] + fb[3] / 2)
                log("像素扫描定位完成按钮: %dx%d" % (fb[2], fb[3]))
        if btn_center:
            force_fg(done)
            click_at(int(btn_center[0]), int(btn_center[1]))
        else:
            log("警告：未定位到完成按钮，发送 WM_CLOSE")
        time.sleep(2)
        user32.PostMessageW(done, 0x0010, 0, 0)
        time.sleep(2)
        if not find_hr_window(2000):
            log("安装完成窗口已关闭")
        else:
            user32.PostMessageW(done, 0x0010, 0, 0)
            time.sleep(2)

    shot("huorong_done_python")
    if os.path.exists(P1) and os.path.exists(P2):
        log("火绒安全安装成功")
        return 0
    log("警告：主程序文件缺失，请检查")
    return 1


if __name__ == "__main__":
    try:
        rc = main()
    except Exception as ex:
        log("[异常] %s: %s" % (type(ex).__name__, ex))
        rc = 2
    sys.exit(rc)
