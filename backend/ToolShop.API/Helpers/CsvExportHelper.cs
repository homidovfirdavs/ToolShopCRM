using System.Text;

namespace ToolShop.API.Helpers;

public static class CsvExportHelper
{
    public static byte[] BuildCsv(IEnumerable<string> headers, IEnumerable<IEnumerable<string?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(h => SanitizeField(h))));

        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",", row.Select(SanitizeField)));
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
        return result;
    }

    public static string SanitizeField(string? value)
    {
        value ??= "";

        // Prevent CSV formula injection: if the field starts with a character
        // that spreadsheet software (Excel, Google Sheets) treats as a formula
        // trigger, prefix it with a single quote so it is read as plain text.
        if (value.Length > 0 && (value[0] == '=' || value[0] == '+' || value[0] == '-' || value[0] == '@' || value[0] == '\t' || value[0] == '\r'))
        {
            value = "'" + value;
        }

        var needsQuoting = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (needsQuoting)
        {
            value = "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}