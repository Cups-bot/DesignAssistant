# Установка CupsForge на рабочее место дизайнера.
#
# Запускать не отсюда, а с сетевой раздачи: Y:\Soft\CupsForge\release\install.cmd
# Туда этот файл кладёт publish.ps1 вместе с новой версией.
#
# Прав администратора не требуется: программа ставится в профиль пользователя.

$ErrorActionPreference = 'Stop'

$release = $PSScriptRoot
$target  = Join-Path $env:LOCALAPPDATA 'CupsForge'

Write-Host ''
Write-Host 'Установка CupsForge' -ForegroundColor Cyan
Write-Host ''

# --- Какая версия свежая ---
$latestPath = Join-Path $release 'latest.json'
if (-not (Test-Path $latestPath)) {
    throw "Не найден $latestPath. Запускайте install.cmd из папки раздачи, а не из копии."
}

$latest = Get-Content $latestPath -Raw | ConvertFrom-Json
$source = Join-Path (Join-Path $release $latest.folder) 'CupsForge.exe'
if (-not (Test-Path $source)) {
    throw "Заявлена версия $($latest.version), но файла нет: $source"
}
Write-Host "Версия: $($latest.version)"

# --- Копируем ---
New-Item -ItemType Directory -Force -Path $target | Out-Null
$targetExe = Join-Path $target 'CupsForge.exe'

# Если программа сейчас запущена, файл занят — просим закрыть.
try {
    Copy-Item $source $targetExe -Force
} catch {
    throw "Не удалось скопировать программу — возможно, она сейчас запущена. Закройте её и повторите."
}
Write-Host "Установлено: $targetExe"

# Доступ к Bitrix намеренно не переносится: раздача может быть публичной,
# а ключ — нет. Дизайнер вводит его у себя один раз, в настройках программы.

# --- Ярлык на рабочем столе ---
$shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'CupsForge.lnk'
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $targetExe
$link.WorkingDirectory = $target
$link.IconLocation = $targetExe
$link.Description = 'CupsForge — создание проектов дизайна'
$link.Save()
Write-Host "Ярлык на рабочем столе: $shortcut"

Write-Host ''
Write-Host 'Готово.' -ForegroundColor Green
Write-Host ''
Write-Host 'Что дальше:'
Write-Host '  - В офисе программа готова к работе сразу.'
Write-Host '  - На домашней машине при первом запуске откроется мастер настройки:'
Write-Host '    укажите свою папку STAKANY и нажмите «Разложить по офисной структуре».'
Write-Host '  - Доступ к Bitrix вводится в настройках программы (шестерёнка).'
Write-Host ''

$answer = Read-Host 'Запустить сейчас? [Y/n]'
if ($answer -eq '' -or $answer -match '^[YyДд]') {
    Start-Process $targetExe
}
