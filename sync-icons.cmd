@echo off
rem Wrapper for sync-icons.ps1 -- rebuilds Shared\Theme\Icons.xaml from Icons\*.svg.
rem
rem ASCII ONLY: see the note in push-templates.cmd. cmd.exe reads this file
rem in the console codepage, so Cyrillic here would decode into garbage and
rem a stray byte can look like a command separator. Everything a human reads
rem is printed by the .ps1.
rem
rem Usage:
rem     sync-icons.cmd
rem     sync-icons.cmd -WhatIf

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0sync-icons.ps1" %*

rem Keep the window open: on a double click there is no other way to see
rem how it ended.
echo.
pause
