// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.CommandLine;

using Spectre.Console;

using StonyGrey.BlueConnect.Smalt;

var input = new Argument<FileInfo>("log")
{
    Description = "A MetroLog file (or any text log containing _BLE_IO lines).",
};

var output = new Option<FileInfo?>("--output", "-o")
{
    Description = "HTML path. Defaults to the log path with a .html extension.",
};

var command = new RootCommand("Rewrite a Zaffre read/write IO log as color-coded HTML.")
{
    input,
    output,
};

command.SetAction(parse =>
{
    var log = parse.GetValue(input) ?? throw new InvalidOperationException("A log file is required.");
    var htmlPath = parse.GetValue(output)?.FullName
        ?? Path.ChangeExtension(log.FullName, ".html");

    if (!log.Exists)
    {
        AnsiConsole.MarkupLine($"[red]No such file:[/] {log.FullName}");
        return 1;
    }

    try
    {
        var lines = File.ReadAllLines(log.FullName);
        var html = HtmlWriter.Render(log.FullName, lines);
        Directory.CreateDirectory(Path.GetDirectoryName(htmlPath) ?? ".");
        File.WriteAllText(htmlPath, html);
        var ioLines = lines.Count(line => IoLine.TryParse(line) is not null);
        AnsiConsole.MarkupLine($"[green]Wrote[/] {htmlPath}");
        AnsiConsole.MarkupLine($"[grey]{lines.Length} lines, {ioLines} IO records[/]");
        return 0;
    }
    catch (Exception ex)
    {
        AnsiConsole.WriteException(ex);
        return 1;
    }
});

return command.Parse(args).Invoke();
