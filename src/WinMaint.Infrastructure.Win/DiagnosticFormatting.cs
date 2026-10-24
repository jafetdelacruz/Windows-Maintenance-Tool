using System.Globalization;

namespace WinMaint.Infrastructure.Win;

internal static class DiagnosticFormatting
{
    internal static string Bytes(ulong value)
    {
        string[] suffixes = ["B", "KiB", "MiB", "GiB", "TiB"];
        var converted = (double)value;
        var suffix = 0;
        while (converted >= 1024 && suffix < suffixes.Length - 1)
        {
            converted /= 1024;
            suffix++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{converted:0.##} {suffixes[suffix]}");
    }

    internal static string Percentage(double value) =>
        string.Create(CultureInfo.InvariantCulture, $"{value:0.0} %");

    internal static string Duration(TimeSpan value) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalDays} d {value.Hours:00} h {value.Minutes:00} min");
}
