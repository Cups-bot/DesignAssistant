@echo off
rem Wrapper for publish.ps1 -- builds and publishes a new version.
rem
rem ASCII ONLY: see the note in push-templates.cmd. cmd.exe reads this file
rem in the console codepage, so Cyrillic here would decode into garbage and
rem a stray byte can look like a command separator.
rem
rem Windows blocks .ps1 by default and we have no administrator rights to
rem lift that. The policy is bypassed for this one process only -- nothing
rem in the system changes.
rem
rem Usage:
rem     publish.cmd
rem     publish.cmd -Notes "..."
rem     publish.cmd -Destination D:\test

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1" %*

rem Keep the window open: on a double click there is no other way to see
rem how it ended.
echo.
pause
