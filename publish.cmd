@echo off
rem Обёртка над publish.ps1.
rem
rem По умолчанию Windows запрещает запуск .ps1, а прав администратора,
rem чтобы снять запрет, у нас нет. Здесь политика обходится только для
rem одного запуска и только для этого процесса — в системе не меняется ничего.
rem
rem Использование:
rem     publish.cmd
rem     publish.cmd -Notes "добавлены крышки"
rem     publish.cmd -Destination D:\test

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1" %*

rem Окно не закрывается сразу: при запуске двойным кликом иначе не увидеть,
rem чем всё закончилось.
echo.
pause
