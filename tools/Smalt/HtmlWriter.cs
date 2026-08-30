// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Text;

namespace StonyGrey.BlueConnect.Smalt;

internal static class HtmlWriter
{
    public static string Render(string sourcePath, IReadOnlyList<string> lines)
    {
        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("<meta charset=\"utf-8\"/>");
        html.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>");
        html.AppendLine($"<title>smalt — {WebUtility.HtmlEncode(Path.GetFileName(sourcePath))}</title>");
        html.AppendLine("<style>");
        html.AppendLine(Css);
        html.AppendLine("</style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine($"<h1>smalt · {WebUtility.HtmlEncode(Path.GetFileName(sourcePath))}</h1>");
        html.AppendLine($"<p class=\"meta\">{lines.Count} lines · {WebUtility.HtmlEncode(sourcePath)}</p>");
        html.Append(Legend("top"));
        html.AppendLine("<ol class=\"log\">");

        var plain = new List<string>();
        void FlushPlain()
        {
            if (plain.Count == 0)
            {
                return;
            }

            html.AppendLine("<li class=\"plain\">");
            html.Append("<pre class=\"raw\">")
                .Append(WebUtility.HtmlEncode(string.Join('\n', plain)))
                .AppendLine("</pre>");
            html.AppendLine("</li>");
            plain.Clear();
        }

        foreach (var line in lines)
        {
            var io = IoLine.TryParse(line);
            if (io is null)
            {
                plain.Add(line);
                continue;
            }

            FlushPlain();
            html.AppendLine("<li class=\"entry\">");
            html.Append("<pre class=\"raw\">").Append(WebUtility.HtmlEncode(line)).AppendLine("</pre>");
            var dir = io.Direction == "read" ? "RX" : "TX";
            html.Append($"<div class=\"dir {io.Direction}\">{dir}</div>");
            if (io.Bytes.Length > 0)
            {
                html.AppendLine("<div class=\"bytes\" aria-label=\"decoded bytes\">");
                var roles = ByteRoles.Classify(io);
                for (var i = 0; i < io.Bytes.Length; i++)
                {
                    var role = roles[i];
                    var color = Color(role);
                    var label = Label(role);
                    html.Append($"<span class=\"b\" style=\"background:{color}\" title=\"[{i}] {label}\">{io.Bytes[i]:X2}</span>");
                }

                html.AppendLine();
                html.AppendLine("</div>");
            }

            html.AppendLine("</li>");
        }

        FlushPlain();

        html.AppendLine("</ol>");
        html.Append(Legend("bottom"));
        html.AppendLine("</body>");
        html.AppendLine("</html>");
        return html.ToString();
    }

    private static string Legend(string id)
    {
        var html = new StringBuilder();
        html.AppendLine($"<section class=\"legend\" id=\"legend-{id}\">");
        html.AppendLine("<h2>Color legend</h2>");
        html.AppendLine("<ul>");
        foreach (var (role, label, color, description) in ByteRoles.Legend)
        {
            html.Append("<li>");
            html.Append($"<span class=\"swatch\" style=\"background:{color}\"></span>");
            html.Append($"<strong>{WebUtility.HtmlEncode(label)}</strong>");
            html.Append($" — {WebUtility.HtmlEncode(description)}");
            html.AppendLine("</li>");
        }

        html.AppendLine("</ul>");
        html.AppendLine("</section>");
        return html.ToString();
    }

    private static string Color(ByteRole role)
        => ByteRoles.Legend.First(item => item.Role == role).Color;

    private static string Label(ByteRole role)
        => ByteRoles.Legend.First(item => item.Role == role).Label;

    private const string Css = """
        :root { color-scheme: light; }
        body { font: 14px/1.45 ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; margin: 2rem; color: #0f172a; background: #f8fafc; }
        h1 { font: 600 1.4rem/1.3 ui-sans-serif, system-ui, sans-serif; margin: 0 0 .25rem; }
        h2 { font: 600 1rem/1.3 ui-sans-serif, system-ui, sans-serif; margin: 0 0 .75rem; }
        .meta { color: #64748b; margin: 0 0 1.5rem; }
        .legend { background: #fff; border: 1px solid #e2e8f0; border-radius: 8px; padding: 1rem 1.25rem; margin: 1.25rem 0 1.75rem; }
        .legend ul { list-style: none; margin: 0; padding: 0; display: grid; gap: .4rem; }
        .legend li { display: flex; gap: .6rem; align-items: center; flex-wrap: wrap; }
        .swatch { display: inline-block; width: 1.1rem; height: 1.1rem; border-radius: 3px; flex: none; }
        .b { display: inline-block; min-width: 1.7rem; padding: .1rem .25rem; border-radius: 3px; color: #fff; text-align: center; font-weight: 600; letter-spacing: .04em; }
        .log { list-style: none; margin: 0; padding: 0; }
        .entry { background: #fff; border: 1px solid #e2e8f0; border-radius: 8px; padding: .75rem 1rem; margin: 0 0 .6rem; }
        .plain { margin: 0 0 .4rem; }
        .plain .raw { margin: 0; color: #64748b; }
        .raw { margin: 0 0 .55rem; white-space: pre-wrap; overflow-wrap: anywhere; color: #334155; }
        .dir { display: inline-block; font: 600 .75rem/1 ui-sans-serif, system-ui, sans-serif; letter-spacing: .06em; padding: .15rem .4rem; border-radius: 3px; margin: 0 0 .45rem; }
        .dir.write { background: #dbeafe; color: #1e3a8a; }
        .dir.read { background: #dcfce7; color: #14532d; }
        .bytes { display: flex; flex-wrap: wrap; gap: 3px; }
        .b { cursor: default; }
        """;
}
