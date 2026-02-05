using System.Text.RegularExpressions;
using AutoToolCatalog.Models;

namespace AutoToolCatalog.Services;

/// <summary>
/// Fallback: extract specs from tool part numbers when website scraping returns no data.
/// Supplier part number formats often encode diameter, length, corner radius, flute count.
/// </summary>
public static class PartNumberSpecExtractor
{
    public static ToolSpecResult Extract(ToolRecord record)
    {
        var result = new ToolSpecResult { Success = true };
        var desc = record.ToolDescription;
        var supplier = record.Supplier.ToUpperInvariant();

        if (record.IsEndmill)
        {
            switch (supplier)
            {
                case "SECO":
                    ExtractSecoEndmill(desc, result);
                    break;
                case "KENNAMETAL":
                    ExtractKennametalEndmill(desc, result);
                    break;
                case "SANDVIK":
                    ExtractSandvikEndmill(desc, result);
                    break;
                case "WALTER":
                    ExtractWalterEndmill(desc, result);
                    break;
                default:
                    ExtractGeneric(desc, result);
                    break;
            }
        }
        else
        {
            switch (supplier)
            {
                case "SECO":
                    ExtractSecoDrill(desc, result);
                    break;
                case "KENNAMETAL":
                    ExtractKennametalDrill(desc, result);
                    break;
                case "SANDVIK":
                    ExtractSandvikDrill(desc, result);
                    break;
                case "WALTER":
                    ExtractWalterDrill(desc, result);
                    break;
                default:
                    ExtractGeneric(desc, result);
                    break;
            }
        }

        return result;
    }

