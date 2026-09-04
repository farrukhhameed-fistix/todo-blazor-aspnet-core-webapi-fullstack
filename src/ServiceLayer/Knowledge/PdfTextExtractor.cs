#nullable enable

using System;
using System.IO;
using System.Text;
using UglyToad.PdfPig;

namespace Fistix.TaskManager.ServiceLayer.Knowledge;

/// <summary>Extracts plain text from PDF bytes for Knowledge Lab ingest.</summary>
public static class PdfTextExtractor
{
    public static string Extract(byte[] pdfBytes)
    {
        if (pdfBytes is null || pdfBytes.Length == 0)
        {
            throw new InvalidOperationException("PDF file is empty.");
        }

        using var stream = new MemoryStream(pdfBytes, writable: false);
        using var document = PdfDocument.Open(stream);
        var builder = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            var text = page.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append(text.Trim());
        }

        var result = builder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(result))
        {
            throw new InvalidOperationException(
                "PDF contained no extractable text (scanned/image-only PDFs are not supported).");
        }

        return result;
    }
}
