// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Net.Codecrete.QrCodeGenerator;

namespace StonyGrey.BlueConnect.Provision;

/// <summary>
/// Shared QR rendering for the provisioning flows: the console half-height
/// render and the printable SVG label (QR plus two caption lines).
/// </summary>
internal static class QrOutput
{
    /// <summary>Half-height render: two module rows per text line.</summary>
    public static void PrintToConsole(QrCode qr)
    {
        const int border = 2;

        for (var y = -border; y < qr.Size + border; y += 2)
        {
            for (var x = -border; x < qr.Size + border; x++)
            {
                var top = qr.GetModule(x, y);
                var bottom = qr.GetModule(x, y + 1);
                Console.Write(top ? (bottom ? '█' : '▀') : (bottom ? '▄' : ' '));
            }

            Console.WriteLine();
        }
    }

    /// <summary>
    /// The printable label: the QR plus a main caption underneath (the PAKE
    /// setup code; the EDHOC tier), a smaller sub-caption in gray, and —
    /// when given — the full payload text wrapped in small monospace lines,
    /// so the label works without a camera (paste it into the app's payload
    /// field). The text is exactly as secret as the QR: both ARE the
    /// credential.
    /// </summary>
    public static string ToLabeledSvg(QrCode qr, string caption, string subCaption, string? payload = null)
    {
        const int border = 4;
        const int payloadChunk = 44;
        const double payloadLine = 2.4;
        var size = qr.Size + 2 * border;
        var svg = qr.ToSvgString(border);

        var viewBox = $"viewBox=\"0 0 {size} {size}\"";
        if (!svg.Contains(viewBox))
        {
            // Library output format changed; ship the plain QR rather than a broken label.
            Console.Error.WriteLine("warning: unexpected SVG layout; writing the QR without a text label");
            return svg;
        }

        var main = System.Security.SecurityElement.Escape(caption);
        var sub = System.Security.SecurityElement.Escape(subCaption);

        var chunks = payload is null
            ? []
            : Enumerable.Range(0, (payload.Length + payloadChunk - 1) / payloadChunk)
                .Select(i => payload.Substring(i * payloadChunk, Math.Min(payloadChunk, payload.Length - i * payloadChunk)))
                .ToArray();
        var height = size + 8 + (chunks.Length == 0 ? 0 : chunks.Length * payloadLine + 1);

        var text = $"\t<text x=\"{size / 2.0}\" y=\"{size + 3}\" text-anchor=\"middle\" " +
            "font-family=\"Consolas, Menlo, monospace\" font-size=\"2.6\" fill=\"#000000\">" +
            $"{main}</text>\n" +
            $"\t<text x=\"{size / 2.0}\" y=\"{size + 6}\" text-anchor=\"middle\" " +
            "font-family=\"Consolas, Menlo, monospace\" font-size=\"2\" fill=\"#666666\">" +
            $"{sub}</text>\n";
        for (var i = 0; i < chunks.Length; i++)
        {
            text += $"\t<text x=\"{size / 2.0}\" y=\"{size + 9 + i * payloadLine}\" text-anchor=\"middle\" " +
                "font-family=\"Consolas, Menlo, monospace\" font-size=\"1.8\" fill=\"#333333\">" +
                $"{System.Security.SecurityElement.Escape(chunks[i])}</text>\n";
        }

        return svg
            .Replace(viewBox, $"viewBox=\"0 0 {size} {height}\"")
            .Replace("</svg>", text + "</svg>");
    }
}
