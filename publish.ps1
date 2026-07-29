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

# Установщик — рядом с раздачей, чтобы дизайнер запускал его прямо с сетевого диска.
Copy-Item (Join-Path $root 'install.cmd') $Destination -Force
Copy-Item (Join-Path $root 'install.ps1') $Destination -Force
Write-Host "Установщик обновлён: $Destination\install.cmd"

# appsettings.json к версии НЕ прикладывается. Раньше это был мост, пока в настройках
# не было полей для ключа Bitrix. Поля есть, а раздача теперь может стать публичной —
# и ключ вместе с ней. Каждый вводит его у себя: шестерёнка → ДОСТУП К BITRIX.
$stray = Join-Path $target 'appsettings.json'
if (Test-Path $stray) {
    Remove-Item $stray -Force
    Write-Host 'Убран appsettings.json, оставшийся от прошлой публикации.' -ForegroundColor Yellow
}

# --- Указатель на свежую версию ---
$latest = [ordered] @{ version = $version; folder = $version; notes = $Notes }
$latestPath = Join-Path $Destination 'latest.json'
# Out-File -Encoding utf8 в Windows PowerShell дописывает BOM. Программа его
# переваривает, но JSON с BOM — источник сюрпризов, пишем без него.
[System.IO.File]::WriteAllText($latestPath, ($latest | ConvertTo-Json),
                               (New-Object System.Text.UTF8Encoding $false))
Write-Host "Обновлён указатель: $latestPath"

# --- Заливка в публичную раздачу ---
# Без неё новая версия доедет только до офиса. Раздача — единственный канал,
# который достаёт до домашних машин.
$tokenPath = Join-Path $env:APPDATA 'CupsForge\github-token.txt'
if (Test-Path $tokenPath) {
    $token = (Get-Content $tokenPath -Raw).Trim()
    $headers = @{ Authorization = "Bearer $token"; 'User-Agent' = 'CupsForge-Publisher'
                  Accept = 'application/vnd.github+json' }
    $repo = 'Cups-bot/CupsForge-public'
    $tag  = 'dist'

    Write-Host ''
    Write-Host 'Заливаю программу в раздачу…'
    try {
        $release = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/$repo/releases/tags/$tag"
    } catch {
        $release = Invoke-RestMethod -Headers $headers -Method Post `
            -Uri "https://api.github.com/repos/$repo/releases" `
            -Body (@{ tag_name = $tag; name = 'Раздача файлов' } | ConvertTo-Json)
    }

    $exe = Join-Path $target 'CupsForge.exe'
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

    # Опись: программу дописываем, шаблоны не трогаем — их ведёт push-templates.
    $manifestPath = Join-Path $root 'manifest.json'
    $manifest = if (Test-Path $manifestPath) {
        Get-Content $manifestPath -Raw | ConvertFrom-Json
    } else {
        [pscustomobject]@{ generated = ''; app = $null; catalog = $null; templates = @() }
    }

    $manifest.generated = (Get-Date).ToString('s')
    $manifest | Add-Member -NotePropertyName app -NotePropertyValue ([ordered]@{
        version = $version; notes = $Notes; asset = $assetName
        size = (Get-Item $exe).Length; sha256 = $sha
    }) -Force

    [System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6),
                                   (New-Object System.Text.UTF8Encoding $false))
    Write-Host "Программа в раздаче: $assetName"
    Write-Host "Опись обновлена: $manifestPath"
    Write-Host 'Не забудьте отправить опись: git add manifest.json && git commit && git push'
} else {
    Write-Host ''
    Write-Host 'Токен GitHub не найден — в раздачу не заливаю, только на сетевой диск.' -ForegroundColor Yellow
    Write-Host 'Удалённые дизайнеры эту версию не увидят. Токен спросит push-templates.cmd.'
}

Remove-Item $staging -Recurse -Force

Write-Host ''
Write-Host 'Готово. У дизайнеров при следующем запуске появится полоска «Доступна версия».'
Write-Host 'Каталог продуктов обновляется отдельно — правкой catalog.json в папке шаблонов.'
