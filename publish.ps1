# Публикация новой версии на сетевую раздачу.
#
#   .\publish.ps1                       — собрать и выложить на Y:\Soft\CupsForge\release
#   .\publish.ps1 -Destination D:\test  — то же в другую папку (проверить, не трогая рабочую)
#   .\publish.ps1 -Notes "добавлены крышки"
#
# Прав администратора не требуется: пишем только в сетевую папку,
# а у дизайнеров программа живёт в их собственном профиле.

param(
    [string] $Destination = 'Y:\Soft\CupsForge\release',
    [string] $Notes = ''
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# --- Версия берётся из проекта, дублировать её здесь нельзя ---
[xml] $proj = Get-Content (Join-Path $root 'CupsForge\CupsForge.csproj')
$version = ($proj.Project.PropertyGroup.Version | Where-Object { $_ }) -as [string]
if (-not $version) { throw 'В CupsForge.csproj не задан <Version>.' }
Write-Host "Версия: $version"

# --- Самопроверка: не выкладываем то, что не проходит собственные тесты ---
Write-Host 'Самопроверка…'
dotnet run --project (Join-Path $root 'Tests\SelfCheck') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Самопроверка не прошла — публикация отменена.' }

# --- Сборка ---
$staging = Join-Path $env:TEMP "cupsforge_publish_$version"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }

Write-Host 'Сборка…'
dotnet publish (Join-Path $root 'CupsForge\CupsForge.csproj') -c Release -o $staging --nologo
if ($LASTEXITCODE -ne 0) { throw 'Сборка не удалась.' }

# --- Выкладка ---
$target = Join-Path $Destination $version
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item (Join-Path $staging 'CupsForge.exe') $target -Force

# Каталог кладётся в папку шаблонов, а не сюда: он бесполезен без .ai-файлов
# и должен ехать к удалённым дизайнерам вместе с ними.
Write-Host "Выложено: $target"

# --- Указатель на свежую версию ---
$latest = [ordered] @{ version = $version; folder = $version; notes = $Notes }
$latestPath = Join-Path $Destination 'latest.json'
$latest | ConvertTo-Json | Out-File $latestPath -Encoding utf8
Write-Host "Обновлён указатель: $latestPath"

Remove-Item $staging -Recurse -Force

Write-Host ''
Write-Host 'Готово. У дизайнеров при следующем запуске появится полоска «Доступна версия».'
Write-Host 'Каталог продуктов обновляется отдельно — правкой catalog.json в папке шаблонов.'
