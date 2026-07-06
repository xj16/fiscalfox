using System.Globalization;
using FiscalFox.Domain.Entities;

namespace FiscalFox.Api.Services;

/// <summary>
/// Parses cached open-data CSVs (Date,Open,High,Low,Close,Volume) from disk.
/// No network access is ever made — the app ships with a local price cache.
/// </summary>
public static class PriceCsvLoader
{
    /// <summary>Parse a single CSV file into price bars (Instrument FK unset).</summary>
    public static IReadOnlyList<PriceBar> ParseFile(string path)
    {
        var bars = new List<PriceBar>();
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var cols = line.Split(',');
            if (cols.Length < 6)
                continue;

            if (!DateOnly.TryParse(cols[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                continue;

            bars.Add(new PriceBar
            {
                Date = d,
                Open = ParseDecimal(cols[1]),
                High = ParseDecimal(cols[2]),
                Low = ParseDecimal(cols[3]),
                Close = ParseDecimal(cols[4]),
                Volume = long.TryParse(cols[5], NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0
            });
        }

        return bars.OrderBy(b => b.Date).ToList();
    }

    /// <summary>Enumerate all &lt;symbol&gt;.csv files in a directory.</summary>
    public static IEnumerable<(string Symbol, string Path)> Discover(string directory)
    {
        if (!Directory.Exists(directory))
            yield break;

        foreach (var file in Directory.EnumerateFiles(directory, "*.csv"))
        {
            var symbol = Path.GetFileNameWithoutExtension(file);
            yield return (symbol, file);
        }
    }

    private static decimal ParseDecimal(string s) =>
        decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
}
