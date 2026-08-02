# Выкладывает шаблоны и каталог в публичную раздачу на GitHub.
#
#   .\push-templates.cmd -WhatIf         — примерка: что уедет, ничего не трогая
#   .\push-templates.cmd                 — выложить изменившееся
#   .\push-templates.cmd -Prune          — заодно удалить из раздачи то, чего больше нет
#   .\push-templates.cmd -Templates D:\… — взять шаблоны из другой папки
#
# Начинать всегда с -WhatIf: он считает отпечатки и показывает список, но не
# заливает и не требует токена. Дёшево увидеть, что уедет лишнее.
#
# Запускается там, где лежат шаблоны, — обычно на рабочем месте с диском Y:.
#
# Заливается ТОЛЬКО изменившееся: файлы адресуются отпечатком, поэтому правка
# одного шаблона — это одно вложение, а не четыреста мегабайт.
#
# Ключ нужен только здесь, для записи. Дизайнерам для скачивания он не нужен:
# раздача открыта на чтение.
#
# -Manifest <файл> — записать опись в файл вместо публикации в раздачу.
#                    Нужно для проверки: видно, что получилось, никого не задев.

param(
    [string] $Templates = 'Y:\STAKANY\_Templates',
    [string] $Repo = 'Cups-bot/CupsForge-public',
    [string] $Tag = 'dist',
    [string] $Manifest = '',
    [switch] $Prune,
    [switch] $NoBump,
    [switch] $WhatIf
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
. (Join-Path $root 'dist-common.ps1')

# Без этого PowerShell 5.1 рисует полоску прогресса на каждый кусок тела запроса
# и заливка замедляется в разы. На 400 мегабайтах это час против пяти минут.
$ProgressPreference = 'SilentlyContinue'

# Про папку спрашиваем раньше, чем про ключ: обидно ввести ключ и только потом
# узнать, что диск не подключён.
if (-not (Test-Path $Templates)) { throw "Папка шаблонов не найдена: $Templates" }

# --- Ключ ---
# В режиме -WhatIf ничего не пишем, поэтому ключ не нужен вовсе: раздача
# открыта на чтение. Так примерку может сделать кто угодно, ничего не заводя.
$token = ''
if ($WhatIf) {
    Write-Host 'Примерка (-WhatIf): ничего не заливается и не удаляется.'
} else {
    $token = Get-DistToken -Repo $Repo
    Assert-DistBootstrapped -Repo $Repo -Headers (New-DistHeaders $token)
}
$headers = New-DistHeaders $token

# --- Выпуск-хранилище ---
# Один долгоживущий выпуск, в котором лежат все файлы. Вложения можно
# добавлять и удалять по отдельности — история при этом не растёт.
Write-Host "Раздача: $Repo (выпуск «$Tag»)"
$release = $null
try {
    $release = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/$Repo/releases/tags/$Tag"
} catch {
    if ($WhatIf) {
        Write-Host 'Выпуска ещё нет (при настоящем прогоне он будет создан).'
    } else {
        Write-Host 'Выпуска ещё нет — создаю.'
        $release = Invoke-RestMethod -Headers $headers -Method Post `
            -Uri "https://api.github.com/repos/$Repo/releases" `
            -Body (@{ tag_name = $Tag; name = 'Раздача файлов'
                      body = 'Шаблоны и программа. Обновляется push-templates и publish.' } | ConvertTo-Json)
    }
}

# --- Что уже лежит в раздаче ---
# Список вложений берём отдельной ручкой с постраничностью, а не из тела выпуска:
# там он обрезан, и на сотне файлов скрипт решил бы, что залитого нет, полез
# заливать заново и получил бы отказ «имя занято».
#
# Учитываются только вложения в состоянии uploaded. Сорванная заливка оставляет
# запись в состоянии starter: имя занято, содержимого нет. Если считать такое
# вложение доставленным, файл молча не доедет до дизайнера.
$existing = @{}
$broken = @()
if ($release) {
    $page = 1
    while ($true) {
        # ВНИМАНИЕ: ответ НЕЛЬЗЯ оборачивать в @(...) прямо на вызове.
        # Invoke-RestMethod отдаёт массив одним объектом, и @(Invoke-RestMethod …)
        # даёт массив ИЗ ОДНОГО элемента, внутри которого лежит настоящая сотня.
        # Прогон тогда видел «в раздаче 1 вложение» вместо 290 и собирался залить
        # всё заново — а GitHub на каждое имя отвечал бы «уже занято».
        # Сначала в переменную, и только потом можно нормализовать.
        $response = Invoke-RestMethod -Headers $headers `
            -Uri "https://api.github.com/repos/$Repo/releases/$($release.id)/assets?per_page=100&page=$page"
        $batch = @($response)

        if ($batch.Count -eq 0) { break }
        foreach ($a in $batch) {
            if ($a.state -eq 'uploaded') { $existing[$a.name] = $a } else { $broken += $a }
        }
        if ($batch.Count -lt 100) { break }
        $page++
    }
}
Write-Host "Сейчас в раздаче вложений: $($existing.Count)"

# Битое вложение занимает имя, поэтому поверх него не зальёшь — сначала убираем.
foreach ($a in $broken) {
    Write-Host "  ! недокачанное вложение: $($a.name) ($($a.state)) — убираю, зальётся заново"
    if (-not $WhatIf) {
        Invoke-RestMethod -Headers $headers -Method Delete `
            -Uri "https://api.github.com/repos/$Repo/releases/assets/$($a.id)" | Out-Null
    }
}

# --- Опись того, что должно быть ---
function Get-Sha256([string] $path) {
    (Get-FileHash -Path $path -Algorithm SHA256).Hash.ToLower()
}

# --- Версия каталога ---
# Каталог версионируется САМ, при выкладке.
#
# Требовать помнить про «version» перед каждой правкой бессмысленно: правят
# каталог редко и урывками, а забытая версия хуже отсутствующей — программа
# показывает «v2» и там, и там, и понять, у кого старый каталог, нельзя.
#
# Поднимаем ровно тогда, когда содержимое действительно изменилось: сверяем
# отпечаток файла с тем, что уже опубликовано. Повторный прогон без правок
# версию не двигает.
function Update-CatalogVersion {
    param([string] $CatalogPath, [object] $PublishedCatalog, [switch] $Pretend)

    if (-not (Test-Path $CatalogPath)) { return $null }

    $sha = Get-Sha256 $CatalogPath
    if ($PublishedCatalog -and $PublishedCatalog.sha256 -eq $sha) {
        Write-Host 'Каталог не менялся — версию не трогаю.'
        return $null
    }

    # Кодировка явно: Get-Content в PowerShell 5.1 читает UTF-8 без BOM как ANSI,
    # и кириллица в названиях продуктов развалилась бы прямо в файле.
    $text = [System.IO.File]::ReadAllText($CatalogPath, [System.Text.Encoding]::UTF8)

    $verMatch = [regex]::Match($text, '"version"\s*:\s*(\d+)')
    if (-not $verMatch.Success) {
        Write-Host 'В catalog.json нет поля "version" — пропускаю подпечатку.' -ForegroundColor Yellow
        return $null
    }

    $next = [int] $verMatch.Groups[1].Value + 1
    $today = (Get-Date).ToString('yyyy-MM-dd')

    if ($Pretend) {
        Write-Host "Каталог изменился: версия стала бы $next от $today (примерка)."
        return $next
    }

    # Правим только два поля, остальной файл — включая комментарии и порядок —
    # остаётся байт в байт. Разбирать и пересобирать JSON значило бы потерять
    # комментарии, ради которых схема и заведена.
    $text = [regex]::Replace($text, '"version"\s*:\s*\d+', ('"version": ' + $next), 1)
    $text = [regex]::Replace($text, '"updated"\s*:\s*"[^"]*"', ('"updated": "' + $today + '"'), 1)

    [System.IO.File]::WriteAllText($CatalogPath, $text, (New-Object System.Text.UTF8Encoding $false))
    Write-Host "Каталог изменился: версия поднята до $next от $today." -ForegroundColor Green
    return $next
}

# Опись нужна ДО подсчёта отпечатков: по ней видно, менялся ли каталог.
$published = if ($WhatIf) { @{ doc = $null; sha = $null } } else { Get-DistManifest -Repo $Repo -Headers $headers }

if (-not $NoBump) {
    $null = Update-CatalogVersion -CatalogPath (Join-Path $Templates 'catalog.json') `
                                  -PublishedCatalog $published.doc.catalog `
                                  -Pretend:$WhatIf
}

$files = @()
$sourceRoot = (Resolve-Path $Templates).Path.TrimEnd('\')

# --- Что не выкладывать ---
# Мусор Windows отсекается всегда. Остальное — по желанию: рядом с шаблонами
# можно положить .distignore, по строке на образец, и эти пути уедут мимо раздачи.
# Пример: папка All весит 306 МБ и программе не нужна вовсе.
$skip = @('Thumbs.db', 'desktop.ini', '*.tmp', '~$*',
         'push-templates.*', 'manifest.json', '.distignore')
$builtIn = $skip.Count

$ignorePath = Join-Path $sourceRoot '.distignore'
if (Test-Path $ignorePath) {
    # Кодировку указываем явно. PowerShell 5.1 без неё читает UTF-8 как ANSI,
    # и строка «под вопросом» превращается в «РїРѕРґ РІРѕРїСЂРѕСЃРѕРј» — правило
    # молча перестаёт совпадать, а в раздачу уезжает лишнее.
    $skip += (Get-Content $ignorePath -Encoding UTF8 |
              ForEach-Object { $_.Trim() } |
              Where-Object { $_ -and -not $_.StartsWith('#') })
    Write-Host "Исключений из .distignore: $($skip.Count - $builtIn)"
}

function Test-Skip([string] $relative) {
    foreach ($pattern in $skip) {
        if ($relative -like $pattern -or (Split-Path $relative -Leaf) -like $pattern) { return $true }
        # Образец без звёздочки, совпавший с началом пути, — это папка целиком.
        if ($pattern -notmatch '[*?]' -and $relative -like "$pattern/*") { return $true }
    }
    return $false
}

$skipped = 0
Write-Host 'Считаю отпечатки…'
# -Force обязателен. Без него Get-ChildItem не показывает файлы с признаком
# «скрытый», а такие среди шаблонов есть (2_Offset\HB\*.ai) — они молча
# не доезжали бы до дизайнеров. Пусть лучше всё будет видно, а лишнее
# отсекается явными правилами: тогда пропуск виден в отчёте, а не угадывается.
foreach ($f in Get-ChildItem $sourceRoot -Recurse -File -Force) {
    $rel = $f.FullName.Substring($sourceRoot.Length + 1).Replace('\', '/')
    if (Test-Skip $rel) { $skipped++; continue }

    $sha = Get-Sha256 $f.FullName

    # Имя вложения — из отпечатка, а не из имени файла: GitHub переименовывает
    # вложения с пробелами и кириллицей, и угадывать потом нечем.
    $files += [pscustomobject]@{
        path   = $rel
        asset  = 'f-' + $sha.Substring(0, 16) + [System.IO.Path]::GetExtension($f.Name).ToLower()
        size   = $f.Length
        sha256 = $sha
        source = $f.FullName
    }
}
Write-Host "Файлов к выкладке: $($files.Count) (пропущено: $skipped)"

# --- Заливаем недостающее ---
# Одинаковые по содержимому файлы получают одно имя вложения — это не сбой,
# а свойство адресации по отпечатку: платим за содержимое, а не за копии.
# Но заливать его надо один раз, иначе второй заход получит отказ «имя занято»
# и уронит весь прогон. Поэтому уже залитое сразу отмечаем в $existing.
$dup = ($files | Group-Object asset | Where-Object { $_.Count -gt 1 } | Measure-Object).Count
if ($dup) { Write-Host "Одинаковых по содержимому файлов: $dup (зальются по разу)" }

$uploaded = 0
$uploadUrl = if ($release) { $release.upload_url -replace '\{.*$', '' } else { '' }

foreach ($file in $files) {
    if ($existing.ContainsKey($file.asset)) { continue }

    Write-Host "  ↑ $($file.path)"
    if ($WhatIf) {
        $existing[$file.asset] = 'примерка'
        $uploaded++
        continue
    }

    $uploadHeaders = $headers.Clone()
    $uploadHeaders['Content-Type'] = 'application/octet-stream'

    # Сеть моргает, а прогон длинный: одна осечка на четырёхстах файлах не должна
    # означать «начать сначала». Три попытки, потом сдаёмся с понятным сообщением.
    $attempt = 0
    while ($true) {
        $attempt++
        try {
            $asset = Invoke-RestMethod -Headers $uploadHeaders -Method Post `
                -Uri "$uploadUrl`?name=$($file.asset)" `
                -InFile $file.source
            $existing[$file.asset] = $asset
            $uploaded++
            break
        } catch {
            # Отказ по правам повторять бессмысленно — он не рассосётся.
            $refusal = Get-DistRefusal -ErrorRecord $_ -Repo $Repo
            if ($refusal) { throw $refusal }

            if ($attempt -ge 3) { throw "Не удалось залить $($file.path): $($_.Exception.Message)" }
            Write-Host "    осечка ($attempt из 3), пробую снова: $($_.Exception.Message)"
            Start-Sleep -Seconds (3 * $attempt)
        }
    }
}
Write-Host "$(if ($WhatIf) { 'Залилось бы новых вложений' } else { 'Залито новых вложений' }): $uploaded"

# --- Опись ---
# ВНИМАНИЕ, грабли, на которые тут наступали дважды.
# Имена переменных в PowerShell нечувствительны к регистру, а параметры
# объявлены как [string] $Templates и [string] $Manifest. Локальные $templates
# и $manifest молча приводились к строке: список шаблонов превращался в " ",
# а вся опись — в текст «System.Collections.Specialized.OrderedDictionary».
# Прогон при этом выглядел успешным — вложения залиты, а качать дизайнеру нечего.
# Поэтому здесь $templateFiles и $manifestDoc, а не то, что просится.
$catalog = $files | Where-Object { $_.path -eq 'catalog.json' } | Select-Object -First 1
$templateFiles = @($files | Where-Object { $_.path -ne 'catalog.json' })

$manifestDoc = [ordered]@{
    generated = (Get-Date).ToString('s')
    app       = $null
    catalog   = if ($catalog) {
        # Версия и дата дублируются в описи НАМЕРЕННО: так программа видит,
        # отстал ли её каталог, не скачивая его целиком.
        $meta = [System.IO.File]::ReadAllText((Join-Path $Templates 'catalog.json'), [System.Text.Encoding]::UTF8)
        $v = [regex]::Match($meta, '"version"\s*:\s*(\d+)')
        $u = [regex]::Match($meta, '"updated"\s*:\s*"([^"]*)"')
        [ordered]@{ path = $catalog.path; asset = $catalog.asset
                    size = $catalog.size; sha256 = $catalog.sha256
                    version = if ($v.Success) { [int] $v.Groups[1].Value } else { 0 }
                    updated = if ($u.Success) { $u.Groups[1].Value } else { '' } }
    } else { $null }
    templates = @($templateFiles | ForEach-Object {
        [ordered]@{ path = $_.path; asset = $_.asset; size = $_.size; sha256 = $_.sha256 }
    })
}

# Сторож ровно на этот случай: пустая опись означает, что раздача есть,
# а пользы от неё ноль. Лучше упасть здесь, чем выложить пустышку.
if ($manifestDoc.templates.Count -eq 0) {
    throw 'В описи нет ни одного шаблона — выкладывать нечего, что-то не так.'
}
if (-not $manifestDoc.catalog) {
    throw "В папке шаблонов нет catalog.json — без него программа работать не будет."
}

# Сведения о программе берём из той описи, что уже лежит в раздаче: их пишет
# publish.ps1, возможно с другой машины. Свой раздел мы перезаписываем, чужой —
# переносим как есть.
$current = $published
if ($current.doc -and $current.doc.app) { $manifestDoc.app = $current.doc.app }

$weight = '{0:N0} МБ' -f (($files | Measure-Object size -Sum).Sum / 1MB)
if ($WhatIf -and $Manifest) {
    # Примерка с указанным файлом: опись получить можно, никого при этом не задев.
    # Так её видно глазами и можно скормить программе на проверку.
    [System.IO.File]::WriteAllText($Manifest, ($manifestDoc | ConvertTo-Json -Depth 6),
                                   (New-Object System.Text.UTF8Encoding $false))
    Write-Host "Опись записана в файл (примерка): $Manifest ($($manifestDoc.templates.Count) шаблонов, $weight)"
} elseif ($WhatIf) {
    Write-Host "Опись НЕ записана (примерка). В ней было бы: $($manifestDoc.templates.Count) шаблонов + каталог, всего $weight."
} elseif ($Manifest) {
    # Явно попросили файл — значит идёт проверка, в раздачу не публикуем.
    [System.IO.File]::WriteAllText($Manifest, ($manifestDoc | ConvertTo-Json -Depth 6),
                                   (New-Object System.Text.UTF8Encoding $false))
    Write-Host "Опись записана в файл: $Manifest ($($manifestDoc.templates.Count) шаблонов, $weight)"
} else {
    Publish-DistManifest -Repo $Repo -Headers $headers -Document $manifestDoc -Sha $current.sha `
                         -Message "Шаблоны: $($manifestDoc.templates.Count) файлов"
    Write-Host "Опись опубликована в $Repo ($($manifestDoc.templates.Count) шаблонов, $weight)"
}

# --- Уборка ---
# Уборка работает только на настоящем прогоне: в примерке $existing уже
# засорён тем, что «залилось бы», и удалять по нему нечего и незачем.
if ($Prune -and -not $WhatIf) {
    $needed = @{}
    foreach ($f in $files) { $needed[$f.asset] = $true }
    if ($manifestDoc.app) { $needed[$manifestDoc.app.asset] = $true }

    $removed = 0
    foreach ($name in @($existing.Keys)) {
        if ($needed.ContainsKey($name)) { continue }
        Write-Host "  × $name"
        Invoke-RestMethod -Headers $headers -Method Delete `
            -Uri "https://api.github.com/repos/$Repo/releases/assets/$($existing[$name].id)" | Out-Null
        $removed++
    }
    Write-Host "Удалено лишних вложений: $removed"
} elseif ($Prune) {
    Write-Host 'Уборка (-Prune) в примерке не считается — запустите без -WhatIf.'
}

if (-not $WhatIf -and -not $Manifest) {
    Write-Host ''
    Write-Host 'Готово. У дизайнеров при следующем запуске появится обновление шаблонов.'
}
