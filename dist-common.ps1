# Общая часть выкладки: токен и опись.
#
# Подключается из push-templates.ps1 (шаблоны) и publish.ps1 (программа).
# Обе половины пишут в ОДНУ опись, но каждая — только свой раздел:
# push-templates ведёт catalog и templates, publish ведёт app.
#
# Опись живёт в самом репозитории раздачи и правится через API. Раньше каждый
# скрипт писал manifest.json рядом с собой и советовал «сделайте git push» —
# но клона раздачи ни у кого нет, а два скрипта на двух машинах вели бы две
# разные описи. Здесь общий источник один и тот же для всех.

$ErrorActionPreference = 'Stop'

$script:TokenPath = Join-Path $env:APPDATA 'CupsForge\github-token.txt'

function New-DistHeaders {
    param([string] $Token)
    $h = @{ 'User-Agent' = 'CupsForge-Publisher'; Accept = 'application/vnd.github+json' }
    if ($Token) { $h['Authorization'] = "Bearer $Token" }
    return $h
}

<#
Годен ли ключ. Возвращает @{ who = кто; problem = что не так }.

Проверять одним лишь «кто я» бессмысленно: этот запрос проходит с ЛЮБЫМ живым
ключом, хоть вовсе без прав. Поэтому дополнительно смотрим, видит ли ключ сам
репозиторий раздачи, — так «ключ не выбран для этого репозитория» ловится сразу,
а не через три запроса под видом непонятного отказа.

Право на ЗАПИСЬ заранее не проверить: GitHub не отвечает на вопрос «а можно ли»,
он отвечает только на попытку. Поэтому отказ при первой записи разбирается
отдельно — см. Get-DistRefusal.
#>
function Test-DistToken {
    param([string] $Token, [string] $Repo)
    if (-not $Token) { return @{ who = $null; problem = 'ключ пустой' } }

    $headers = New-DistHeaders $Token
    try {
        $who = (Invoke-RestMethod 'https://api.github.com/user' -Headers $headers).login
    } catch {
        return @{ who = $null; problem = 'GitHub не принял ключ (истёк, отозван или в файл попало не то)' }
    }

    if ($Repo) {
        try {
            Invoke-RestMethod "https://api.github.com/repos/$Repo" -Headers $headers | Out-Null
        } catch {
            return @{ who = $who; problem = "ключ не видит репозиторий $Repo — в правах ключа он не выбран" }
        }
    }
    return @{ who = $who; problem = $null }
}

<#
Разбирает отказ на запись. GitHub отвечает «Resource not accessible by personal
access token» и не поясняет, чего именно не хватило, — а не хватает всегда
одного и того же: права записи. Возвращает понятный текст или $null.
#>
function Get-DistRefusal {
    param($ErrorRecord, [string] $Repo)

    $code = $ErrorRecord.Exception.Response.StatusCode.value__
    if ($code -ne 403 -and $code -ne 404) { return $null }

    return @"
GitHub отказал в записи в $Repo.

Ключ настоящий и репозиторий видит, но права записи у него нет.
Поправить: github.com → Settings → Developer settings →
           Personal access tokens → Fine-grained tokens → ваш ключ → Edit
  Repository access : Only select repositories → $Repo
  Permissions       : Repository permissions → Contents → Read and write

После правки запустите заново — ключ уже сохранён, спрашивать не будет.
"@
}

<#
Токен с правом записи в раздачу.

