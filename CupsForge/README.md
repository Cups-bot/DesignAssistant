# CupsForge

Отдельное приложение (.NET 8 WPF) для направления **MyCups**. Тянет параметры заказа
из Bitrix24 по ссылке, показывает их для сверки и по кнопке создаёт проект
(папка + `.ai`-шаблон + `args.txt` + запуск Illustrator) — той же логикой, что ручной
редактор `DesignAssistant`.

## Поток работы

1. Пользователь вставляет **ссылку на заказ Bitrix** (напр. `.../mycups/edit.php?ID=1872914`).
2. `→` — запрос `POST {BaseUrl}/mycups/api/design/getData { "id": ... }` (Basic-auth).
3. Русские значения (`type/print/side/coating`) маппятся в enum'ы, артикул (`product`) →
   ключ шаблона, код дизайна → имя папки/файла.
4. Панель «Проверка данных» показывает всё для сверки (+ предупреждения о нераспознанных
   значениях).
5. Кнопка **«Создать проект»** запускает сборку.
6. Иконка **✎** открывает ручной редактор (текущий `DesignAssistant`).

## Настройка — `appsettings.json`

```jsonc
{
  "Bitrix": {
    "BaseUrl": "https://bitrix.formacia.ru",
    "GetDataPath": "/mycups/api/design/getData",
    "AuthorizationHeader": "",                     // ЛИБО целиком "Basic <hash>"
    "Login": "",                                   // ЛИБО логин+пароль (Basic соберётся сам)
    "Password": "",
    "TimeoutSeconds": 30
  },
  "ManualEditor": { "ExePath": "DesignAssistant.exe" }  // путь к ручному редактору для ✎
}
```

## ⚠️ Что нужно уточнить / проверить перед боем

1. **Авторизация** — заполнить `Login`/`Password` (или `AuthorizationHeader`). Секреты в код не зашиты.
2. **Код дизайна** — берётся из названия заказа (`name`). Если в названии есть скобки —
   используется их содержимое (`CarBar (132583 CarBar ST DW90-430)` → `132583 CarBar ST DW90-430`),
   иначе всё название целиком (`132581 OHTAAWA лето 26 DW80-280`). Отдельный эндпоинт не используется.
3. **Строки-значения Bitrix** — маппинг в `Services/BitrixMapper.cs`. Проверены для примера
   (Бумажный стакан / Офсет / Белый мелованный / Soft Touch). Для сахара/шоколада/конфет/пластика
   значения `type` — по подстроке (пластик/сахар/шокол/конфет/стакан). При новых формулировках
   правьте **только** `BitrixMapper.cs`.
4. **Шоколад/конфеты** — вкус (Milk/Dark/…, Assorted/Dubai) в API `result` не приходит.
   Для шоколада берётся `CHOKO_Milk.ai` по умолчанию, для конфет — по артикулу либо `SWEET_AS`.
   Точный выбор — через ✎ ручной редактор. (Если вкус будет добавлен в API — расширить `DesignData` и `ProjectBuilder`.)

## Соответствие полей

| Bitrix / API        | enum приложения        | Роль                    |
|---------------------|------------------------|-------------------------|
| `type` Тип продукта | `ProductType`          | ветка логики/шаблонов   |
| `product` Артикул   | ключ `TemplateResolver`| выбор `.ai`-шаблона      |
| `print` Печать      | `PrintTech`            | offset/digital/pantone  |
| `side` Материал     | `Material`             | coated/uncoated         |
| `coating` Покрытие  | `Coating`              | none/soft/color touch   |
| Код дизайна         | `DesignCode`           | имя папки и `.ai`-файла  |

## Структура

```
Models/       DesignData, Enums, AppConfig
Services/     LinkParser, BitrixClient, BitrixMapper, ProjectBuilder
Helpers/      Paths, TemplateResolver   (перенесены из DesignAssistant)
AutoWindow.*  главное окно
```
