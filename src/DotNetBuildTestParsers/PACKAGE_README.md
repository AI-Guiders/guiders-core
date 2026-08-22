# AIGuiders.DotNetBuildTestParsers

Небольшая библиотека **без внешних зависимостей**: она берёт **сырой текст** из stdout/stderr процессов `dotnet build` и `dotnet test` и возвращает структуры C#, с которыми удобно строить отчёты, панели в IDE, ответы MCP и т.д.

**Целевой фреймворк:** .NET 10 (`net10.0`). **Лицензия:** MIT.

## Зачем

- **Сборка:** MSBuild печатает диагностики в виде `путь(строка,столбец): error|warning КОД: сообщение` — парсер вытаскивает списки ошибок и предупреждений и при необходимости код выхода из строки вида `Exit code: N`.
- **Тесты:** при типичном консольном выводе `dotnet test` парсер собирает счётчики passed/failed/skipped и для упавших тестов — имя, длительность (если есть) и текст сообщения, если он следует в логе сразу после `Failed …`.

Такой слой общий для инструментов вроде **dotnet-build-test-mcp** и **Cascade IDE**: один раз разобрали вывод — дальше не дублировать regex по проектам.

## Установка

```bash
dotnet add package AIGuiders.DotNetBuildTestParsers
```

Пакет на NuGet: [AIGuiders.DotNetBuildTestParsers](https://www.nuget.org/packages/AIGuiders.DotNetBuildTestParsers). Исходники: [github.com/KarataevDmitry/dotnet-build-test-parsers](https://github.com/KarataevDmitry/dotnet-build-test-parsers).

## `dotnet build`

```csharp
using DotNetBuildTestParsers;

var log = await File.ReadAllTextAsync("build.log"); // или stdout процесса
var r = BuildOutputParser.Parse(log);

if (!r.Success)
{
    foreach (var e in r.Errors)
        Console.WriteLine($"{e.File}:{e.Line} {e.Code} {e.Message}");
}
// r.Warnings — предупреждения; r.ExitCode — если в тексте была строка Exit code:
```

**Типы:** `BuildParseResult`, `BuildDiagnostic` (файл, строка, столбец, код, сообщение).

## `dotnet test`

Ориентир по формату лога — **console** logger, уровень детализации **normal** или **detailed**, когда в выводе есть строки вида `Passed …`, `Failed …` и при падении блоки с `Error Message:` / `Message:`.

```csharp
using DotNetBuildTestParsers;

var log = await File.ReadAllTextAsync("test.log");
var r = TestOutputParser.Parse(log);

Console.WriteLine($"Total={r.Total}, passed={r.Passed}, failed={r.Failed}, skipped={r.Skipped}");
foreach (var t in r.FailedTests)
    Console.WriteLine($"{t.Name}: {t.Message}");
```

**Типы:** `TestParseResult`, `TestResultItem`.

## Ограничения

Парсеры заточены под **распространённые шаблоны** вывода MSBuild и консольного логгера тестов. Экзотические локали, другие логгеры или сильно урезанный verbosity могут дать неполную картину — в сомнительных случаях проверяй сырой лог рядом с результатом `Parse`.
