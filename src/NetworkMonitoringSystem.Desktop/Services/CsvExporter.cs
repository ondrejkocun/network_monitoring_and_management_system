using System.IO;
using System.Text;
using Microsoft.Win32;

namespace NetworkMonitoringSystem.Desktop.Services;

/// <summary>Saves a table as a CSV file the user chooses. View models use this interface, so they stay testable.</summary>
public interface ICsvExporter
{
    /// <summary>Asks where to save the table and saves it.</summary>
    /// <returns>The path of the saved file, or null when the user cancelled.</returns>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file could not be written.</exception>
    string? Export(string suggestedName, IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows);
}

public sealed class SaveFileDialogCsvExporter : ICsvExporter
{
    public string? Export(string suggestedName, IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Exportovať do CSV",
            FileName = suggestedName,
            DefaultExt = ".csv",
            Filter = "CSV (oddelené bodkočiarkou)|*.csv",
            AddExtension = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        // With the byte order mark Excel reads the diacritics correctly.
        File.WriteAllText(dialog.FileName, Csv.Format(header, rows), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        return dialog.FileName;
    }
}

/// <summary>Writes tables in the CSV form a Slovak Excel opens directly: fields separated by semicolons.</summary>
public static class Csv
{
    public const char Separator = ';';

    public static string Format(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rows);

        var text = new StringBuilder();
        AppendRow(text, header);

        foreach (var row in rows)
        {
            AppendRow(text, row);
        }

        return text.ToString();
    }

    private static void AppendRow(StringBuilder text, IReadOnlyList<string> fields)
    {
        text.AppendJoin(Separator, fields.Select(Quote)).Append("\r\n");
    }

    /// <summary>A field with a separator, a quote or a line break is quoted, and its quotes doubled.</summary>
    private static string Quote(string? field)
    {
        field ??= string.Empty;

        // A leading = + - @ would make Excel evaluate the field as a formula.
        if (field.Length > 0 && "=+-@".Contains(field[0], StringComparison.Ordinal))
        {
            field = "'" + field;
        }

        return field.IndexOfAny([Separator, '"', '\r', '\n']) >= 0
            ? "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : field;
    }
}
