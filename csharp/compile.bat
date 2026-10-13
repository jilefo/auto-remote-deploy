@echo off
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set MK=..\Tools\mailkit

"%CSC%" /nologo /target:exe /platform:x64 /out:AutoInstall.exe /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:"%MK%\MailKit.dll" /r:"%MK%\MimeKit.dll" /r:"%MK%\BouncyCastle.Cryptography.dll" AutoInstall.cs

if errorlevel 1 (
    echo BUILD FAILED
) else (
    echo BUILD OK: AutoInstall.exe
)
pause
