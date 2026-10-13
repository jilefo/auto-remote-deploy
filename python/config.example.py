# SysIns 自动安装工具 - Python 配置模板
# 使用方法：复制本文件为 config.py，然后填写实际配置
# 注意：config.py 已被 .gitignore 排除，不会上传到 GitHub

CFG = {
    'root': r'.',           # 项目根目录（脚本会自动探测）
    'soft_dir': r'Soft',
    'log_dir': r'Logs',
    'ffmpeg': r'Tools\ffmpeg.exe',
    'uu_exe': r'Soft\uuyc_4.42.0.exe',
    'aws_exe': r'Soft\AweSun_16.6.0.32198.exe',
    'uu_dir': r'D:\SoftApp\GameViewer',
    'phone': 'your_phone_number',
    'aws_pwd': 'your_awesun_password',
    'imap_host': 'imap.zoho.com.cn',
    'imap_port': 993,
    'imap_user': 'your_email@example.com',
    'imap_pwd': 'your_imap_password',
    'code_subject': '验证码邮件通知',
    'smtp_host': 'smtp.qq.com',
    'smtp_port': 465,
    'smtp_user': 'your_email@qq.com',
    'smtp_pwd': 'your_smtp_authorization_code',
    'mail_to': 'your_email@example.com',
    'send_mail': True,
    'record': True,
    'lang': 'Python',
}
