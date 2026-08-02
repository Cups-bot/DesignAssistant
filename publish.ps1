# Выпуск новой версии CupsForge.
#
#   .\publish.ps1                        — собрать и выложить на Y:\Soft\CupsForge\release
#   .\publish.ps1 -Notes "добавлены крышки"
#   .\publish.ps1 -Destination D:\test   — то же в другую папку (проверить, не трогая рабочую)
#   .\publish.ps1 -Destination D:\test -SkipDist
#       — собрать «как для дизайнеров», никуда не отправляя. Это то, что нужно
#         на домашней машине: сетевого диска нет, ключа GitHub нет, а посмотреть
#         на готовый Setup.exe надо.
#
#   Чтобы просто ЗАПУСТИТЬ и посмотреть окно, выпуск не нужен вовсе:
#       dotnet run --project CupsForge
#
# Прав администратора не требуется ни здесь, ни у дизайнеров: программа живёт
# в профиле пользователя.

param(
    [string] $Destination = 'Y:\Soft\CupsForge\release',
    [string] $Notes = '',
    [switch] $SkipDist
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# ─────────────────────────────────────────────────────────────────────────────
# Версия
# ─────────────────────────────────────────────────────────────────────────────
# Старшая часть берётся из проекта (её меняют осознанно, когда меняется
# сама программа), младшая — число коммитов. Забыть поднять версию невозможно:
# любой коммит её двигает, и она всегда больше предыдущей.
#
# Не теги: тегов в репозитории нет, а схема, требующая ручного действия перед
# каждым выпуском, ровно тем и плоха, от чего мы уходим.

[xml] $proj = Get-Content (Join-Path $root 'CupsForge\CupsForge.csproj')
$declared = ($proj.Project.PropertyGroup.Version | Where-Object { $_ }) -as [string]
if (-not $declared) { throw 'В CupsForge.csproj не задан <Version>.' }

$parts = $declared.Split('.')
$commits = (git -C $root rev-list --count HEAD).Trim()
if (-not $commits) { throw 'Не удалось спросить у git число коммитов.' }

$version = "$($parts[0]).$($parts[1]).$commits"
Write-Host "Версия: $version  (из <Version>$declared> и $commits коммитов)"

# Примечание к выпуску: что увидят дизайнеры в полоске обновления.
# Не указали — берём заголовок последнего коммита: он почти всегда и есть
# ответ на вопрос «что изменилось».
if (-not $Notes) {
    $Notes = (git -C $root log -1 --pretty=%s).Trim()
    Write-Host "Примечание из последнего коммита: $Notes"
}

# ─────────────────────────────────────────────────────────────────────────────
# Самопроверка — не выкладываем то, что не проходит собственные тесты
# ─────────────────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host 'Самопроверка…'
dotnet run --project (Join-Path $root 'Tests\SelfCheck') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Самопроверка не прошла — выпуск отменён.' }

# ─────────────────────────────────────────────────────────────────────────────
# Сборка
# ─────────────────────────────────────────────────────────────────────────────
# Публикуем ПАПКОЙ, а не одним файлом: дельта-обновления считаются по файлам.
# Единый сжатый blob менялся бы целиком, и «сдвинуть кнопку» стоило бы
# дизайнеру восьмидесяти мегабайт вместо сотни килобайт.

$staging = Join-Path $env:TEMP "cupsforge_build_$version"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }

Write-Host ''
Write-Host 'Сборка…'
dotnet publish (Join-Path $root 'CupsForge\CupsForge.csproj') -c Release -o $staging `
    -p:Version=$version --nologo
if ($LASTEXITCODE -ne 0) { throw 'Сборка не удалась.' }

# ─────────────────────────────────────────────────────────────────────────────
# Куда кладём
# ─────────────────────────────────────────────────────────────────────────────
# Проверка «есть ли куда» относится ТОЛЬКО к сетевому диску по умолчанию:
# из дома Y: не виден, и это не повод останавливаться. Названную явно папку
# создаём — раз назвали, значит туда и хотят.

$isDefault = $Destination -eq 'Y:\Soft\CupsForge\release'
$networkReady = if ($isDefault) {
    Test-Path (Split-Path -Parent $Destination)
} else {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $true
}

# Папка выпусков — она же канал обновления: Velopack читает её напрямую.
# Складываем СРАЗУ туда, где лежат прошлые выпуски: без них дельту не с чем
# считать, и каждое обновление снова весило бы как полная программа.
$releases = if ($networkReady) { $Destination } else { Join-Path $root 'release-local' }

if (-not $networkReady) {
    Write-Host ''
    Write-Host "Сетевой диск недоступен: $Destination" -ForegroundColor Yellow
    Write-Host "Собираю в локальную папку: $releases"
}
New-Item -ItemType Directory -Force -Path $releases | Out-Null

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
    --outputDir $releases
if ($LASTEXITCODE -ne 0) { throw 'Упаковка не удалась.' }

Remove-Item $notesFile -Force -ErrorAction SilentlyContinue
Remove-Item $staging -Recurse -Force

$setup = Join-Path $releases 'CupsForge-win-Setup.exe'
$delta = Get-ChildItem $releases -Filter "CupsForge-$version-delta.nupkg" -ErrorAction SilentlyContinue

Write-Host ''
Write-Host "Готово: $releases" -ForegroundColor Green
Write-Host "  установщик : $setup"
if ($delta) {
    $kb = [int]($delta.Length / 1KB)
    Write-Host "  дельта     : $kb КБ — столько скачают те, у кого стоит прошлая версия"
} else {
    Write-Host '  дельта     : нет (первый выпуск либо прошлых версий рядом не оказалось)'
}

# ─────────────────────────────────────────────────────────────────────────────
# Публичная раздача — единственный канал, достающий до домашних машин
# ─────────────────────────────────────────────────────────────────────────────
if ($SkipDist) {
    Write-Host ''
    Write-Host 'В раздачу не заливаю (-SkipDist). Удалённые дизайнеры версию не увидят.' -ForegroundColor Yellow
    exit 0
}

. (Join-Path $root 'dist-common.ps1')
$repo = 'Cups-bot/CupsForge-public'
$token = Get-DistToken -Repo $repo

Write-Host ''
Write-Host 'Заливаю в раздачу…'
dotnet vpk upload github `
    --repoUrl "https://github.com/$repo" `
    --token $token `
    --outputDir $releases `
    --releaseName "CupsForge $version" `
    --tag "v$version" `
    --merge
if ($LASTEXITCODE -ne 0) { throw 'Заливка в раздачу не удалась.' }

Write-Host ''
Write-Host 'Готово. У дизайнеров при следующем запуске появится полоска «Доступна версия».'
Write-Host 'Каталог продуктов и шаблоны обновляются отдельно — push-templates.cmd.'
