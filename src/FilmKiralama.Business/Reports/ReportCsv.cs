using System.Text;

namespace FilmKiralama.Business.Reports;

/// <summary>A file to save: a suggested name and its content.</summary>
public sealed record ExportFile(string SuggestedName, byte[] Content);

/// <summary>
/// CSV for spreadsheets: UTF-8 with a byte order mark (so Excel reads Turkish letters), ";" between the
/// fields (the Turkish list separator), every field quoted, and a leading "'" before any field a
/// spreadsheet would run as a formula (one starting with =, +, -, @, a tab or a carriage return).
/// </summary>
public static class ReportCsv
{
    public static byte[] Write(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        var text = new StringBuilder();
        AppendLine(text, headers);
        foreach (var row in rows)
        {
            AppendLine(text, row);
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return [.. encoding.GetPreamble(), .. encoding.GetBytes(text.ToString())];
    }

    /// <summary>A field as a spreadsheet must take it: text, never a formula, quoted.</summary>
    public static string Field(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            text = "'" + text;
        }

        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static void AppendLine(StringBuilder text, IEnumerable<string> fields) =>
        text.AppendJoin(';', fields.Select(Field)).Append("\r\n");
}
