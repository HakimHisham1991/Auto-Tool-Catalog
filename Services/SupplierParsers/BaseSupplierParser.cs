using System.Net;
using System.Text.RegularExpressions;
using AutoToolCatalog.Models;
using HtmlAgilityPack;

namespace AutoToolCatalog.Services.SupplierParsers;

public abstract class BaseSupplierParser : ISupplierParser
{
    private static readonly Regex MeasurementPattern = new(@"\d+[,.]?\d*\s*(?:mm|in)", RegexOptions.IgnoreCase);
    private static readonly Regex MetricMeasurementPattern = new(@"\d+[,.]?\d*\s*mm", RegexOptions.IgnoreCase);
    private static readonly Regex PlainNumberPattern = new(@"^\s*\d{1,2}\s*$");
    protected readonly HttpClient HttpClient;
    private const int MaxRetries = 3;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    protected BaseSupplierParser(IHttpClientFactory httpClientFactory)
    {
        HttpClient = httpClientFactory.CreateClient(SupplierName);
    }

    public abstract string SupplierName { get; }
    protected abstract string SearchBaseUrl { get; }

    public async Task<ToolSpecResult> FetchSpecsAsync(ToolRecord record, CancellationToken ct = default)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(Timeout);
                return await FetchSpecsCoreAsync(record, cts.Token);
            }
            catch (OperationCanceledException)
            {
                if (attempt == MaxRetries)
                    return ToolSpecResult.Failed("Timeout");
            }
            catch (HttpRequestException ex)
            {
                if (attempt == MaxRetries)
                    return ToolSpecResult.Failed($"Website error: {ex.Message}");
            }
            catch (Exception ex)
            {
                if (attempt == MaxRetries)
                    return ToolSpecResult.Failed(ex.Message);
            }

            await Task.Delay(500 * attempt, ct);
        }

        return ToolSpecResult.Failed("Max retries exceeded");
    }

    protected abstract Task<ToolSpecResult> FetchSpecsCoreAsync(ToolRecord record, CancellationToken ct);

    protected async Task<HtmlDocument?> FetchHtmlAsync(string url, CancellationToken ct)
    {
        var response = await HttpClient.GetAsync(url, ct);
        if (response.StatusCode != HttpStatusCode.OK)
            return null;
        var html = await response.Content.ReadAsStringAsync(ct);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc;
    }

    protected static string? ExtractSpec(HtmlDocument doc, string[] specCodes, bool metricOnly = false)
    {
        // 0. Kennametal-style: td.spec-label + td.spec-value, prefer metric over inch rows
        var specRows = doc.DocumentNode.SelectNodes("//tr[.//td[contains(@class,'spec-label')]]");
        if (specRows != null)
        {
            foreach (HtmlNode row in specRows)
            {
                if (row.GetAttributeValue("class", "").Contains("inch", StringComparison.OrdinalIgnoreCase))
                    continue;
                var labelCell = row.SelectSingleNode(".//td[contains(@class,'spec-label')]");
                var valueCell = row.SelectSingleNode(".//td[contains(@class,'spec-value')]");
                if (labelCell == null || valueCell == null) continue;
                var header = labelCell.InnerText.Trim().ToUpperInvariant();
                var value = valueCell.InnerText.Trim();
                foreach (var code in specCodes)
                {
                    if (header.Contains(code.ToUpperInvariant()))
                        return NormalizeValue(value, metricOnly);
                }
            }
        }

        // 1. Table rows: <tr><td>DC</td><td>10</td></tr> or 3-col: <tr><td>APMXS</td><td>Depth of cut max...</td><td>18.00 mm</td></tr>
        var rows = doc.DocumentNode.SelectNodes("//table//tr");
        if (rows != null)
        {
            foreach (var row in rows)
            {
                var cells = row.SelectNodes(".//td|.//th");
                if (cells == null || cells.Count < 2) continue;
                string? value = null;
                foreach (var code in specCodes)
                {
                    var codeUpper = code.ToUpperInvariant();
                    for (var i = 0; i < cells.Count; i++)
                    {
                        var cellText = cells[i].InnerText.Trim();
                        if (!cellText.ToUpperInvariant().Contains(codeUpper)) continue;
                        // Found code in this cell; value is in a sibling cell with a measurement
                        for (var j = 0; j < cells.Count; j++)
                        {
                            if (j == i) continue;
                            var siblingText = cells[j].InnerText.Trim();
                            var matchesMeasure = metricOnly ? MetricMeasurementPattern.IsMatch(siblingText) : MeasurementPattern.IsMatch(siblingText);
                            if (matchesMeasure || PlainNumberPattern.IsMatch(siblingText))
                            {
                                value = siblingText;
                                break;
                            }
                        }
                        if (value != null) break;
                    }
                    if (value != null) return NormalizeValue(value, metricOnly);
                }
            }
        }

        // 1b. Simpler table: header in first cell, value in second (or last cell with measurement)
        rows = doc.DocumentNode.SelectNodes("//table//tr");
        if (rows != null)
        {
            foreach (var row in rows)
            {
                var cells = row.SelectNodes(".//td|.//th");
                if (cells == null || cells.Count < 2) continue;
                var header = cells[0].InnerText.Trim().ToUpperInvariant();
                var value = cells[1].InnerText.Trim();
                if (cells.Count >= 3)
                {
                    for (var i = 1; i < cells.Count; i++)
                    {
                        var cellText = cells[i].InnerText.Trim();
                        var matchesMeasure = metricOnly ? MetricMeasurementPattern.IsMatch(cellText) : MeasurementPattern.IsMatch(cellText);
                        if (matchesMeasure || PlainNumberPattern.IsMatch(cellText)) { value = cellText; break; }
                    }
                }
                foreach (var code in specCodes)
                {
                    if (header.Contains(code.ToUpperInvariant()))
                        return NormalizeValue(value, metricOnly);
                }
            }
        }

        // 2. Definition lists: <dl><dt>DC</dt><dd>10</dd></dl>
        var dts = doc.DocumentNode.SelectNodes("//dt");
        if (dts != null)
        {
            foreach (var dt in dts)
            {
                var header = dt.InnerText.Trim().ToUpperInvariant();
                var dd = dt.NextSibling;
                while (dd != null && dd.Name != "dd") dd = dd.NextSibling;
                var value = dd?.InnerText.Trim() ?? "";
                foreach (var code in specCodes)
                {
                    if (header.Contains(code.ToUpperInvariant()))
                        return NormalizeValue(value, metricOnly);
                }
            }
        }

        // 3. Div/spans: label + value pairs (e.g. "DC" or "Diameter" in one element, "10" in next)
        var labels = doc.DocumentNode.SelectNodes("//*[contains(@class,'label') or contains(@class,'name') or contains(@class,'spec') or contains(@class,'property') or contains(@class,'attribute') or contains(@class,'detail') or contains(@class,'title')]");
        if (labels != null)
        {
            foreach (var label in labels)
            {
                var header = label.InnerText.Trim().ToUpperInvariant();
                var value = label.NextSibling?.InnerText.Trim()
                    ?? label.ParentNode?.SelectSingleNode(".//*[contains(@class,'value') or contains(@class,'val')]")?.InnerText.Trim()
                    ?? "";
                foreach (var code in specCodes)
                {
                    if (header.Contains(code.ToUpperInvariant()) && !string.IsNullOrWhiteSpace(value))
                        return NormalizeValue(value, metricOnly);
                }
            }
        }

        // 4. Regex fallback: find "DC 10" or "Diameter: 10 mm" in raw text (code immediately followed by number)
        var bodyText = doc.DocumentNode.InnerText;
        foreach (var code in specCodes)
        {
            var pattern = metricOnly ? $@"(?:{Regex.Escape(code)})[\s:]+([0-9]+[,.]?[0-9]*)\s*mm" : $@"(?:{Regex.Escape(code)})[\s:]+([0-9]+[,.]?[0-9]*)\s*(?:mm)?";
            var m = Regex.Match(bodyText, pattern, RegexOptions.IgnoreCase);
            if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
                return NormalizeValue(m.Groups[1].Value.Trim() + " mm", metricOnly);
        }

        // 5. Label then next measurement: "APMXS ... 18.00 mm" or "OAL ... 57.00 mm" - find code then next number+mm within 200 chars
        foreach (var code in specCodes)
        {
            var escaped = Regex.Escape(code);
            var pattern = metricOnly ? $@"(?:{escaped})[\s\S]{{0,300}}?([0-9]+[,.]?[0-9]*)\s*mm" : $@"(?:{escaped})[\s\S]{{0,300}}?([0-9]+[,.]?[0-9]*)\s*(?:mm|in)";
            var m = Regex.Match(bodyText, pattern, RegexOptions.IgnoreCase);
            if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
            {
                var val = m.Groups[1].Value.Trim();
                var normalized = val.Contains('.') || val.Contains(',') ? val + " mm" : val;
                return NormalizeValue(normalized, metricOnly);
            }
        }

        // 5b. For integer-only specs (flute count): "Z 4" or "Number of flutes 4" - no mm unit
        var integerSpecKeywords = new[] { "Z", "flute", "teeth", "cutting edges", "Number of" };
        if (specCodes.Any(c => integerSpecKeywords.Any(k => c.Contains(k, StringComparison.OrdinalIgnoreCase))))
        {
            foreach (var code in specCodes)
            {
                var escaped = Regex.Escape(code);
                var pattern = $@"(?:{escaped})[\s\S]{{0,150}}?(\d{{1,2}})(?:\s|$|mm|in)";
                var m = Regex.Match(bodyText, pattern, RegexOptions.IgnoreCase);
                if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n >= 1 && n <= 24)
                    return m.Groups[1].Value.Trim();
            }
        }

        // 6. Relaxed regex for Column 4 (shank/connection) when label has extra text (e.g. "Shank diameter 6.00 mm")
        var shankKeywords = new[] { "DMM", "shank", "bore", "DCONMS", "Connection diameter machine side", "Adapter / Shank / Bore Diameter", "Shank diameter", "Connection diameter", "Shank diameter (h6)", "Adapter", "Bore Diameter" };
        var hasShankCode = specCodes.Any(c => shankKeywords.Any(k => c.Contains(k, StringComparison.OrdinalIgnoreCase) || string.Equals(c, k, StringComparison.OrdinalIgnoreCase)));
        if (hasShankCode)
        {
            var relaxedPattern = metricOnly ? @"(?:DMM|shank|bore|DCONMS|Connection diameter machine side|Adapter\s*/\s*Shank\s*/\s*Bore\s*Diameter|Shank diameter|Connection diameter|Shank diameter \(h6\)|Adapter|Bore Diameter)\s+(?:\w+\s+)*([0-9]+[,.]?[0-9]*)\s*mm" : @"(?:DMM|shank|bore|DCONMS|Connection diameter machine side|Adapter\s*/\s*Shank\s*/\s*Bore\s*Diameter|Shank diameter|Connection diameter|Shank diameter \(h6\)|Adapter|Bore Diameter)\s+(?:\w+\s+)*([0-9]+[,.]?[0-9]*)\s*(?:mm)?";
            var m = Regex.Match(bodyText, relaxedPattern, RegexOptions.IgnoreCase);
            if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
                return NormalizeValue(m.Groups[1].Value.Trim() + " mm", metricOnly);
        }

        return null;
    }

    protected static string NormalizeValue(string value, bool metricOnly = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return "#NA";
        value = value.Trim();
        if (value.Contains("in") && !value.Contains("mm"))
        {
            if (metricOnly) return "#NA"; // Inch data not accepted when metric only
            if (double.TryParse(value.Replace("in", "").Replace("\"", "").Trim(), out var inches))
                return $"{inches * 25.4:F2} mm";
        }
        return value;
    }
}
