using System.Globalization;
using System.Text;

namespace B2B.Api;

// CSV que Excel en español abre de un doble clic: BOM UTF-8, punto y coma como
// separador y coma decimal. Sin BOM Excel se come los acentos; con coma como
// separador parte las cifras por la mitad.
public static class Csv
{
    private const char Bom = '﻿';
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public static byte[] Build(IEnumerable<string> header, IEnumerable<IEnumerable<object?>> rows)
    {
        var text = new StringBuilder();
        text.Append(Bom);
        text.AppendLine(string.Join(';', header.Select(Field)));
        foreach (var row in rows)
            text.AppendLine(string.Join(';', row.Select(Field)));
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private static string Field(object? value)
    {
        var text = value switch
        {
            null => "",
            decimal number => number.ToString("0.00", Spanish),
            double number => number.ToString("0.00", Spanish),
            DateTime moment => moment.ToString("dd/MM/yyyy", Spanish),
            DateTimeOffset moment => moment.ToString("dd/MM/yyyy", Spanish),
            IFormattable formattable => formattable.ToString(null, Spanish),
            _ => value.ToString() ?? ""
        };

        return text.IndexOfAny([';', '"', '\n', '\r']) >= 0
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }
}
