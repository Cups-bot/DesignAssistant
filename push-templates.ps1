# Выкладывает шаблоны и каталог в публичную раздачу на GitHub.
#
#   .\push-templates.cmd                 — выложить изменившееся
#   .\push-templates.cmd -Prune          — заодно удалить из раздачи то, чего больше нет
#   .\push-templates.cmd -Templates D:\… — взять шаблоны из другой папки
#
# Запускается там, где лежат шаблоны, — обычно на рабочем месте с диском Y:.
#
# Заливается ТОЛЬКО изменившееся: файлы адресуются отпечатком, поэтому правка
# одного шаблона — это одно вложение, а не четыреста мегабайт.
#
# Токен нужен только здесь, для записи. Дизайнерам для скачивания он не нужен:
# раздача открыта на чтение.

param(
    [string] $Templates = 'Y:\STAKANY\_Templates',
    [string] $Repo = 'Cups-bot/CupsForge-public',
    [string] $Tag = 'dist',
    [switch] $Prune
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# --- Токен ---
# Лежит в профиле пользователя, рядом с настройками программы, и в git не попадает.
$tokenPath = Join-Path $env:APPDATA 'CupsForge\github-token.txt'
if (Test-Path $tokenPath) {
    $token = (Get-Content $tokenPath -Raw).Trim()
} else {
    Write-Host 'Нужен токен GitHub с правом записи в репозиторий раздачи.'
    Write-Host 'Создать: github.com → Settings → Developer settings → Personal access tokens'
    Write-Host ''
    $token = (Read-Host 'Вставьте токен' ).Trim()
    if (-not $token) { throw 'Токен не введён.' }
    New-Item -ItemType Directory -Force -Path (Split-Path $tokenPath) | Out-Null
    Set-Content -Path $tokenPath -Value $token -Encoding ascii
    Write-Host "Сохранён: $tokenPath"
}

$headers = @{
    Authorization = "Bearer $token"
    'User-Agent'  = 'CupsForge-Publisher'
    Accept        = 'application/vnd.github+json'
}

if (-not (Test-Path $Templates)) { throw "Папка шаблонов не найдена: $Templates" }

# --- Выпуск-хранилище ---
# Один долгоживущий выпуск, в котором лежат все файлы. Вложения можно
# добавлять и удалять по отдельности — история при этом не растёт.
Write-Host "Раздача: $Repo (выпуск «$Tag»)"
try {
    $release = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/$Repo/releases/tags/$Tag"
} catch {
    Write-Host 'Выпуска ещё нет — создаю.'
    $release = Invoke-RestMethod -Headers $headers -Method Post `
        -Uri "https://api.github.com/repos/$Repo/releases" `
        -Body (@{ tag_name = $Tag; name = 'Раздача файлов'
                  body = 'Шаблоны и программа. Обновляется push-templates и publish.' } | ConvertTo-Json)
}

$existing = @{}
foreach ($a in $release.assets) { $existing[$a.name] = $a }
Write-Host "Сейчас в раздаче вложений: $($existing.Count)"

# --- Опись того, что должно быть ---
function Get-Sha256([string] $path) {
    (Get-FileHash -Path $path -Algorithm SHA256).Hash.ToLower()
}

$files = @()
$sourceRoot = (Resolve-Path $Templates).Path.TrimEnd('\')

Write-Host 'Считаю отпечатки…'
foreach ($f in Get-ChildItem $sourceRoot -Recurse -File) {
    # Каталог кладём отдельной записью: программа обновляет его вместе с шаблонами.
    $relative = $f.FullName.Substring($sourceRoot.Length + 1).Replace('\', '/')
    $sha = Get-Sha256 $f.FullName

    # Имя вложения — из отпечатка, а не из имени файла: GitHub переименовывает
    # вложения с пробелами и кириллицей, и угадывать потом нечем.
    $files += [pscustomobject]@{
        path   = $relative
        asset  = 'f-' + $sha.Substring(0, 16) + [System.IO.Path]::GetExtension($f.Name).ToLower()
        size   = $f.Length
        sha256 = $sha
        source = $f.FullName
    }
}
Write-Host "Файлов у источника: $($files.Count)"

# --- Заливаем недостающее ---
$uploaded = 0
$uploadUrl = $release.upload_url -replace '\{.*$', ''

foreach ($file in $files) {
    if ($existing.ContainsKey($file.asset)) { continue }

    Write-Host "  ↑ $($file.path)"
    $uploadHeaders = $headers.Clone()
    $uploadHeaders['Content-Type'] = 'application/octet-stream'

    Invoke-RestMethod -Headers $uploadHeaders -Method Post `
        -Uri "$uploadUrl`?name=$($file.asset)" `
        -InFile $file.source | Out-Null
    $uploaded++
}
Write-Host "Залито новых вложений: $uploaded"

# --- Опись ---
$catalog = $files | Where-Object { $_.path -eq 'catalog.json' } | Select-Object -First 1
$templates = $files | Where-Object { $_.path -ne 'catalog.json' }

$manifest = [ordered]@{
    generated = (Get-Date).ToString('s')
    app       = $null
    catalog   = if ($catalog) { [ordered]@{ path = $catalog.path; asset = $catalog.asset
                                            size = $catalog.size; sha256 = $catalog.sha256 } } else { $null }
    templates = @($templates | ForEach-Object {
        [ordered]@{ path = $_.path; asset = $_.asset; size = $_.size; sha256 = $_.sha256 }
    })
}

# Сведения о программе берём из прошлой описи — их пишет publish, не этот скрипт.
$manifestPath = Join-Path $root 'manifest.json'
if (Test-Path $manifestPath) {
    $previous = Get-Content $manifestPath -Raw | ConvertFrom-Json
    if ($previous.app) { $manifest.app = $previous.app }
}

[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6),
                               (New-Object System.Text.UTF8Encoding $false))
Write-Host "Опись обновлена: $manifestPath"

# --- Уборка ---
if ($Prune) {
    $needed = @{}
    foreach ($f in $files) { $needed[$f.asset] = $true }
    if ($manifest.app) { $needed[$manifest.app.asset] = $true }

    $removed = 0
    foreach ($name in $existing.Keys) {
        if ($needed.ContainsKey($name)) { continue }
        Write-Host "  × $name"
        Invoke-RestMethod -Headers $headers -Method Delete `
            -Uri "https://api.github.com/repos/$Repo/releases/assets/$($existing[$name].id)" | Out-Null
        $removed++
    }
    Write-Host "Удалено лишних вложений: $removed"
}

Write-Host ''
Write-Host 'Осталось отправить опись в репозиторий раздачи:'
Write-Host '  git add manifest.json && git commit -m "Обновлены шаблоны" && git push'
Write-Host ''
Write-Host 'После этого у дизайнеров при запуске появится обновление.'
