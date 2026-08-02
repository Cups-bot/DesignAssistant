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
    [string] $Notes = '',
    [switch] $SkipDist
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

# --- Выкладка на сетевой диск ---
# Это офисный канал, и он есть не всегда: из дома Y: не виден вообще. Раньше
# скрипт на этом падал — собрал, прогнал самопроверку и умер на копировании,
# то есть выложить что-либо из дома было нельзя в принципе. А раздача, которая
# и достаёт до всех, работает откуда угодно. Поэтому недоступный диск —
# это предупреждение, а не остановка.
$networkDone = $false
$exe = Join-Path $staging 'CupsForge.exe'

# Проверка «есть ли куда класть» относится ТОЛЬКО к сетевому диску по умолчанию.
# Если папку назвали явно, значит её и хотят — создаём. Раньше условие было
# общим, и `publish.ps1 -Destination D:\куда-нибудь` молча не делал ничего:
# скрипт собирал, прогонял самопроверку и заканчивался словами «выложено»
# ни во что.
$isDefaultDestination = $Destination -eq 'Y:\Soft\CupsForge\release'
$destinationReady = if ($isDefaultDestination) {
    Test-Path (Split-Path -Parent $Destination)
} else {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $true
}

if ($destinationReady) {
    $target = Join-Path $Destination $version
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item $exe $target -Force
    Write-Host "Выложено: $target"

    # Установщик — рядом с раздачей, чтобы дизайнер запускал его прямо с диска.
    Copy-Item (Join-Path $root 'install.cmd') $Destination -Force
    Copy-Item (Join-Path $root 'install.ps1') $Destination -Force
    Write-Host "Установщик обновлён: $Destination\install.cmd"

    # appsettings.json к версии НЕ прикладывается. Раньше это был мост, пока
    # в настройках не было полей для ключа Bitrix. Поля есть, а раздача теперь
    # публичная — и ключ уехал бы вместе с ней. Каждый вводит его у себя:
    # шестерёнка → ДОСТУП К BITRIX.
    $stray = Join-Path $target 'appsettings.json'
    if (Test-Path $stray) {
        Remove-Item $stray -Force
        Write-Host 'Убран appsettings.json, оставшийся от прошлой публикации.' -ForegroundColor Yellow
    }

    # Указатель на свежую версию — по нему установщик понимает, что ставить.
    $latest = [ordered] @{ version = $version; folder = $version; notes = $Notes }
    $latestPath = Join-Path $Destination 'latest.json'
    # Out-File -Encoding utf8 в Windows PowerShell дописывает BOM. Программа его
    # переваривает, но JSON с BOM — источник сюрпризов, пишем без него.
    [System.IO.File]::WriteAllText($latestPath, ($latest | ConvertTo-Json),
                                   (New-Object System.Text.UTF8Encoding $false))
    Write-Host "Обновлён указатель: $latestPath"
    $networkDone = $true
} else {
    Write-Host ''
    Write-Host "Сетевой диск недоступен: $Destination" -ForegroundColor Yellow
    Write-Host 'Пропускаю — выкладываю только в раздачу. До удалённых дизайнеров'
    Write-Host 'версия доедет, до офисных (они обновляются с диска) — нет.'
    Write-Host 'Чтобы доехала и до них, запустите это же из офиса.'
}

# --- Заливка в публичную раздачу ---
# Без неё новая версия доедет только до офиса. Раздача — единственный канал,
# который достаёт до домашних машин.
. (Join-Path $root 'dist-common.ps1')
$ProgressPreference = 'SilentlyContinue'

$repo = 'Cups-bot/CupsForge-public'
$tag  = 'dist'
if ($SkipDist) {
    Write-Host ''
    Write-Host 'В раздачу не заливаю (-SkipDist) — только на сетевой диск.' -ForegroundColor Yellow
    Write-Host 'Удалённые дизайнеры эту версию не увидят.'
} else {
    $token = Get-DistToken -Repo $repo
    $headers = New-DistHeaders $token
    Assert-DistBootstrapped -Repo $repo -Headers $headers

    Write-Host ''
    Write-Host 'Заливаю программу в раздачу…'
    try {
        $release = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/$repo/releases/tags/$tag"
    } catch {
        $release = Invoke-RestMethod -Headers $headers -Method Post `
            -Uri "https://api.github.com/repos/$repo/releases" `
            -Body (@{ tag_name = $tag; name = 'Раздача файлов' } | ConvertTo-Json)
    }

    # Берём файл из сборочной папки, а не с сетевого диска: диска может не быть.
    $sha = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
    $assetName = "app-$version.exe"

    # Одноимённое вложение заменяем: перезалить поверх нельзя.
    foreach ($a in $release.assets) {
        if ($a.name -eq $assetName) {
            Invoke-RestMethod -Headers $headers -Method Delete `
                -Uri "https://api.github.com/repos/$repo/releases/assets/$($a.id)" | Out-Null
        }
    }

    $uploadHeaders = $headers.Clone()
    $uploadHeaders['Content-Type'] = 'application/octet-stream'
    $uploadUrl = ($release.upload_url -replace '\{.*$', '') + "?name=$assetName"
    Invoke-RestMethod -Headers $uploadHeaders -Method Post -Uri $uploadUrl -InFile $exe | Out-Null

    # Опись: дописываем ТОЛЬКО раздел app. Шаблоны и каталог ведёт push-templates,
    # возможно с другой машины, — берём их из раздачи и кладём обратно как есть.
    $current = Get-DistManifest -Repo $repo -Headers $headers
    $doc = if ($current.doc) { $current.doc } else {
        [pscustomobject]@{ generated = ''; app = $null; catalog = $null; templates = @() }
    }

    $doc | Add-Member -NotePropertyName generated -NotePropertyValue (Get-Date).ToString('s') -Force
    $doc | Add-Member -NotePropertyName app -NotePropertyValue ([ordered]@{
        version = $version; notes = $Notes; asset = $assetName
        size = (Get-Item $exe).Length; sha256 = $sha
    }) -Force

    if (-not $doc.templates -or @($doc.templates).Count -eq 0) {
        Write-Host 'В раздаче нет шаблонов — программа доедет, а шаблоны нет.' -ForegroundColor Yellow
        Write-Host 'Запустите push-templates.cmd.'
    }

    Publish-DistManifest -Repo $repo -Headers $headers -Document $doc -Sha $current.sha `
                         -Message "Программа $version"
    Write-Host "Программа в раздаче: $assetName"
    Write-Host "Опись обновлена в $repo"
}

Write-Host ''
if ($networkDone -or -not $SkipDist) {
    Remove-Item $staging -Recurse -Force
    Write-Host 'Готово. У дизайнеров при следующем запуске появится полоска «Доступна версия».'
} else {
    # Никуда не доехало — собранный файл НЕ удаляем и говорим, где он лежит.
    # Раньше скрипт стирал сборку и в этом случае: человек ждал несколько минут
    # и не получал ни выкладки, ни файла, который можно запустить руками.
    Write-Host 'Никуда не выложено: сетевого диска нет, в раздачу заливать запретили (-SkipDist).' -ForegroundColor Yellow
    Write-Host ''
    Write-Host 'Собранная программа осталась здесь:' -ForegroundColor Cyan
    Write-Host "  $exe"
    Write-Host 'Её можно запустить как есть — .NET на машине не нужен, всё внутри файла.'
}
Write-Host 'Каталог продуктов обновляется отдельно — правкой catalog.json в папке шаблонов.'
