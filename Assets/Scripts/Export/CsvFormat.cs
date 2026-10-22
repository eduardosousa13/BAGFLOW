using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AirportBaggageSimulator
{
    /// <summary>Culture-independent CSV/JSON value formatting (RFC 4180 quoting, InvariantCulture numbers,
    /// shortest round-trip doubles). Null values are written as empty CSV fields / JSON null.</summary>
    public static class CsvFormat
    {
        public const char Separator = ',';
        public const string NewLine = "\n";
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>Round-trip representation; never depends on the system culture.</summary>
        public static string Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new FormatException("Non-finite value cannot be exported: " + value.ToString(Invariant));
            string text = value.ToString("R", Invariant);
            if (double.Parse(text, NumberStyles.Float, Invariant) != value) text = value.ToString("G17", Invariant);
            return text;
        }

        public static string Number(double? value) { return value.HasValue ? Number(value.Value) : string.Empty; }
        public static string Integer(int value) { return value.ToString(Invariant); }
        public static string Integer(int? value) { return value.HasValue ? Integer(value.Value) : string.Empty; }
        public static string Flag(bool? value) { return value.HasValue ? (value.Value ? "1" : "0") : string.Empty; }

        /// <summary>Quotes a text field when it contains a separator, quote, line break or edge spaces.</summary>
        public static string Text(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            bool quote = value.IndexOfAny(new[] { Separator, '"', '\r', '\n' }) >= 0 ||
                value[0] == ' ' || value[value.Length - 1] == ' ';
            return quote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        public static string Row(IList<string> fields)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < fields.Count; i++)
            {
                if (i > 0) builder.Append(Separator);
                builder.Append(fields[i]);
            }
            return builder.Append(NewLine).ToString();
        }

        /// <summary>JSON string literal with escaping of quotes, backslashes and control characters.</summary>
        public static string JsonString(string value)
        {
            if (value == null) return "null";
            var builder = new StringBuilder("\"");
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    default:
                        if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4", Invariant));
                        else builder.Append(c);
                        break;
                }
            }
            return builder.Append('"').ToString();
        }
    }
}
