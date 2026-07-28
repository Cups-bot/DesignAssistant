using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace CupsCore
{
    public sealed class BuildResult
    {
        public bool Success { get; set; }
        public bool AlreadyExisted { get; set; }
        public string ProjectPath { get; set; } = "";
        public string? AiFile { get; set; }
        public List<string> Log { get; } = new();
    }

    /// <summary>
    /// Создаёт структуру проекта, копирует .ai-шаблон, пишет args.txt и запускает Illustrator.
    ///
    /// Единственная копия этой процедуры. Раньше она существовала дважды — в ручном
    /// редакторе и в автоматической программе — и копии успели разойтись.
    /// Все правила (папка, имя, шаблон, содержимое args.txt) берутся из каталога.
    /// </summary>
    public static class ProjectBuilder
    {
        public static BuildResult Build(BuildRequest request)
        {
            var res = new BuildResult();
            void Log(string m) => res.Log.Add(m);

            string code = request.DesignCode.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                Log("Имя дизайна пустое — создать проект нельзя.");
                return res;
            }

            string[] parts = code.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                Log("Некорректное имя дизайна.");
                return res;
            }

            DesignSpec spec = request.Spec;
            string folderName = code;
            string mat = request.Material == Material.Coated ? "coated" : "uncoated";
            string tech = spec.PrintTech.ToString().ToLowerInvariant();

            CatalogProduct product;
            string article;
            string mainPath;
            try
            {
                product = CatalogService.Current.Require(spec);

                // null — артикул нужно достать из имени по правилу продукта.
                article = (request.Article ?? product.ExtractArticle(code)).Trim();

                if (product.IsPantoneNaming)
                {
                    if (parts.Length < 3)
                    {
                        Log("Формат имени для Pantone: <номер> <название> <артикул>.");
                        return res;
                    }
                    string number = parts[0];
                    string nomenclature = string.IsNullOrWhiteSpace(article) ? parts[^1] : article;
                    string title = string.Join(' ', parts[1..^1]);
                    article = nomenclature;
                    folderName = $"{nomenclature}-{number}_{title}";
                }

                if (!string.IsNullOrWhiteSpace(product.ArgsTech))
                    tech = product.ArgsTech;

                mainPath = Path.Combine(product.ResolveOutput(), folderName);
            }
            catch (ConfigurationException ex)
            {
                Log(ex.Message);
                return res;
            }

            string innerPath = Path.Combine(mainPath, "In");
            res.ProjectPath = mainPath;

            // --- Уже существует? ---
            if (Directory.Exists(mainPath))
            {
                Log($"Папка \"{folderName}\" уже существует.");
                res.AlreadyExisted = true;
                res.Success = true;
                TryStart("explorer.exe", mainPath, Log);

                string existingAi = Path.Combine(mainPath, $"{folderName}.ai");
                if (File.Exists(existingAi))
                {
                    res.AiFile = existingAi;
                    if (Paths.IllustratorFound)
                        TryStart(Paths.IllustratorExe, $"/launch \"{existingAi}\"", Log);
                    else
                        Log(Paths.IllustratorNotFoundMessage);
                }
                return res;
            }

            // --- Шаблон ---
            string templatePath;
            string? templateFile;
            try
            {
                (templatePath, templateFile) = TemplateResolver.ResolveTemplate(product, spec, ref article);
            }
            catch (ConfigurationException ex)
            {
                Log(ex.Message);
                return res;
            }

            if (string.IsNullOrEmpty(templateFile))
            {
                Log($"Не удалось определить шаблон по артикулу: \"{article}\".");
                return res;
            }

            string sourceFile = Path.Combine(templatePath, templateFile);
            string targetFile = Path.Combine(mainPath, $"{folderName}.ai");

            if (!File.Exists(sourceFile))
            {
                Log($"Файл шаблона не найден: {sourceFile}");
                return res;
            }

            try
            {
                Directory.CreateDirectory(mainPath);
                Directory.CreateDirectory(innerPath);
                File.Copy(sourceFile, targetFile);
            }
            catch (Exception ex)
            {
                Log($"Ошибка при создании проекта: {ex.Message}");
                return res;
            }

            // --- args.txt ---
            // Применимо ли покрытие — свойство продукта из каталога, а не проверка «это стаканы».
            string? coating = product.Coating && request.Material == Material.Coated
                ? request.Coating switch
                {
                    CupsCore.Coating.SoftTouch => "soft_touch",
                    CupsCore.Coating.ColorTouch => "color_touch",
                    _ => "none"
                }
                : null;

            var lines = new List<string> { code, article, tech, mat };
            if (coating != null) lines.Add($"coating:{coating}");
            if (product.ArgsCountry) lines.Add(spec.Country.ToString());

            try
            {
                File.WriteAllLines(Path.Combine(innerPath, "args.txt"), lines);
            }
            catch (Exception ex)
            {
                Log($"Ошибка записи args.txt: {ex.Message}");
                return res;
            }

            res.AiFile = targetFile;
            res.Success = true;
            Log($"Создан проект: {folderName}");
            Log($"Продукт: {product.Title} · Артикул: {article} · Печать: {tech} · Материал: {mat}");
            if (coating != null) Log($"Покрытие: {coating}");
            if (product.ArgsCountry) Log($"Страна: {spec.Country}");

            // --- Запуск Illustrator ---
            // Проект уже создан, поэтому отсутствие Illustrator — не провал сборки:
            // папка и args.txt на месте, файл можно открыть руками.
            if (!Paths.IllustratorFound)
            {
                Log(Paths.IllustratorNotFoundMessage);
                return res;
            }

            try
            {
                string exe = Paths.IllustratorExe;
                var proc = Process.Start(exe, $"/launch \"{targetFile}\"");
                proc?.WaitForInputIdle(5000);
                Process.Start(exe, $"/run \"{Paths.JsxScriptPath}\"");
                Log("Illustrator запущен, скрипт выполнен.");
            }
            catch (ConfigurationException ex)
            {
                Log(ex.Message);
            }
            catch (Exception ex)
            {
                Log($"Не удалось запустить Illustrator: {ex.Message}");
            }

            return res;
        }

        private static void TryStart(string file, string args, Action<string> log)
        {
            try { Process.Start(file, args); }
            catch (Exception ex) { log($"Не удалось запустить {Path.GetFileName(file)}: {ex.Message}"); }
        }
    }
}
