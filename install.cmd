@echo off
rem Установка CupsForge на рабочее место. Двойной клик — и всё.
rem
rem Windows по умолчанию запрещает запуск .ps1, а прав администратора для снятия
rem запрета у дизайнеров нет. Здесь политика обходится на один запуск.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*

if errorlevel 1 (
    echo.
    echo Установка не завершена. Текст ошибки выше.
    pause
)
