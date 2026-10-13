# SysIns 自动安装工具 - 配置模板
# 使用方法：复制本文件为 config.ps1，然后填写实际配置
# 注意：config.ps1 已被 .gitignore 排除，不会上传到 GitHub

$Config = @{
    # 手机号（用于短信登录）
    Phone     = "your_phone_number"

    # 网易UU远程安装目录
    UuycDir   = "D:\SoftApp\GameViewer"

    # IMAP 配置（用于接收验证码邮件）
    ImapHost  = "imap.zoho.com.cn"
    ImapPort  = 993
    ImapUser  = "your_email@example.com"
    ImapPwd   = "your_imap_password"

    # 验证码邮件主题关键词
    SubjectKw = "验证码邮件通知"

    # 是否录屏
    Record    = $true

    # SMTP 配置（用于发送安装结果邮件）
    SmtpHost  = "smtp.qq.com"
    SmtpPort  = 465
    SmtpUser  = "your_email@qq.com"
    SmtpPwd   = "your_smtp_authorization_code"

    # 接收安装结果的邮箱
    MailTo    = "your_email@example.com"
}
