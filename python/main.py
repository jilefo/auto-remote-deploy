#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
AutoInstall_Python - Python版自动安装脚本
支持 uuyc / AweSun 自动安装、登录、复制识别码、发送邮件；火绒按要求不安装（仅检测报警）
"""

import os
import sys
import time
import subprocess
import re
import base64
import imaplib
import email
import smtplib
import win32gui
import win32con
import win32api
import win32clipboard
import win32process
import win32ui
from datetime import datetime
from email.mime.text import MIMEText
from email.mime.multipart import MIMEMultipart
from email.mime.base import MIMEBase
from email import encoders
import pyautogui
import pyscreeze
from PIL import Image

pyautogui.FAILSAFE = False
pyautogui.PAUSE = 0.5

# 从外部配置文件加载（config.py 不纳入版本控制）
import importlib.util, os
_cfg_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'config.py')
if not os.path.exists(_cfg_path):
    print('错误：配置文件 config.py 不存在。')
    print('请复制 config.example.py 为 config.py 并填写实际配置后再运行。')
    sys.exit(1)
_spec = importlib.util.spec_from_file_location('config', _cfg_path)
_mod = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_mod)
CFG = _mod.CFG

LOG_FILE = ''
def log(msg):
    line = datetime.now().strftime('%H:%M:%S') + '  ' + msg
    print(line)
    try:
        with open(LOG_FILE, 'a', encoding='utf-8') as f:
            f.write(line + '\n')
    except: pass

def ensure_dir(p):
    if not os.path.exists(p):
        os.makedirs(p)

def screenshot(name):
    path = os.path.join(CFG['log_dir'], name + '.png')
    try:
        img = pyautogui.screenshot()
        img.save(path)
    except Exception as e:
        log(f'截图失败 {name}: {e}')
    return path

REC_PROC = None
REC_TS = None

def _build_rec_args(hwnd, path):
    geo = []
    if hwnd:
        try:
            r = win32gui.GetWindowRect(hwnd)
            w, h = r[2] - r[0], r[3] - r[1]
            if w >= 100 and h >= 100:
                w &= ~1
                h &= ~1
                geo = ['-offset_x', str(r[0]), '-offset_y', str(r[1]), '-video_size', f'{w}x{h}']
                log(f'录屏区域: {r[0]},{r[1]} {w}x{h}')
        except Exception as e:
            log(f'获取录屏窗口区域失败: {e}')
    return [CFG['ffmpeg'], '-y', '-threads', '2', '-thread_queue_size', '64',
            '-loglevel', 'error', '-f', 'gdigrab', '-framerate', '5',
            '-draw_mouse', '1'] + geo + ['-i', 'desktop', '-pix_fmt', 'yuv420p', '-an', path]

def _rec_launch(base_cmd, path):
    try:
        p = subprocess.Popen(base_cmd, creationflags=subprocess.CREATE_NO_WINDOW,
                             stdin=subprocess.PIPE)
        time.sleep(1.5)
        if p.poll() is not None:
            log('录屏进程立即退出')
            return None
        log(f'录屏开始 -> {path}')
        return p
    except Exception as e:
        log(f'录屏启动失败: {e}')
        return None

def start_recording(hwnd, tag):
    global REC_PROC
    if not CFG['record']:
        return None
    path = os.path.join(CFG['log_dir'], f'AutoInstallRec-{REC_TS}-{tag}_{CFG["lang"]}.mkv')
    base = _build_rec_args(hwnd, path)
    idx = base.index('-pix_fmt')
    REC_PROC = _rec_launch(base[:idx] + ['-c:v', 'libx264', '-preset', 'ultrafast', '-crf', '26'] + base[idx:], path)
    return REC_PROC

def stop_recording(proc=None):
    global REC_PROC
    if REC_PROC:
        try:
            if REC_PROC.poll() is None and REC_PROC.stdin:
                try:
                    REC_PROC.stdin.write(b'q')
                    REC_PROC.stdin.flush()
                except Exception:
                    pass
            try:
                REC_PROC.wait(timeout=8)
            except Exception:
                subprocess.run(['taskkill', '/f', '/im', 'ffmpeg.exe'],
                              capture_output=True, creationflags=subprocess.CREATE_NO_WINDOW)
            time.sleep(0.5)
            log('录屏结束')
        except: pass
        REC_PROC = None

def switch_recording(hwnd, tag):
    if not CFG['record']:
        return
    stop_recording()
    start_recording(hwnd, tag)

def resume_state_path():
    return os.path.join(CFG['log_dir'], f'resume_{CFG["lang"]}.txt')

TASK_NAME = 'SysInsAutoInstall_' + CFG['lang']

def get_tick64():
    import ctypes
    return ctypes.windll.kernel32.GetTickCount64()

def resume_save(mode, tick):
    ensure_dir(CFG['log_dir'])
    with open(resume_state_path(), 'w') as f:
        f.write(mode + '\n' + str(tick))
    log(f'续跑状态已保存: {mode} tick={tick}')

def resume_read_mode():
    try:
        with open(resume_state_path()) as f:
            return f.readline().strip()
    except:
        return None

def resume_read_tick():
    try:
        with open(resume_state_path()) as f:
            f.readline()
            return int(f.readline().strip())
    except:
        return 0

def resume_clear():
    if os.path.exists(resume_state_path()):
        os.remove(resume_state_path())

def resume_register_task(mode):
    exe = sys.executable
    script = os.path.abspath(__file__)
    tr = '\\"' + exe + '\\" \\"' + script + '\\" -resume'
    r = subprocess.run(['schtasks', '/create', '/tn', TASK_NAME, '/tr', tr,
                        '/sc', 'onlogon', '/rl', 'highest', '/f'],
                       capture_output=True, creationflags=subprocess.CREATE_NO_WINDOW)
    log(f'计划任务创建 rc={r.returncode}: {TASK_NAME}')
    resume_save(mode, get_tick64())

def resume_remove_task():
    subprocess.run(['schtasks', '/delete', '/tn', TASK_NAME, '/f'],
                   capture_output=True, creationflags=subprocess.CREATE_NO_WINDOW)

def check_resume():
    mode = resume_read_mode()
    if not mode:
        return None
    now_tick = get_tick64()
    saved_tick = resume_read_tick()
    if now_tick < saved_tick:
        log(f'重启已确认（tick {now_tick} < {saved_tick}），续跑: {mode}')
        resume_remove_task()
        resume_clear()
        return mode
    log(f'续跑在关机过程中被触发（tick {now_tick} >= {saved_tick}），不执行任何操作，等待真正重启')
    sys.exit(3)

def do_reboot():
    log('系统即将重启（卸载sysdiag后必须重启才能重装）...')
    subprocess.Popen(['shutdown', '/r', '/t', '10', '/c', 'SysIns: sysdiag已卸载，重启后继续自动安装'],
                    creationflags=subprocess.CREATE_NO_WINDOW)

def kill_processes(names):
    for name in names:
        try:
            result = subprocess.run(['taskkill', '/f', '/im', name + '.exe'],
                                   capture_output=True, creationflags=subprocess.CREATE_NO_WINDOW)
            if result.returncode == 0:
                log(f'结束进程 {name}')
        except: pass

def run_cmd(file, args='', wait_ms=0):
    try:
        p = subprocess.Popen([file] + (args.split() if args else []),
                            creationflags=subprocess.CREATE_NO_WINDOW)
        if wait_ms > 0:
            p.wait(timeout=wait_ms/1000)
    except Exception as e:
        log(f'命令失败 {file}: {e}')

def start_exe(path):
    try:
        subprocess.Popen([path], cwd=os.path.dirname(path))
    except Exception as e:
        log(f'启动失败 {path}: {e}')

def enum_windows():
    def callback(hwnd, windows):
        if win32gui.IsWindowVisible(hwnd):
            text = win32gui.GetWindowText(hwnd)
            cls = win32gui.GetClassName(hwnd)
            rect = win32gui.GetWindowRect(hwnd)
            w = rect[2] - rect[0]
            h = rect[3] - rect[1]
            _, pid = win32process.GetWindowThreadProcessId(hwnd)
            windows.append({'hwnd': hwnd, 'text': text, 'cls': cls, 'rect': rect, 'w': w, 'h': h, 'pid': pid})
        return True
    windows = []
    win32gui.EnumWindows(callback, windows)
    return windows

def find_window(pred, timeout_ms=0):
    start = time.time() * 1000
    while True:
        for w in enum_windows():
            if pred(w):
                return w
        if timeout_ms > 0 and (time.time() * 1000 - start) > timeout_ms:
            return None
        time.sleep(0.8)

def find_window_by_title(title_part, min_w=0, min_h=0, timeout_ms=0):
    return find_window(lambda w: title_part.lower() in w['text'].lower() and w['w'] >= min_w and w['h'] >= min_h, timeout_ms)

def find_window_by_class(cls_part, min_w=0, min_h=0, timeout_ms=0):
    return find_window(lambda w: cls_part.lower() in w['cls'].lower() and w['w'] >= min_w and w['h'] >= min_h, timeout_ms)

def force_foreground(hwnd):
    if not hwnd or not win32gui.IsWindow(hwnd):
        return False
    for i in range(5):
        if win32gui.IsIconic(hwnd):
            win32gui.ShowWindow(hwnd, win32con.SW_RESTORE)
        fg = win32gui.GetForegroundWindow()
        fg_tid, _ = win32process.GetWindowThreadProcessId(fg)
        my_tid = win32api.GetCurrentThreadId()
        if fg_tid != my_tid:
            win32process.AttachThreadInput(my_tid, fg_tid, True)
        win32gui.ShowWindow(hwnd, win32con.SW_SHOW)
        win32gui.BringWindowToTop(hwnd)
        win32gui.SetWindowPos(hwnd, win32con.HWND_TOPMOST, 0,0,0,0, win32con.SWP_NOMOVE | win32con.SWP_NOSIZE)
        win32gui.SetForegroundWindow(hwnd)
        win32gui.SetWindowPos(hwnd, win32con.HWND_NOTOPMOST, 0,0,0,0, win32con.SWP_NOMOVE | win32con.SWP_NOSIZE)
        if fg_tid != my_tid:
            win32process.AttachThreadInput(my_tid, fg_tid, False)
        time.sleep(0.35)
        if win32gui.GetForegroundWindow() == hwnd:
            return True
        time.sleep(0.2)
    log('警告：窗口置前失败')
    return False

def click_point(x, y):
    pyautogui.moveTo(x, y, duration=0.1)
    pyautogui.click()
    time.sleep(0.3)

def click_ratio(hwnd, rx, ry, tag=''):
    if not hwnd or not win32gui.IsWindow(hwnd):
        log(f'点击中止：窗口无效 {tag}')
        return None
    force_foreground(hwnd)
    rect = win32gui.GetWindowRect(hwnd)
    x = int(rect[0] + (rect[2] - rect[0]) * rx)
    y = int(rect[1] + (rect[3] - rect[1]) * ry)
    log(f'点击 {tag} ({x},{y})')
    click_point(x, y)
    return (x, y)

def type_text(text):
    pyautogui.typewrite(text, interval=0.05)
    time.sleep(0.3)

def clip_read():
    for i in range(6):
        try:
            win32clipboard.OpenClipboard()
            data = win32clipboard.GetClipboardData(win32con.CF_UNICODETEXT)
            win32clipboard.CloseClipboard()
            return data
        except:
            try: win32clipboard.CloseClipboard()
            except: pass
            time.sleep(0.25)
    return ''

def clip_set(text):
    try:
        win32clipboard.OpenClipboard()
        win32clipboard.EmptyClipboard()
        win32clipboard.SetClipboardText(text, win32con.CF_UNICODETEXT)
        win32clipboard.CloseClipboard()
    except:
        try: win32clipboard.CloseClipboard()
        except: pass

def find_button(hwnd, text_part):
    def enum_child(hwnd_child, param):
        cls = win32gui.GetClassName(hwnd_child)
        if 'button' not in cls.lower():
            return True
        text = win32gui.GetWindowText(hwnd_child)
        if text_part.lower() in text.lower():
            param[0] = hwnd_child
            return False
        return True
    result = [None]
    try:
        win32gui.EnumChildWindows(hwnd, enum_child, result)
    except: pass
    return result[0]

def bm_click(btn):
    if btn:
        win32gui.SendMessage(btn, win32con.BM_CLICK, 0, 0)

class IMAPClient:
    def __init__(self):
        self.client = None

    def connect(self):
        if self.client and self.client.state == 'SELECTED':
            return
        import ssl
        context = ssl.create_default_context()
        context.check_hostname = False
        context.verify_mode = ssl.CERT_NONE
        self.client = imaplib.IMAP4_SSL(CFG['imap_host'], CFG['imap_port'], ssl_context=context)
        self.client.login(CFG['imap_user'], CFG['imap_pwd'])
        self.client.select('INBOX')
        log(f'IMAP 已连接')

    def max_uid(self):
        self.connect()
        _, data = self.client.search(None, f'SUBJECT "{CFG["code_subject"]}"')
        uids = data[0].split()
        if uids:
            return int(uids[-1])
        return 0

    def wait_code(self, base_uid):
        log('按要求等待 45 秒后再收取验证码...')
        time.sleep(45)
        deadline = time.time() + 240
        while time.time() < deadline:
            time.sleep(5)
            try:
                self.connect()
                _, data = self.client.search(None, f'SUBJECT "{CFG["code_subject"]}"')
                uids = data[0].split()
                if uids:
                    max_uid = int(uids[-1])
                    if max_uid > base_uid:
                        _, msg_data = self.client.fetch(str(max_uid), '(RFC822)')
                        msg = email.message_from_bytes(msg_data[0][1])
                        body = ''
                        if msg.is_multipart():
                            for part in msg.walk():
                                if part.get_content_type() == 'text/plain':
                                    body = part.get_payload(decode=True).decode('utf-8', errors='ignore')
                                    break
                        else:
                            body = msg.get_payload(decode=True).decode('utf-8', errors='ignore')
                        log(f'收到新邮件: {msg["Subject"]}')
                        m = re.search(r'验证码[^0-9]{0,12}(\d{4,6})', body)
                        if m:
                            code = m.group(1)
                        else:
                            m2 = re.search(r'\d{6}', body)
                            code = m2.group(0) if m2 else ''
                        if code and len(code) >= 4:
                            log(f'提取验证码: {code}')
                            return code
                        base_uid = max_uid
            except Exception as e:
                log(f'IMAP 轮询异常: {e}')
                self.client = None
        log('等待验证码超时')
        return None

    def close(self):
        if self.client:
            try: self.client.logout()
            except: pass
            self.client = None

imap_client = IMAPClient()

def send_mail(soft_name, body, attachments):
    if not CFG['send_mail']:
        log(f'已禁用发信，跳过: {soft_name}')
        return
    msg = MIMEMultipart()
    msg['From'] = CFG['smtp_user']
    msg['To'] = CFG['mail_to']
    msg['Subject'] = f'您的软件-{soft_name}安装成功，现附上截图。'
    msg.attach(MIMEText(body, 'plain', 'utf-8'))
    for f in attachments or []:
        if os.path.exists(f):
            with open(f, 'rb') as fp:
                part = MIMEBase('application', 'octet-stream')
                part.set_payload(fp.read())
            encoders.encode_base64(part)
            part.add_header('Content-Disposition', f'attachment; filename="{os.path.basename(f)}"')
            msg.attach(part)
    try:
        import ssl
        context = ssl.create_default_context()
        context.check_hostname = False
        context.verify_mode = ssl.CERT_NONE
        with smtplib.SMTP_SSL(CFG['smtp_host'], CFG['smtp_port'], context=context, timeout=60) as s:
            s.login(CFG['smtp_user'], CFG['smtp_pwd'])
            s.send_message(msg)
        log(f'邮件已发送: {msg["Subject"]}')
    except Exception as e:
        log(f'邮件发送失败: {e}')

def send_results():
    if FlowUU.login_ok:
        body = ('网易UU远程已自动安装并登录成功。\r\n识别码信息: ' + (FlowUU.clip or '(未复制到)') +
                '\r\n截图见附件。')
        send_mail('网易UU远程', body, [os.path.join(CFG['log_dir'], 'uu_after_copy.png')])
    if FlowAweSun.login_ok:
        body = ('AweSun向日葵已自动安装并登录成功。\r\n识别码: ' + (FlowAweSun.id_clip or '(未复制到)') +
                '\r\n验证码: ' + (FlowAweSun.code_clip or '(未复制到)') + '\r\n截图见附件。')
        send_mail('AweSun向日葵', body, [os.path.join(CFG['log_dir'], 'awsun_copied.png')])

class FlowUU:
    login_ok = False
    info_ok = False
    clip = ''

    @staticmethod
    def find_main(timeout_ms=0):
        return find_window(lambda w: 'qwindowicon' in w['cls'].lower() and 'uu远程' in w['text'].lower() and w['w'] > 700 and w['h'] > 500, timeout_ms)

    @staticmethod
    def find_login():
        return find_window(lambda w: 'gameviewer' in w['text'].lower() and 'qwindowicon' not in w['cls'].lower() and w['w'] > 700 and w['h'] > 500, 1000)

    @staticmethod
    def wait_login_stable():
        deadline = time.time() + 20
        last = None
        stable = 0
        while time.time() < deadline:
            w = FlowUU.find_login()
            if w:
                rect = win32gui.GetWindowRect(w['hwnd'])
                if last and last['hwnd'] == w['hwnd'] and last['rect'] == rect and abs(w['w'] - last['w']) <= 1 and abs(w['h'] - last['h']) <= 1:
                    stable += 1
                    if stable >= 2 and w['w'] >= 760 and w['h'] >= 560:
                        return w
                else:
                    stable = 0
                last = w
            time.sleep(0.7)
        log('警告：登录窗未达到稳定尺寸')
        return last

    @staticmethod
    def install():
        kill_processes(['GameViewer', 'GameViewerService'])
        time.sleep(1)
        log('启动 UU 安装包')
        start_exe(CFG['uu_exe'])
        inst = find_window_by_title('UU远程', 300, 200, 60000)
        if not inst:
            log('错误：找不到 UU 安装窗口')
            screenshot('uu_no_installer')
            return False
        log(f'UU 安装窗口出现 {inst["w"]}x{inst["h"]}')
        switch_recording(inst['hwnd'], 'uu-install')
        time.sleep(2)
        screenshot('uu_installer_1')
        force_foreground(inst['hwnd'])
        btn_next = find_button(inst['hwnd'], '下一步')
        if btn_next:
            bm_click(btn_next)
            log('点击下一步')
            time.sleep(2)
        ed = None
        def find_edit(hwnd_child, param):
            cls = win32gui.GetClassName(hwnd_child)
            if 'edit' in cls.lower():
                param[0] = hwnd_child
                return False
            return True
        result = [None]
        try: win32gui.EnumChildWindows(inst['hwnd'], find_edit, result)
        except: pass
        ed = result[0]
        if not ed:
            log('错误：找不到路径输入框')
            screenshot('uu_no_edit')
            return False
        win32gui.SendMessage(ed, win32con.WM_SETTEXT, 0, CFG['uu_dir'])
        log(f'安装目录已设置: {CFG["uu_dir"]}')
        time.sleep(0.8)
        screenshot('uu_installer_path')
        btn_inst = find_button(inst['hwnd'], '安装')
        if not btn_inst:
            log('错误：找不到安装按钮')
            return False
        bm_click(btn_inst)
        log('已点击安装，等待安装窗口关闭...')
        deadline = time.time() + 240
        while time.time() < deadline:
            time.sleep(2)
            if not win32gui.IsWindow(inst['hwnd']):
                break
        if win32gui.IsWindow(inst['hwnd']):
            log('警告：安装窗口仍在')
            screenshot('uu_install_stuck')
        main = FlowUU.find_main(120000)
        if not main:
            log('错误：UU 主窗口未出现')
            screenshot('uu_no_main')
            return False
        log(f'UU 主窗口出现 {main["w"]}x{main["h"]}')
        switch_recording(main['hwnd'], 'uu-main')
        force_foreground(main['hwnd'])
        time.sleep(4)
        screenshot('uu_main')
        FlowUU.main_wnd = main
        return True

    @staticmethod
    def is_logged_out(hwnd):
        rect = win32gui.GetWindowRect(hwnd)
        w = rect[2] - rect[0]
        h = rect[3] - rect[1]
        x = int(rect[0] + w * 0.629)
        y = int(rect[1] + h * 0.763)
        img = pyautogui.screenshot(region=(x-30, y-16, 60, 32))
        blue = 0
        for px in range(0, img.width, 2):
            for py in range(0, img.height, 2):
                r, g, b = img.getpixel((px, py))
                if b > 170 and r < 130 and g > 70:
                    blue += 1
        log(f'UU 登录态检测 蓝色像素={blue}')
        return blue >= 30

    @staticmethod
    def login():
        for i in range(6):
            m = FlowUU.find_main(500)
            if m:
                click_ratio(m['hwnd'], 0.698, 0.772, 'UU跳过引导')
            time.sleep(1.3)
        for attempt in range(3):
            m = FlowUU.find_main(500)
            if not m:
                time.sleep(1)
                continue
            click_ratio(m['hwnd'], 0.629, 0.763, 'UU打开登录')
            time.sleep(2.5)
            l = FlowUU.wait_login_stable()
            if l:
                log(f'UU 登录窗稳定 {l["w"]}x{l["h"]}')
                break
        login = FlowUU.wait_login_stable()
        if not login:
            log('错误：UU 登录窗未打开')
            screenshot('uu_no_login')
            return False
        force_foreground(login['hwnd'])
        time.sleep(0.8)
        screenshot('uu_login_page')

        def login_click(rx, ry, tag):
            for i in range(3):
                l = FlowUU.find_login()
                if l:
                    click_ratio(l['hwnd'], rx, ry, tag)
                    return True
                time.sleep(0.8)
            return False

        if not login_click(0.5458, 0.6628, 'UU协议框'):
            return False
        time.sleep(0.7)
        screenshot('uu_agree')
        if not login_click(0.7067, 0.3503, 'UU手机号框'):
            return False
        time.sleep(0.5)
        l = FlowUU.find_login()
        if l:
            force_foreground(l['hwnd'])
            type_text(CFG['phone'])
        time.sleep(0.7)
        screenshot('uu_phone_in')

        base_uid = imap_client.max_uid()
        if not login_click(0.8082, 0.4194, 'UU获取验证码'):
            return False
        time.sleep(2.5)
        screenshot('uu_sendcode')
        log(f'UU 已请求验证码，基线 UID={base_uid}')

        code = imap_client.wait_code(base_uid)
        if not code:
            screenshot('uu_code_timeout')
            return False

        if not login_click(0.6324, 0.4243, 'UU验证码框'):
            return False
        time.sleep(0.4)
        l = FlowUU.find_login()
        if l:
            force_foreground(l['hwnd'])
            type_text(code)
        time.sleep(0.7)
        screenshot('uu_code_in')
        login_click(0.7092, 0.5822, 'UU登录')
        deadline = time.time() + 25
        while time.time() < deadline:
            time.sleep(1)
            if not FlowUU.find_login():
                log('UU 登录成功')
                time.sleep(3)
                screenshot('uu_login_done')
                FlowUU.login_ok = True
                return True
        log('警告：UU 登录窗仍在')
        screenshot('uu_login_fail')
        return False

    @staticmethod
    def ensure_login():
        for i in range(6):
            m = FlowUU.find_main(500)
            if m:
                click_ratio(m['hwnd'], 0.698, 0.772, 'UU跳过引导')
            time.sleep(1.3)
        m = FlowUU.find_main(3000)
        if m and not FlowUU.is_logged_out(m['hwnd']):
            log('UU 已是登录态，跳过短信登录')
            FlowUU.login_ok = True
            time.sleep(1)
            return True
        return FlowUU.login()

    @staticmethod
    def copy_info():
        m = FlowUU.find_main(8000)
        if not m:
            log('错误：复制阶段找不到主窗')
            return False
        FlowUU.main_wnd = m
        force_foreground(m['hwnd'])
        time.sleep(0.8)
        click_ratio(m['hwnd'], 100/920, 74/680, 'UU折叠我的设备')
        time.sleep(1.5)
        click_ratio(m['hwnd'], 100/920, 153/680, 'UU开始协助')
        time.sleep(2.5)
        screenshot('uu_info_page')
        old = clip_read()
        click_ratio(m['hwnd'], 789/920, 256/680, 'UU复制并分享')
        now_s = ''
        for i in range(12):
            time.sleep(0.7)
            now_s = clip_read()
            if now_s and now_s != old:
                break
        screenshot('uu_after_copy')
        if not now_s or now_s == old:
            log('警告：UU 剪贴板未更新')
            return False
        FlowUU.clip = now_s.strip()
        log(f'UU 复制内容: {FlowUU.clip}')
        FlowUU.info_ok = True
        return True

    @staticmethod
    def run():
        try:
            ok = FlowUU.install()
            if ok and FlowUU.ensure_login():
                time.sleep(2)
                FlowUU.copy_info()
        except Exception as e:
            log(f'UU 流程异常: {e}')
            screenshot('uu_exception')

class FlowAweSun:
    login_ok = False
    info_ok = False
    id_clip = ''
    code_clip = ''
    main_wnd = None

    @staticmethod
    def is_aws_cls(cls):
        return 'orayui' in cls.lower() or 'flutter_runner' in cls.lower()

    @staticmethod
    def install():
        kill_processes(['AweSun', 'SunloginClient', 'OldAweSun'])
        time.sleep(1)
        before = set(w['hwnd'] for w in enum_windows())
        log('启动 AweSun 安装包')
        start_exe(CFG['aws_exe'])
        inst = find_window(lambda w: w['hwnd'] not in before and FlowAweSun.is_aws_cls(w['cls']) and w['w'] > 500 and w['h'] > 350, 60000)
        if not inst:
            inst = find_window(lambda w: FlowAweSun.is_aws_cls(w['cls']) and w['w'] > 500 and w['h'] > 350, 5000)
        if not inst:
            log('错误：找不到 AweSun 安装窗口')
            screenshot('awsun_no_installer')
            return False
        log(f'AweSun 安装窗口出现 {inst["w"]}x{inst["h"]}')
        switch_recording(inst['hwnd'], 'awesun-install')
        screenshot('awsun_installer')
        click_ratio(inst['hwnd'], 0.500, 0.548, 'AweSun立即安装')

        deadline = time.time() + 420
        stable = None
        intro_clicks = 0
        while time.time() < deadline:
            time.sleep(3)
            cands = [w for w in enum_windows() if FlowAweSun.is_aws_cls(w['cls']) and w['w'] > 600 and w['h'] > 400]
            if not cands:
                continue
            win_intro = None
            win_dark = None
            intro_size = 0
            dark_size = 0
            for x in cands:
                rect = x['rect']
                rc = (rect[0] + (rect[2]-rect[0])//4, rect[1] + (rect[3]-rect[1])//3,
                      (rect[2]-rect[0])//2, (rect[3]-rect[1])//3)
                img = pyautogui.screenshot(region=rc)
                total = 0
                count = 0
                for px in range(0, img.width, 4):
                    for py in range(0, img.height, 4):
                        r, g, b = img.getpixel((px, py))
                        total += (r*30 + g*59 + b*11)//100
                        count += 1
                br = total / count if count > 0 else 0
                area = x['w'] * x['h']
                pname = ''
                try:
                    p = subprocess.run(['powershell', '-Command', f'(Get-Process -Id {x["pid"]}).ProcessName'],
                                     capture_output=True, text=True, creationflags=subprocess.CREATE_NO_WINDOW)
                    pname = p.stdout.strip()
                except: pass
                is_main = pname.lower() == 'awesun'
                if br > 150 and area > intro_size:
                    intro_size = area
                    win_intro = x
                if br < 90 and is_main and area > dark_size:
                    dark_size = area
                    win_dark = x
            if win_intro:
                rect = win_intro['rect']
                bx = rect[0] + int((rect[2]-rect[0]) * 0.50)
                by = rect[1] + int((rect[3]-rect[1]) * 0.744)
                img = pyautogui.screenshot(region=(bx-140, by-35, 280, 70))
                blue = 0
                for px in range(0, img.width, 2):
                    for py in range(0, img.height, 2):
                        r, g, b = img.getpixel((px, py))
                        if b > 170 and r < 130 and g > 70:
                            blue += 1
                log(f'检测到亮色页面(blue={blue})，疑似16欢迎页')
                screenshot('awsun_intro')
                if blue >= 10 and intro_clicks < 6:
                    click_ratio(win_intro['hwnd'], 0.50, 0.744, 'AweSun立即进入')
                    intro_clicks += 1
                    time.sleep(3)
                else:
                    time.sleep(2)
            if win_dark:
                if stable == win_dark['hwnd']:
                    FlowAweSun.main_wnd = win_dark
                    log(f'AweSun 深色主窗稳定出现 {win_dark["w"]}x{win_dark["h"]}')
                    switch_recording(win_dark['hwnd'], 'awesun-main')
                    force_foreground(win_dark['hwnd'])
                    time.sleep(3)
                    screenshot('awsun_main')
                    return True
                stable = win_dark['hwnd']
                time.sleep(1.5)
        log('错误：等待 AweSun 主窗口超时')
        screenshot('awsun_no_main')
        return False

    @staticmethod
    def is_logged_in():
        rect = win32gui.GetWindowRect(FlowAweSun.main_wnd['hwnd'])
        w = rect[2] - rect[0]
        h = rect[3] - rect[1]
        rc = (rect[0] + int(0.025*w) - 20, rect[1] + int(0.062*h) - 20, 40, 40)
        img = pyautogui.screenshot(region=rc)
        pink = 0
        for px in range(0, img.width, 2):
            for py in range(0, img.height, 2):
                r, g, b = img.getpixel((px, py))
                if r > 170 and g < 140 and b < 170 and r - g > 45:
                    pink += 1
        return pink >= 10

    @staticmethod
    def nianmi_visible():
        rect = win32gui.GetWindowRect(FlowAweSun.main_wnd['hwnd'])
        w = rect[2] - rect[0]
        h = rect[3] - rect[1]
        rc = (rect[0] + int(0.22*w), rect[1] + int(0.15*h), int(0.56*w), int(0.35*h))
        img = pyautogui.screenshot(region=rc)
        white = 0
        count = 0
        for px in range(0, img.width, 3):
            for py in range(0, img.height, 3):
                r, g, b = img.getpixel((px, py))
                count += 1
                if r >= 235 and g >= 235 and b >= 235:
                    white += 1
        return white / count > 0.35 if count > 0 else False

    @staticmethod
    def dismiss_ads(rounds=3):
        for i in range(rounds):
            # 检测蓝色弹窗
            rect = win32gui.GetWindowRect(FlowAweSun.main_wnd['hwnd'])
            w = rect[2] - rect[0]
            h = rect[3] - rect[1]
            rc = (rect[0] + int(0.25*w), rect[1] + int(0.25*h), int(0.55*w), int(0.50*h))
            img = pyautogui.screenshot(region=rc)
            n = 0
            min_x, min_y, max_x, max_y = 99999, 99999, 0, 0
            for px in range(0, img.width, 2):
                for py in range(0, img.height, 2):
                    r, g, b = img.getpixel((px, py))
                    if b > 160 and r < 120 and b - g > 55:
                        n += 1
                        if px < min_x: min_x = px
                        if px > max_x: max_x = px
                        if py < min_y: min_y = py
                        if py > max_y: max_y = py
            if n >= 1500:
                bw = max_x - min_x
                bh = max_y - min_y
                if bw >= 250 and bh >= 130:
                    dens = n / ((bw//2+1) * (bh//2+1))
                    if dens >= 0.30:
                        log('检测到蓝色广告弹窗，关闭')
                        click_point(rc[0] + max_x - 13, rc[1] + min_y + 13)
                        time.sleep(1.5)
                        screenshot('awsun_ad_closed')
                        continue
            if FlowAweSun.nianmi_visible():
                log('检测到免密远控弹窗，关闭')
                click_ratio(FlowAweSun.main_wnd['hwnd'], 0.772, 0.100, 'AweSun关免密弹窗')
                time.sleep(1.5)
                screenshot('awsun_nianmi_closed')
                continue
            # 检测推广弹窗取消按钮
            rc2 = (rect[0] + int(0.55*w), rect[1] + int(0.79*h), int(0.14*w), int(0.08*h))
            img2 = pyautogui.screenshot(region=rc2)
            n2 = 0
            min_x2, min_y2, max_x2, max_y2 = 99999, 99999, 0, 0
            for px in range(img2.width):
                for py in range(img2.height):
                    r, g, b = img2.getpixel((px, py))
                    if r > 180 and g > 180 and b > 180:
                        n2 += 1
                        if px < min_x2: min_x2 = px
                        if px > max_x2: max_x2 = px
                        if py < min_y2: min_y2 = py
                        if py > max_y2: max_y2 = py
            bw2 = max_x2 - min_x2
            bh2 = max_y2 - min_y2
            if n2 >= 50 and bw2 <= 110 and bh2 <= 45:
                log('检测到推广弹窗，点取消')
                click_point(rc2[0] + (min_x2+max_x2)//2, rc2[1] + (min_y2+max_y2)//2)
                time.sleep(1.5)
                screenshot('awsun_promo_closed')
                continue
            return

    @staticmethod
    def id_visible():
        rect = win32gui.GetWindowRect(FlowAweSun.main_wnd['hwnd'])
        w = rect[2] - rect[0]
        h = rect[3] - rect[1]
        rc = (rect[0] + int(0.30*w), rect[1] + int(0.275*h), int(0.22*w), int(0.06*h))
        img = pyautogui.screenshot(region=rc)
        white = 0
        for px in range(img.width):
            for py in range(img.height):
                r, g, b = img.getpixel((px, py))
                if r > 180 and g > 180 and b > 180:
                    white += 1
        return white >= 400

    @staticmethod
    def login():
        force_foreground(FlowAweSun.main_wnd['hwnd'])
        time.sleep(1)
        FlowAweSun.dismiss_ads(2)
        if FlowAweSun.is_logged_in():
            log('AweSun 已是登录态，跳过短信登录')
            screenshot('awsun_logged_in')
            FlowAweSun.dismiss_popups()
            FlowAweSun.login_ok = True
            return True
        click_ratio(FlowAweSun.main_wnd['hwnd'], 0.075, 0.070, 'AweSun登录注册')
        time.sleep(3)
        screenshot('awsun_login_page')
        # 协议勾选
        rect = win32gui.GetWindowRect(FlowAweSun.main_wnd['hwnd'])
        w = rect[2] - rect[0]
        h = rect[3] - rect[1]
        cx = rect[0] + int(0.433 * w)
        cy = rect[1] + int(0.805 * h)
        img = pyautogui.screenshot(region=(cx-9, cy-9, 18, 18))
        blue = 0
        for px in range(0, img.width, 2):
            for py in range(0, img.height, 2):
                r, g, b = img.getpixel((px, py))
                if b > 170 and r < 130 and g > 70:
                    blue += 1
        log(f'协议勾选检测 blue={blue}')
        if blue < 8:
            click_ratio(FlowAweSun.main_wnd['hwnd'], 0.433, 0.805, 'AweSun勾选协议')
        else:
            log('协议已勾选，跳过')
        click_ratio(FlowAweSun.main_wnd['hwnd'], 0.600, 0.347, 'AweSun手机号框')
        time.sleep(0.5)
        type_text(CFG['phone'])
        time.sleep(0.7)
        screenshot('awsun_phone_in')

        base_uid = imap_client.max_uid()
        click_ratio(FlowAweSun.main_wnd['hwnd'], 0.722, 0.434, 'AweSun获取验证码')
        time.sleep(3)
        screenshot('awsun_sendcode')
        log(f'AweSun 已请求验证码，基线 UID={base_uid}')

        code = imap_client.wait_code(base_uid)
        if not code:
            screenshot('awsun_code_timeout')
            return False

        click_ratio(FlowAweSun.main_wnd['hwnd'], 0.540, 0.431, 'AweSun验证码框')
        time.sleep(0.4)
        type_text(code)
        time.sleep(0.7)
        screenshot('awsun_code_in')
        click_ratio(FlowAweSun.main_wnd['hwnd'], 0.590, 0.548, 'AweSun登录')
        time.sleep(10)
        screenshot('awsun_after_login')
        FlowAweSun.dismiss_popups()
        FlowAweSun.login_ok = True
        return True

    @staticmethod
    def dismiss_popups():
        was_visible = False
        miss = 0
        for i in range(8):
            if FlowAweSun.nianmi_visible():
                was_visible = True
                miss = 0
                click_ratio(FlowAweSun.main_wnd['hwnd'], 0.772, 0.100, 'AweSun关免密弹窗')
                time.sleep(1.8)
            else:
                if was_visible:
                    break
                miss += 1
                if miss >= 2:
                    break
                time.sleep(1.5)
        time.sleep(0.8)
        click_ratio(FlowAweSun.main_wnd['hwnd'], 0.781, 0.202, 'AweSun关红包提示')
        time.sleep(1.2)
        click_ratio(FlowAweSun.main_wnd['hwnd'], 0.781, 0.202, 'AweSun关红包提示')
        time.sleep(0.8)
        screenshot('awsun_popups_closed')

    @staticmethod
    def set_password():
        FlowAweSun.dismiss_ads(2)
        click_ratio(FlowAweSun.main_wnd['hwnd'], 0.060, 0.950, 'AweSun设置')
        time.sleep(2)
        screenshot('awsun_settings')
        FlowAweSun.dismiss_ads(2)
        click_ratio(FlowAweSun.main_wnd['hwnd'], 0.260, 0.435, 'AweSun安全')
        time.sleep(2)
        screenshot('awsun_security_page')
        FlowAweSun.dismiss_ads(2)
        rect = win32gui.GetWindowRect(FlowAweSun.main_wnd['hwnd'])
        cbx = rect[0] + int(0.465 * (rect[2]-rect[0]))
        cby = rect[1] + int(0.464 * (rect[3]-rect[1]))
        img = pyautogui.screenshot(region=(cbx-9, cby-9, 18, 18))
        blue = 0
        for px in range(0, img.width, 2):
            for py in range(0, img.height, 2):
                r, g, b = img.getpixel((px, py))
                if b > 170 and r < 130 and g > 70:
                    blue += 1
        log(f'访问密码勾选检测 blue={blue}')
        if blue >= 8:
            click_ratio(FlowAweSun.main_wnd['hwnd'], 0.548, 0.464, 'AweSun修改密码')
        else:
            click_ratio(FlowAweSun.main_wnd['hwnd'], 0.465, 0.464, 'AweSun勾选访问密码')
        time.sleep(1.5)
        screenshot('awsun_pwd_fields')
        FlowAweSun.dismiss_ads(2)
        click_ratio(FlowAweSun.main_wnd['hwnd'], 0.642, 0.534, 'AweSun密码1')
        time.sleep(0.5)
        pyautogui.keyDown('ctrl')
        pyautogui.keyDown('a')
        pyautogui.keyUp('a')
        pyautogui.keyUp('ctrl')
        time.sleep(0.2)
        type_text(CFG['aws_pwd'])
        time.sleep(0.5)
        click_ratio(FlowAweSun.main_wnd['hwnd'], 0.642, 0.609, 'AweSun密码2')
        time.sleep(0.5)
        pyautogui.keyDown('ctrl')
        pyautogui.keyDown('a')
        pyautogui.keyUp('a')
        pyautogui.keyUp('ctrl')
        time.sleep(0.2)
        type_text(CFG['aws_pwd'])
        time.sleep(0.6)
        screenshot('awsun_pwd_in')
        FlowAweSun.dismiss_ads(2)
        click_ratio(FlowAweSun.main_wnd['hwnd'], 0.519, 0.759, 'AweSun保存')
        time.sleep(2)
        screenshot('awsun_pwd_saved')
        FlowAweSun.dismiss_ads(2)
        FlowAweSun.dismiss_popups()
        return True

    @staticmethod
    def wait_clip_change(old, rx, ry, tag):
        cur = old
        for i in range(10):
            time.sleep(0.6)
            cur = clip_read()
            if cur and cur.strip() != (old or '').strip():
                break
            if i == 2:
                click_ratio(FlowAweSun.main_wnd['hwnd'], rx, ry, tag + '-重试')
        return cur

    @staticmethod
    def copy_info():
        FlowAweSun.dismiss_popups()
        FlowAweSun.dismiss_ads(3)
        page_ready = False
        for i in range(12):
            FlowAweSun.dismiss_ads(2)
            if FlowAweSun.id_visible():
                page_ready = True
                break
            click_ratio(FlowAweSun.main_wnd['hwnd'], 0.075, 0.156, 'AweSun远程协助')
            time.sleep(2.5)
        screenshot('awsun_info_page')
        if not page_ready:
            log('警告：未确认远程协助页已就绪，继续尝试复制')

        old = clip_read()
        FlowAweSun.id_clip = old
        for t in range(4):
            FlowAweSun.dismiss_ads(2)
            click_ratio(FlowAweSun.main_wnd['hwnd'], 0.524, 0.303, 'AweSun复制识别码')
            FlowAweSun.id_clip = FlowAweSun.wait_clip_change(old, 0.524, 0.303, 'AweSun复制识别码')
            if FlowAweSun.id_clip and FlowAweSun.id_clip.strip() != (old or '').strip():
                break
            log(f'识别码未复制，重试 {t+1}')
        mid = FlowAweSun.id_clip
        FlowAweSun.code_clip = mid
        for round in range(4):
            FlowAweSun.dismiss_ads(2)
            click_ratio(FlowAweSun.main_wnd['hwnd'], 0.712, 0.300, 'AweSun显示验证码')
            time.sleep(1.2)
            if round == 0:
                screenshot('awsun_code_shown')
            FlowAweSun.dismiss_ads(1)
            click_ratio(FlowAweSun.main_wnd['hwnd'], 0.757, 0.299, 'AweSun复制验证码')
            for i in range(5):
                time.sleep(0.6)
                FlowAweSun.code_clip = clip_read()
                if FlowAweSun.code_clip and FlowAweSun.code_clip.strip() != (mid or '').strip():
                    break
            else:
                log(f'验证码未复制，切换眼睛状态重试 round={round+1}')
                continue
            break
        time.sleep(0.5)
        screenshot('awsun_copied')
        FlowAweSun.id_clip = (FlowAweSun.id_clip or '').strip()
        FlowAweSun.code_clip = (FlowAweSun.code_clip or '').strip()
        log(f'AweSun 识别码: {FlowAweSun.id_clip}')
        log(f'AweSun 验证码: {FlowAweSun.code_clip}')
        if not FlowAweSun.id_clip or FlowAweSun.id_clip == (old or '').strip():
            log('警告：识别码未复制成功')
            return False
        if not FlowAweSun.code_clip or FlowAweSun.code_clip == FlowAweSun.id_clip:
            log('警告：验证码未复制成功')
            return False
        FlowAweSun.info_ok = True
        return True

    @staticmethod
    def run():
        try:
            if FlowAweSun.install() and FlowAweSun.login() and FlowAweSun.set_password():
                time.sleep(2)
                FlowAweSun.copy_info()
        except Exception as e:
            log(f'AweSun 流程异常: {e}')
            screenshot('awsun_exception')

class Uninstaller:
    sysdiag_removed = False
    kill_list = ['GameViewer', 'AweSun', 'SunloginClient', 'OldAweSun', 'SunloginClient_Helper',
                 'HipsMain', 'HipsDaemon', 'HipsTray', 'HipsLog', 'HipsKeeper', 'usysdiag', 'Sysdiag_setup',
                 'sysdiag-all-x64-6.0.12.1-2026.09.22.1', 'uuyc_4.42.0', 'AweSun_16.6.0.32198']

    @staticmethod
    def match(name, kind):
        if not name:
            return False
        if kind == 'uu':
            return 'uu远程' in name.lower() or 'gameviewer' in name.lower()
        if kind == 'aw':
            return '向日葵' in name or 'awesun' in name.lower() or 'sunlogin' in name.lower()
        if kind == 'hr':
            return '火绒' in name or 'huorong' in name.lower() or 'sysdiag' in name.lower()
        return False

    @staticmethod
    def run_uninstall_string(cmd):
        log(f'执行卸载命令: {cmd}')
        try:
            p = subprocess.Popen(['cmd', '/c', cmd], cwd=CFG['soft_dir'],
                                creationflags=subprocess.CREATE_NO_WINDOW)
            p.wait(timeout=90)
        except Exception as e:
            log(f'卸载命令启动失败: {e}')

    @staticmethod
    def handle_uninstall_ui(seconds):
        end = time.time() + seconds
        ok_texts = ['继续卸载', '残忍卸载', '确定', '是', '下一步', '卸载']
        while time.time() < end:
            acted = False
            for w in enum_windows():
                if w['w'] < 200 or w['h'] < 100:
                    continue
                if w['cls'] == '#32770' or 'orayui' in w['cls'].lower() or 'huorong' in w['cls'].lower() or '卸载' in w['text']:
                    for t in ok_texts:
                        btn = find_button(w['hwnd'], t)
                        if btn:
                            log(f'卸载向导点击: {t}')
                            force_foreground(w['hwnd'])
                            bm_click(btn)
                            acted = True
                            time.sleep(1.5)
                            break
            if not acted:
                time.sleep(1.5)

    @staticmethod
    def remove_dir(p):
        if not os.path.exists(p):
            return
        try:
            import shutil
            shutil.rmtree(p)
            log(f'已删除目录 {p}')
        except Exception as e:
            log(f'删除目录失败 {p}: {e}')

    @staticmethod
    def run():
        log('================ 卸载阶段 ================')
        kill_processes(Uninstaller.kill_list)
        time.sleep(2)
        import winreg
        for kind in ['uu', 'aw', 'hr']:
            for root in [winreg.HKEY_LOCAL_MACHINE, winreg.HKEY_CURRENT_USER]:
                for sub in [r'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
                           r'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall']:
                    try:
                        parent = winreg.OpenKey(root, sub)
                    except:
                        continue
                    i = 0
                    while True:
                        try:
                            name = winreg.EnumKey(parent, i)
                            i += 1
                            try:
                                k = winreg.OpenKey(parent, name)
                                dn, _ = winreg.QueryValueEx(k, 'DisplayName')
                                if not Uninstaller.match(str(dn), kind):
                                    winreg.CloseKey(k)
                                    continue
                                log(f'发现已安装: {dn}')
                                try:
                                    quiet, _ = winreg.QueryValueEx(k, 'QuietUninstallString')
                                except:
                                    quiet = ''
                                try:
                                    uns, _ = winreg.QueryValueEx(k, 'UninstallString')
                                except:
                                    uns = ''
                                winreg.CloseKey(k)
                                cmd = quiet if quiet else uns
                                if cmd:
                                    if 'unins' in cmd.lower() and '/' not in cmd:
                                        cmd += ' /S'
                                    Uninstaller.run_uninstall_string(cmd)
                                    Uninstaller.handle_uninstall_ui(60)
                                if kind == 'hr':
                                    Uninstaller.sysdiag_removed = True
                                    log('sysdiag 已卸载，重装前必须重启系统')
                            except:
                                pass
                        except OSError:
                            break
                    winreg.CloseKey(parent)
        run_cmd('sc.exe', 'stop SunloginService', 8000)
        run_cmd('sc.exe', 'delete SunloginService', 8000)
        for svc in ['HipsDaemon', 'HipsKeeper', 'HipsService']:
            run_cmd('sc.exe', f'stop {svc}', 5000)
            run_cmd('sc.exe', f'delete {svc}', 5000)
        time.sleep(3)
        kill_processes(Uninstaller.kill_list)
        Uninstaller.remove_dir(CFG['uu_dir'])
        Uninstaller.remove_dir(r'C:\Program Files\Oray')
        Uninstaller.remove_dir(r'C:\Program Files\Huorong')
        log('卸载阶段完成')

def check_huorong():
    tl = subprocess.run(['tasklist'], capture_output=True, creationflags=subprocess.CREATE_NO_WINDOW)
    procs = tl.stdout.decode('gbk', errors='replace').lower()
    for n in ['hipstray.exe', 'hipsmain.exe', 'hipsdaemon.exe', 'hrconfig.exe']:
        if n in procs:
            return n
    for svc in ['HipsDaemon', 'sysdiag']:
        r = subprocess.run(['sc', 'query', svc], capture_output=True, creationflags=subprocess.CREATE_NO_WINDOW)
        if r.returncode == 0 and 'running' in r.stdout.decode('gbk', errors='replace').lower():
            return svc
    return None

def main():
    global LOG_FILE, REC_TS
    try:
        import ctypes
        ctypes.windll.user32.SetProcessDPIAware()
    except: pass
    ts = datetime.now().strftime('%Y%m%d%H%M%S')
    REC_TS = ts
    ensure_dir(CFG['log_dir'])
    LOG_FILE = os.path.join(CFG['log_dir'], f'AutoInstall-{ts}.log')

    mode = 'all'
    for a in sys.argv[1:]:
        if a == '-record':
            CFG['record'] = True
        elif a == '-nomail':
            CFG['send_mail'] = False
        elif a.startswith('-'):
            mode = a[1:]

    log(f'================ SysIns Python 自动安装 开始 mode={mode} ================')

    if mode == 'resume':
        pending = check_resume()
        if not pending:
            log('无续跑状态，退出')
            return
        mode = pending
    elif os.path.exists(resume_state_path()):
        pending = check_resume()
        if pending:
            mode = pending

    if mode not in ('uninstall', 'sysdiag'):
        hr = check_huorong()
        if hr:
            log('检测到火绒安全软件正在运行（' + hr + '），请先手动退出或卸载后再运行本脚本')
            sys.exit(2)

    start_recording(None, 'boot')
    rc = 0
    try:
        if mode == 'uninstall':
            Uninstaller.run()
        elif mode == 'sysdiag':
            Uninstaller.run()
            if Uninstaller.sysdiag_removed:
                resume_register_task('sysdiag2')
                stop_recording()
                do_reboot()
                time.sleep(15)
                sys.exit(0)
        elif mode == 'sysdiag2':
            FlowUU.run()
            FlowAweSun.run()
            send_results()
        elif mode == 'phase2':
            FlowUU.run()
            FlowAweSun.run()
            send_results()
        elif mode == 'uu':
            FlowUU.run()
        elif mode == 'awesun':
            FlowAweSun.run()
        else:
            Uninstaller.run()
            if Uninstaller.sysdiag_removed:
                resume_register_task('phase2')
                stop_recording()
                do_reboot()
                time.sleep(15)
                sys.exit(0)
            FlowUU.run()
            FlowAweSun.run()
            send_results()
    except Exception as e:
        log(f'主流程异常: {e}')
        rc = 1
    finally:
        imap_client.close()
        stop_recording()
        log('================ 结果汇总 ================')
        log(f'网易UU远程: 安装/登录={FlowUU.login_ok} 识别码={FlowUU.info_ok}')
        log(f'AweSun:    登录={FlowAweSun.login_ok} 识别码={FlowAweSun.info_ok}')
        log('火绒安全:  按要求跳过（不安装）')
    time.sleep(2)
    sys.exit(rc)

if __name__ == '__main__':
    main()
