namespace Paddock.Core.Services;

/// <summary>
/// The "Search Google" link under the desktop search results: Google's site
/// for the user's country (google.com.tr in Turkey, google.de in Germany…),
/// asking for that country's results.
/// </summary>
public static class WebSearch
{
    /// <summary>Countries whose Google is google.com.XX.</summary>
    private static readonly HashSet<string> ComDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "TR", "AR", "AU", "BD", "BO", "BR", "CO", "CU", "DO", "EC", "EG", "GT", "HK", "KH",
        "KW", "LB", "LY", "MT", "MX", "MY", "NG", "NP", "OM", "PA", "PE", "PH", "PK", "PR",
        "PY", "QA", "SA", "SG", "SV", "TW", "UA", "UY", "VN", "BH", "CY", "AF", "BZ", "GI",
    };

    /// <summary>Countries whose Google is google.co.XX.</summary>
    private static readonly HashSet<string> CoDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "ID", "IL", "IN", "JP", "KE", "KR", "MA", "NZ", "TH", "TZ", "UG", "UZ", "VE", "ZA",
        "ZM", "ZW", "CR", "AO", "BW", "CK", "LS", "MZ",
    };

    /// <summary>Countries whose Google is simply google.XX.</summary>
    private static readonly HashSet<string> PlainDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "DE", "FR", "IT", "ES", "NL", "BE", "AT", "CH", "PL", "PT", "SE", "NO", "DK", "FI",
        "IE", "CZ", "SK", "HU", "RO", "BG", "GR", "HR", "SI", "RS", "BA", "LT", "LV", "EE",
        "LU", "IS", "RU", "BY", "KZ", "AZ", "GE", "AM", "MD", "MK", "AL", "ME", "CA", "CL",
        "AE", "IQ", "JO", "DZ", "TN", "SN", "CI", "CM", "LK", "MN", "KG", "TM", "TJ",
    };

    /// <summary>"google.com.tr" for "TR"; google.com when the country is unknown.</summary>
    public static string GoogleDomain(string? countryCode)
    {
        var code = countryCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (code.Length != 2)
            return "google.com";

        if (code == "GB")
            return "google.co.uk";

        var tld = code.ToLowerInvariant();
        if (ComDomains.Contains(code))
            return $"google.com.{tld}";
        if (CoDomains.Contains(code))
            return $"google.co.{tld}";
        if (PlainDomains.Contains(code))
            return $"google.{tld}";

        return "google.com";
    }

    /// <summary>The search page for <paramref name="query"/>, with results for that country.</summary>
    public static string GoogleUrl(string query, string? countryCode)
    {
        var url = $"https://www.{GoogleDomain(countryCode)}/search?q={Uri.EscapeDataString(query.Trim())}";

        // Google now sends most country sites on to google.com; "gl" keeps the
        // results local anyway.
        var code = countryCode?.Trim();
        return code is { Length: 2 } ? $"{url}&gl={code.ToLowerInvariant()}" : url;
    }
}