Сохранённый токен ПРОВЕРЯЕТСЯ, а не берётся на веру. Так уже было: в файл
попала строка запуска PowerShell вместо ключа, скрипт молча её сохранил и
дальше всегда получал «401 Несанкционированный» — без единой подсказки,
что дело в самом файле. Теперь негодный ключ виден сразу и спрашивается заново.
#>
function Get-DistToken {
    param([string] $Repo)

    if (Test-Path $script:TokenPath) {
        $token = (Get-Content $script:TokenPath -Raw).Trim()
        $check = Test-DistToken -Token $token -Repo $Repo
        if (-not $check.problem) {
            Write-Host "Ключ GitHub: $($check.who)"
            return $token
        }
        Write-Host ''
        Write-Host "Сохранённый ключ не годится: $($check.problem)" -ForegroundColor Yellow
        Write-Host "Файл: $script:TokenPath"
    }

    Write-Host ''
    Write-Host "Нужен ключ GitHub с правом записи в $Repo."
    Write-Host 'Создать: github.com → Settings → Developer settings →'
    Write-Host '         Personal access tokens → Fine-grained tokens → Generate new token'
    Write-Host "  Repository access : Only select repositories → $Repo"
    Write-Host '  Permissions       : Repository permissions → Contents → Read and write'
    Write-Host ''

    for ($try = 1; $try -le 3; $try++) {
        # Ключ не показываем на экране: консоль остаётся в истории и на скриншотах.
        $secure = Read-Host 'Вставьте ключ' -AsSecureString
        $token = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
                 [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)).Trim()
        if (-not $token) { Write-Host 'Пусто.' -ForegroundColor Yellow; continue }

        $check = Test-DistToken -Token $token -Repo $Repo
        if (-not $check.problem) {
            New-Item -ItemType Directory -Force -Path (Split-Path $script:TokenPath) | Out-Null
            Set-Content -Path $script:TokenPath -Value $token -Encoding ascii
            Write-Host "Ключ принят ($($check.who)) и сохранён: $script:TokenPath"
            return $token
        }
        Write-Host "Не годится: $($check.problem) (попытка $try из 3)" -ForegroundColor Yellow
    }
    throw 'Годный ключ так и не введён.'
}

<#
В пустом репозитории нельзя ни создать выпуск, ни положить опись: тег и файл
вешаются на коммит, а коммитов нет. GitHub отвечает на это невнятно, поэтому
делаем первый коммит сами.
#>
function Assert-DistBootstrapped {
    param([string] $Repo, [hashtable] $Headers)

    try {
        Invoke-RestMethod -Headers $Headers -Uri "https://api.github.com/repos/$Repo/commits?per_page=1" | Out-Null
        return
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -ne 409) { throw }
    }

    Write-Host 'Репозиторий раздачи пуст — делаю первый коммит.'
    $readme = @"
# CupsForge — раздача

Шаблоны, каталог продуктов и сама программа. Обновляется автоматически
скриптами `push-templates.cmd` и `publish.cmd` — руками сюда ничего не кладут.

* `manifest.json` — опись: что есть в раздаче и с какими отпечатками.
* Сами файлы лежат вложениями к выпуску `dist`.

Программа читает опись при запуске и скачивает только то, что у неё
отличается. Ключ для этого не нужен: раздача открыта на чтение.
"@
    try {
        Invoke-RestMethod -Headers $Headers -Method Put `
            -Uri "https://api.github.com/repos/$Repo/contents/README.md" `
            -Body (@{
                message = 'Раздача заведена'
                content = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($readme))
            } | ConvertTo-Json) | Out-Null
    } catch {
        $refusal = Get-DistRefusal -ErrorRecord $_ -Repo $Repo
        if ($refusal) { throw $refusal } else { throw }
    }
}

<# Опись из репозитория раздачи. Возвращает объект и его sha (нужен для записи). #>
function Get-DistManifest {
    param([string] $Repo, [hashtable] $Headers)

    try {
        $file = Invoke-RestMethod -Headers $Headers -Uri "https://api.github.com/repos/$Repo/contents/manifest.json"
        $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($file.content))
        return @{ doc = ($json | ConvertFrom-Json); sha = $file.sha }
    } catch {
        return @{ doc = $null; sha = $null }
    }
}

<# Кладёт опись в репозиторий раздачи. Это и есть «публикация». #>
function Publish-DistManifest {
    param([string] $Repo, [hashtable] $Headers, $Document, [string] $Sha, [string] $Message)

    $json = $Document | ConvertTo-Json -Depth 6
    $body = @{
        message = $Message
        content = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
    }
    # sha обязателен при замене существующего файла и запрещён при создании.
    if ($Sha) { $body['sha'] = $Sha }

    try {
        Invoke-RestMethod -Headers $Headers -Method Put `
            -Uri "https://api.github.com/repos/$Repo/contents/manifest.json" `
            -Body ($body | ConvertTo-Json) | Out-Null
    } catch {
        $refusal = Get-DistRefusal -ErrorRecord $_ -Repo $Repo
        if ($refusal) { throw $refusal } else { throw }
    }
}
