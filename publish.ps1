# Выпуск новой версии CupsForge.
#
#   .\publish.ps1                       — выпустить. Всё.
#   .\publish.ps1 -Notes "новые иконки"
#   .\publish.ps1 -Local D:\проба       — собрать к себе, никуда не отправляя
#
# Обычный выпуск НИЧЕГО НЕ ТРЕБУЕТ РУКАМИ: ни копировать файлы, ни выбирать
# папку, ни поднимать версию. Запустили — и у всех, у кого программа стоит,
# при следующем запуске появится полоска «Доступна версия».
#
# Раздача — публичный репозиторий GitHub. Он единственный достаёт до всех:
# сетевой диск виден только в офисе, а интернет есть у каждого. Если диск
# доступен, копия кладётся и туда — в офисе обновление пойдёт по локальной
# сети, быстрее. Но это ускорение, а не условие: делать для него ничего
# не нужно.
#
# Прав администратора не требуется ни здесь, ни у дизайнеров.

param(
    [string] $Notes = '',
    [string] $Local = '',
    [string] $Repo = 'Cups-bot/CupsForge-public',
    [string] $OfficeShare = 'Y:\Soft\CupsForge\release'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# ─────────────────────────────────────────────────────────────────────────────
# Версия
# ─────────────────────────────────────────────────────────────────────────────
# Старшая часть из проекта (её меняют осознанно), младшая — число коммитов.
# Забыть поднять невозможно: любой коммит её двигает, и она всегда больше
# предыдущей.

[xml] $proj = Get-Content (Join-Path $root 'CupsForge\CupsForge.csproj')
$declared = ($proj.Project.PropertyGroup.Version | Where-Object { $_ }) -as [string]
if (-not $declared) { throw 'В CupsForge.csproj не задан <Version>.' }

$parts = $declared.Split('.')
$commits = (git -C $root rev-list --count HEAD).Trim()
if (-not $commits) { throw 'Не удалось спросить у git число коммитов.' }

$version = "$($parts[0]).$($parts[1]).$commits"
Write-Host "Версия: $version"

if (-not $Notes) {
    $Notes = (git -C $root log -1 --pretty=%s).Trim()
    Write-Host "Что изменилось (из последнего коммита): $Notes"
}

# ─────────────────────────────────────────────────────────────────────────────
# Самопроверка
# ─────────────────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host 'Самопроверка…'
dotnet run --project (Join-Path $root 'Tests\SelfCheck') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Самопроверка не прошла — выпуск отменён.' }

# ─────────────────────────────────────────────────────────────────────────────
# Ключ раздачи — спрашиваем ДО долгой сборки
# ─────────────────────────────────────────────────────────────────────────────
# Обидно собирать пять минут и только потом узнать, что заливать нечем.
$token = ''
if (-not $Local) {
    . (Join-Path $root 'dist-common.ps1')
    $ProgressPreference = 'SilentlyContinue'
    $token = Get-DistToken -Repo $Repo
}

# ─────────────────────────────────────────────────────────────────────────────
# Сборка
# ─────────────────────────────────────────────────────────────────────────────
# Публикуем ПАПКОЙ, а не одним файлом: разница между версиями считается
# по файлам. Единый сжатый blob менялся бы целиком, и «поправить значок»
# стоило бы дизайнеру восьмидесяти мегабайт вместо трёхсот килобайт.

$staging = Join-Path $env:TEMP "cupsforge_build_$version"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }

