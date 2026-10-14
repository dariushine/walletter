namespace Walletter.Application.Imports;

/// <summary>
/// Lector CSV mínimo RFC 4180: soporta campos entre comillas dobles, comas y
/// saltos de línea dentro de comillas, comillas escapadas ("") y finales CRLF/LF.
/// No pretende ser exhaustivo; cubre lo que exporta la propia app y lo que un
/// usuario pega desde una hoja de cálculo.
/// </summary>
public static class CsvReader
{
    /// <summary>Parsea el texto CSV en filas (cada fila = lista de celdas).</summary>
    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        if (string.IsNullOrEmpty(text)) return rows;

        var row = new List<string>();
        var field = new System.Text.StringBuilder();
        var inQuotes = false;
        var hasField = false;

        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    hasField = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    hasField = false;
                    break;
                case '\r':
                    // ignorar; el \n cierra la fila
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    hasField = false;
                    rows.Add(row);
                    row = new List<string>();
                    break;
                default:
                    field.Append(c);
                    hasField = true;
                    break;
            }
        }

        // Última fila sin salto final.
        if (hasField || field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows
            .Where(r => r.Any(cell => !string.IsNullOrWhiteSpace(cell)))
            .ToList();
    }

    /// <summary>
    /// Normaliza el encabezado: minúsculas, sin acentos, sin espacios ni signos.
    /// El plegado de diacríticos es explícito (no depende de string.Normalize,
    /// que es no-op cuando el runtime corre con globalization invariant).
    /// </summary>
    public static string NormalizeHeader(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s.Trim().ToLowerInvariant())
        {
            var folded = FoldDiacritic(ch);
            if (folded != '\0') sb.Append(folded);
            else if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>Plega una letra acentuada a su base ASCII; '\0' si no aplica.</summary>
    private static char FoldDiacritic(char c) => c switch
    {
        'á' or 'à' or 'ä' or 'â' or 'ã' or 'å' => 'a',
        'é' or 'è' or 'ë' or 'ê' => 'e',
        'í' or 'ì' or 'ï' or 'î' => 'i',
        'ó' or 'ò' or 'ö' or 'ô' or 'õ' => 'o',
        'ú' or 'ù' or 'ü' or 'û' => 'u',
        'ñ' => 'n',
        'ç' => 'c',
        _ => '\0',
    };
}
