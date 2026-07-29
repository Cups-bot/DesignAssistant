@echo off
rem Installs CupsForge on a workstation. Double click and that is all.
rem
rem ASCII ONLY: cmd.exe reads .cmd files in the console codepage, not UTF-8.
rem Cyrillic here decodes into garbage and a stray byte can look like a
rem command separator. Everything the designer reads is printed by
rem install.ps1, which is UTF-8 with BOM and handles Cyrillic properly.
rem
rem Windows blocks .ps1 by default and designers have no administrator
rem rights to lift that. The policy is bypassed for this one run.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*

if errorlevel 1 pause
