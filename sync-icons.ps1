# Пересобирает CupsForge\Theme\Icons.xaml из файлов Icons\*.svg.
#
# Зачем скрипт, а не чтение SVG на лету: WPF не умеет SVG. Библиотеку ради
# двух десятков значков тянуть незачем — она весит больше, чем сами значки,
# и разбирает их при каждом запуске. Поэтому SVG остаются ИСТОЧНИКОМ, который
# правит дизайнер, а в программу едет разобранная геометрия.
#
# Порядок работы:
#   1. правите Icons\link.svg в любом редакторе (Illustrator, Figma, Inkscape);
#   2. запускаете sync-icons.cmd;
#   3. dotnet run --project Tests\SelfCheck — проверка сверит одно с другим.
#
# Забыли шаг 2 — самопроверка покраснеет и назовёт разошедшиеся значки.
# Молчаливого расхождения «в SVG одно, в программе другое» быть не может.

param(
    [switch] $WhatIf
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$iconsDir = Join-Path $root 'Icons'
$target   = Join-Path $root 'CupsForge\Theme\Icons.xaml'

if (-not (Test-Path $iconsDir)) { throw "Нет папки со значками: $iconsDir" }
if (-not (Test-Path $target))   { throw "Нет файла: $target" }

# --- Читаем SVG ---
# Имя файла превращается в ключ ресурса: check-circle.svg -> I.CheckCircle.
function ConvertTo-Key([string] $fileName) {
    $parts = $fileName -split '-'
    $out = ''
    foreach ($p in $parts) {
        if ($p.Length -gt 0) { $out += $p.Substring(0,1).ToUpper() + $p.Substring(1) }
    }
    return "I.$out"
}

$icons = [ordered] @{}
foreach ($file in Get-ChildItem $iconsDir -Filter '*.svg' | Sort-Object Name) {
    # Кодировка задаётся явно: Get-Content в Windows PowerShell 5.1 читает
    # файл без BOM как ANSI, и кириллица внутри разваливается.
    $text = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)

    # Собираем d= всех путей по порядку. Иконки заведомо состоят только из
    # <path>: circle и rect пришлось бы переводить в дуги здесь, а делать
    # геометрию в скрипте — значит завести второе место, где она живёт.
    $paths = [regex]::Matches($text, '<path[^>]*\sd="([^"]+)"')
    if ($paths.Count -eq 0) {
        Write-Host "Пропускаю $($file.Name): нет ни одного <path d=...>" -ForegroundColor Yellow
        continue
    }

    $shapes = [regex]::Matches($text, '<(circle|rect|ellipse|polyline|polygon|line)\b')
    if ($shapes.Count -gt 0) {
        Write-Host "ВНИМАНИЕ $($file.Name): найдены $($shapes.Count) фигур(ы) не-path — они потеряются." -ForegroundColor Yellow
        Write-Host "  В редакторе: Object -> Path -> Convert to path (или Flatten)." -ForegroundColor Yellow
    }

    $d = ($paths | ForEach-Object { $_.Groups[1].Value.Trim() }) -join ' '
    $d = ($d -replace '\s+', ' ').Trim()

    $icons[(ConvertTo-Key $file.BaseName)] = @{ Data = $d; File = $file.Name }
}

if ($icons.Count -eq 0) { throw 'Не найдено ни одной иконки.' }
Write-Host "Иконок прочитано: $($icons.Count)"

# --- Собираем секцию геометрии ---
$sb = [System.Text.StringBuilder]::new()
foreach ($key in $icons.Keys) {
    $null = $sb.AppendLine("    <!-- $($icons[$key].File) -->")
    $null = $sb.AppendLine("    <Geometry x:Key=""$key"">$($icons[$key].Data)</Geometry>")
}

$generated = $sb.ToString().TrimEnd()

# --- Врезаем между маркерами ---
$text = [System.IO.File]::ReadAllText($target, [System.Text.Encoding]::UTF8)
$begin = '<!-- НАЧАЛО АВТОСБОРКИ -->'
$end   = '<!-- КОНЕЦ АВТОСБОРКИ -->'

if ($text -notmatch [regex]::Escape($begin)) {
    throw "В $target нет маркера $begin — добавьте его вокруг блока <Geometry>."
}

$pattern = "(?s)" + [regex]::Escape($begin) + ".*?" + [regex]::Escape($end)
$replacement = "$begin`r`n$generated`r`n    $end"
$updated = [regex]::Replace($text, $pattern, { $replacement })

if ($updated -eq $text) {
    Write-Host 'Изменений нет — Icons.xaml уже соответствует SVG.' -ForegroundColor Green
    exit 0
}

if ($WhatIf) {
    Write-Host 'Только показываю (-WhatIf). Файл не тронут.' -ForegroundColor Yellow
    exit 0
}

# .xaml читается как UTF-8, BOM не нужен и мешает некоторым инструментам.
[System.IO.File]::WriteAllText($target, $updated, (New-Object System.Text.UTF8Encoding $false))
Write-Host "Обновлён: $target" -ForegroundColor Green
Write-Host ''
Write-Host 'Дальше: dotnet run --project Tests\SelfCheck'
