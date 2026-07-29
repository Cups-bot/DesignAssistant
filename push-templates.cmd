@echo off
rem Wrapper for push-templates.ps1 -- publishes templates to the distribution.
rem
rem ASCII ONLY. cmd.exe reads .cmd files in the console codepage, not UTF-8:
rem Cyrillic here decodes into garbage, and a stray byte can look like a
rem command separator -- the line then breaks apart. Explanations live in
rem the .ps1, which is UTF-8 with BOM and handles Cyrillic properly.
rem
rem Usage:
rem     push-templates.cmd -WhatIf
rem     push-templates.cmd
rem     push-templates.cmd -Prune

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0push-templates.ps1" %*

echo.
pause