Write-Host ''
Write-Host 'Сборка…'
dotnet publish (Join-Path $root 'CupsForge\CupsForge.csproj') -c Release -o $staging `
    -p:Version=$version --nologo
if ($LASTEXITCODE -ne 0) { throw 'Сборка не удалась.' }

# ─────────────────────────────────────────────────────────────────────────────
# Где собираем пакеты
# ─────────────────────────────────────────────────────────────────────────────
# Это КЭШ, а не канал: его можно удалить в любой момент, ничего не сломается.
# Прошлые выпуски нужны только затем, чтобы посчитать разницу; если их нет,
# выпуск просто выйдет полным.
#
# Лежит вне репозитория намеренно. Раньше при недоступном сетевом диске
# пакеты складывались в release-local внутри репозитория — папку, на которую
# ничто не смотрит. Выглядело как канал обновления, а обновление из неё
# не приходило никому.
$cache = if ($Local) { $Local } else { Join-Path $env:LOCALAPPDATA 'CupsForge-publish' }
New-Item -ItemType Directory -Force -Path $cache | Out-Null

if (-not $Local) {
    Write-Host ''
    Write-Host 'Забираю прошлый выпуск из раздачи (нужен, чтобы посчитать разницу)…'
    dotnet vpk download github --repoUrl "https://github.com/$Repo" --token $token --outputDir $cache
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Прошлых выпусков в раздаче нет — этот выйдет полным.' -ForegroundColor Yellow
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Упаковка
# ─────────────────────────────────────────────────────────────────────────────
$notesFile = Join-Path $env:TEMP "cupsforge_notes_$version.md"
[System.IO.File]::WriteAllText($notesFile, $Notes, (New-Object System.Text.UTF8Encoding $false))

Write-Host ''
Write-Host 'Упаковка…'
dotnet vpk pack `
    --packId CupsForge `
    --packVersion $version `
    --packDir $staging `
    --mainExe CupsForge.exe `
    --packTitle 'CupsForge' `
    --packAuthors 'Cups' `
    --icon (Join-Path $root 'Images\Icon\logo.ico') `
    --releaseNotes $notesFile `
    --outputDir $cache
if ($LASTEXITCODE -ne 0) { throw 'Упаковка не удалась.' }

Remove-Item $notesFile -Force -ErrorAction SilentlyContinue
Remove-Item $staging -Recurse -Force

$delta = Get-ChildItem $cache -Filter "CupsForge-$version-delta.nupkg" -ErrorAction SilentlyContinue
$weight = if ($delta) { "$([int]($delta.Length / 1KB)) КБ" } else { 'полностью (прошлых версий рядом не было)' }

# ─────────────────────────────────────────────────────────────────────────────
# Только к себе
# ─────────────────────────────────────────────────────────────────────────────
if ($Local) {
    Write-Host ''
    Write-Host "Собрано в $Local" -ForegroundColor Green
    Write-Host "  установщик: $Local\CupsForge-win-Setup.exe"
    Write-Host "  обновление: $weight"
    Write-Host ''
    Write-Host 'В раздачу НЕ отправлено — это сборка для себя.' -ForegroundColor Yellow
    Write-Host 'Дизайнеры этой версии не увидят.'
    exit 0
}

# ─────────────────────────────────────────────────────────────────────────────
# В раздачу — это и есть доставка
# ─────────────────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host 'Отправляю в раздачу…'
dotnet vpk upload github `
    --repoUrl "https://github.com/$Repo" `
    --token $token `
    --outputDir $cache `
    --releaseName "CupsForge $version" `
    --tag "v$version" `
    --publish true `
    --merge true
if ($LASTEXITCODE -ne 0) { throw 'Отправка в раздачу не удалась — версия никому не придёт.' }

# ─────────────────────────────────────────────────────────────────────────────
# Сетевой диск — ускорение для офиса, не условие
# ─────────────────────────────────────────────────────────────────────────────
# Копия на диске нужна только затем, чтобы в офисе обновление шло по локальной
# сети. Диска нет — ничего страшного: все получат то же самое из раздачи.
$shareRoot = Split-Path -Parent $OfficeShare
if (Test-Path $shareRoot) {
    New-Item -ItemType Directory -Force -Path $OfficeShare | Out-Null
    Copy-Item (Join-Path $cache '*') $OfficeShare -Force
    Write-Host "Копия на сетевом диске обновлена: $OfficeShare"
} else {
    Write-Host 'Сетевой диск недоступен — пропускаю. Все получат версию из раздачи.'
}

Write-Host ''
Write-Host 'Готово.' -ForegroundColor Green
Write-Host "  версия     : $version"
Write-Host "  обновление : $weight — столько скачают те, у кого программа стоит"
Write-Host "  установщик : $cache\CupsForge-win-Setup.exe (для НОВЫХ дизайнеров)"
Write-Host ''
Write-Host 'Больше ничего делать не нужно: у всех, у кого программа установлена,'
Write-Host 'при следующем запуске появится полоска «Доступна версия».'
Write-Host ''
Write-Host 'Каталог и шаблоны обновляются отдельно — push-templates.cmd.'
