using System;

namespace AutoInstall_CSharp
{
    static partial class Cfg
    {
        // 敏感配置 - 复制本文件为 Config.cs 并填写实际值
        // 注意：Config.cs 已被 .gitignore 排除，不会上传到 GitHub

        public const string Phone = "your_phone_number";
        public const string AwsPwd = "your_awesun_password";
        public const string ImapHost = "imap.zoho.com.cn";
        public const int ImapPort = 993;
        public const string ImapUser = "your_email@example.com";
        public const string ImapPwd = "your_imap_password";
        public const string CodeSubject = "验证码邮件通知";
        public const string SmtpHost = "smtp.qq.com";
        public const int SmtpPort = 465;
        public const string SmtpUser = "your_email@qq.com";
        public const string SmtpPwd = "your_smtp_authorization_code";
        public const string MailTo = "your_email@example.com";
    }
}
