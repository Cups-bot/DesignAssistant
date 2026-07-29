@echo off
rem Выкладывает шаблоны в публичную раздачу. Двойной клик — и всё.
rem Обходит запрет на запуск .ps1 на один запуск, в системе ничего не меняя.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0push-templates.ps1" %*

echo.
pause