    // SECO Endmill: JS534060D1B.0Z4-NXT -> 060 = 6.0mm diameter, Z4 = 4 flutes, D1B = 1mm corner, shank often = tool dia
    private static void ExtractSecoEndmill(string desc, ToolSpecResult r)
    {
        var m = Regex.Match(desc, @"JS\d{3}(\d{2,3})", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n))
            r.Spec1 = n >= 10 ? $"{n / 10.0:F1} mm" : $"{n} mm";
        m = Regex.Match(desc, @"[Zz]-?(\d)", RegexOptions.IgnoreCase);
        if (m.Success)
            r.Spec4 = m.Groups[1].Value;
        // D1B = 1 mm corner radius (digit before B); D2B = 2 mm, etc.
        m = Regex.Match(desc, @"[Dd](\d)[Bb](?:\.\d)?", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var corner) && corner > 0)
            r.Spec3 = $"{corner} mm";
        if (string.IsNullOrEmpty(r.Spec3) || r.Spec3 == "#NA")
        {
            m = Regex.Match(desc, @"[Dd]\d[Bb]\.(\d)");
            if (m.Success && m.Groups[1].Value != "0")
                r.Spec3 = m.Groups[1].Value + " mm";
        }
    }

    // Kennametal Endmill: H1TE4RA0400N006HBR025M -> 0400=4mm dia, N006=6mm shank, HBR025=0.25 corner
    private static void ExtractKennametalEndmill(string desc, ToolSpecResult r)
    {
        var m = Regex.Match(desc, @"H1TE4RA(\d{4})", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n))
            r.Spec1 = n >= 100 ? $"{n / 100.0:F1} mm" : $"{n} mm";
        m = Regex.Match(desc, @"(?:HAR|HBR)(\d{2,3})", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var corner))
            r.Spec3 = $"{corner / 100.0:F2} mm";
        m = Regex.Match(desc, @"N0?(\d{1,3})\D", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var shank) && shank >= 3 && shank <= 32)
            r.Spec6 = $"{shank} mm";
        m = Regex.Match(desc, @"[LN]0?(\d{2,3})", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var len) && len >= 20 && len <= 200)
            r.Spec5 = $"{len} mm";
    }

    // Sandvik Endmill: 1K344-1300-XD 1730 -> 1300=13mm diameter; shank often = tool dia
    private static void ExtractSandvikEndmill(string desc, ToolSpecResult r)
    {
        var m = Regex.Match(desc, @"1K\d{3}-(\d{3,4})", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n))
            r.Spec1 = n >= 100 ? $"{n / 100.0:F1} mm" : $"{n} mm";
        m = Regex.Match(desc, @"-(\d{2,3})-", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var len) && len >= 20)
            r.Spec5 = $"{len} mm";
    }

    // Walter Endmill: H3094718-6-100 -> 6=6mm dia, 100=100mm length; shank often = tool dia
    private static void ExtractWalterEndmill(string desc, ToolSpecResult r)
    {
        var m = Regex.Match(desc, @"-(\d{1,2})-(\d{2,3})(?:-|$|\.)", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            if (int.TryParse(m.Groups[1].Value, out var d) && d >= 1 && d <= 50)
                r.Spec1 = $"{d} mm";
            if (int.TryParse(m.Groups[2].Value, out var len) && len >= 20 && len <= 300)
                r.Spec5 = $"{len} mm";
        }
        m = Regex.Match(desc, @"[Zz]-?(\d)");
        if (m.Success)
            r.Spec4 = m.Groups[1].Value;
    }

    // SECO Drill: SD1103-1000-035-10R1 -> 1000=10mm, -10R1=10mm shank
    private static void ExtractSecoDrill(string desc, ToolSpecResult r)
    {
        var m = Regex.Match(desc, @"SD\d+-(\d{3,4})-(\d{2,3})", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            if (int.TryParse(m.Groups[1].Value, out var n))
                r.Spec1 = n >= 100 ? $"{n / 100.0:F1} mm" : $"{n} mm";
            if (int.TryParse(m.Groups[2].Value, out var len) && len >= 10)
                r.Spec5 = $"{len} mm";
        }
        m = Regex.Match(desc, @"-(\d{1,2})R\d", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var shank) && shank >= 3 && shank <= 32)
            r.Spec6 = $"{shank} mm";
    }

    // Kennametal Drill: B051F06000CPG -> 06=6mm
    private static void ExtractKennametalDrill(string desc, ToolSpecResult r)
    {
        var m = Regex.Match(desc, @"\d{2}F(\d{2})\d{3}", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var d))
            r.Spec1 = $"{d} mm";
    }

    // Sandvik Drill: 462.1-0803-024A1 -> 0803=8.03mm, 024=24mm length; shank often = tool dia
    private static void ExtractSandvikDrill(string desc, ToolSpecResult r)
    {
        var m = Regex.Match(desc, @"(\d{4})-(\d{2,3})", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            if (int.TryParse(m.Groups[1].Value, out var n))
                r.Spec1 = $"{n / 100.0:F2} mm";
            if (int.TryParse(m.Groups[2].Value, out var len) && len >= 10 && len <= 200)
                r.Spec5 = $"{len} mm";
        }
    }

    // Walter Drill: DC180-03-04.000A1 -> 03=3mm, 04=flute length; shank often = tool dia
    private static void ExtractWalterDrill(string desc, ToolSpecResult r)
    {
        var m = Regex.Match(desc, @"DC\d+-(\d{2})-(\d{2})", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            if (int.TryParse(m.Groups[1].Value, out var d))
                r.Spec1 = $"{d} mm";
            if (int.TryParse(m.Groups[2].Value, out var len))
                r.Spec2 = r.Spec5 = $"{len} mm";
        }
    }

    private static void ExtractGeneric(string desc, ToolSpecResult r)
    {
        var numbers = Regex.Matches(desc, @"\b(\d{1,2})\.?(\d{0,2})\b")
            .Select(m => double.TryParse(m.Value.Replace(",", "."), out var v) ? v : (double?)null)
            .Where(v => v.HasValue && v > 0)
            .Select(v => v!.Value)
            .ToList();
        if (numbers.Count >= 1 && numbers[0] >= 1 && numbers[0] <= 50)
            r.Spec1 = $"{numbers[0]:F1} mm";
        if (numbers.Count >= 2 && numbers[1] >= 20 && numbers[1] <= 300)
            r.Spec5 = $"{numbers[1]:F0} mm";
        if (numbers.Count >= 3 && numbers[2] >= 0.1 && numbers[2] <= 5)
            r.Spec3 = $"{numbers[2]:F1} mm";
    }
}
