using System.Text.Json;

namespace SwaggerToMarkdownTool;

public record SwaggerSpec(string Name, JsonDocument Document);

public class SwaggerClient : IDisposable
{
    private readonly HttpClient _http;

    public SwaggerClient(bool skipSsl = false)
    {
        var handler = new HttpClientHandler();
        if (skipSsl)
        {
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<List<SwaggerSpec>> FetchSpecsAsync(string url)
    {
        var uri = new Uri(url);

        // Check query parameters for direct spec URL (e.g. ?url=/v3/api-docs)
        var queryUrl = ExtractUrlFromQuery(uri);
        if (queryUrl != null)
        {
            var specUrl = queryUrl.StartsWith("http") ? queryUrl : $"{uri.Scheme}://{uri.Authority}{queryUrl}";
            var doc = await FetchJsonAsync(specUrl);
            if (doc != null && IsSwaggerSpec(doc))
                return [new SwaggerSpec("default", doc)];
        }

        // Swagger UI page → discover specs from base URL
        if (IsSwaggerUiUrl(uri))
        {
            var baseUrl = ExtractBaseUrl(uri);
            Console.WriteLine($"Detected Swagger UI page. Base URL: {baseUrl}");
            return await DiscoverSpecs(baseUrl);
        }

        // Try as direct JSON URL
        var directDoc = await FetchJsonAsync(url);
        if (directDoc != null && IsSwaggerSpec(directDoc))
            return [new SwaggerSpec("default", directDoc)];

        // Last resort: try discovery from URL as base
        var specs = await DiscoverSpecs(url.TrimEnd('/'));
        if (specs.Count > 0) return specs;

        throw new InvalidOperationException(
            "Could not find any Swagger/OpenAPI spec. " +
            "Make sure the URL points to a Swagger UI page or an API docs JSON endpoint.");
    }

    private async Task<List<SwaggerSpec>> DiscoverSpecs(string baseUrl)
    {
        // Try springdoc swagger-config (OpenAPI 3.0)
        var configSpecs = await TrySwaggerConfig(baseUrl);
        if (configSpecs.Count > 0) return configSpecs;

        // Try springfox swagger-resources (Swagger 2.0)
        var resourceSpecs = await TrySwaggerResources(baseUrl);
        if (resourceSpecs.Count > 0) return resourceSpecs;

        // Try well-known direct paths
        string[] knownPaths = ["/v3/api-docs", "/v2/api-docs", "/swagger.json", "/api-docs"];
        foreach (var path in knownPaths)
        {
            var doc = await FetchJsonAsync(baseUrl + path);
            if (doc != null && IsSwaggerSpec(doc))
            {
                Console.WriteLine($"  Found spec at: {baseUrl + path}");
                return [new SwaggerSpec("default", doc)];
            }
        }

        return [];
    }

    private async Task<List<SwaggerSpec>> TrySwaggerConfig(string baseUrl)
    {
        var configDoc = await FetchJsonAsync($"{baseUrl}/v3/api-docs/swagger-config");
        if (configDoc == null) return [];

        var root = configDoc.RootElement;

        // Single URL mode
        if (root.TryGetProperty("url", out var singleUrl) && !root.TryGetProperty("urls", out _))
        {
            var specUrl = singleUrl.GetString();
            if (specUrl != null)
            {
                var doc = await FetchWithFallback(baseUrl, specUrl);
                if (doc != null && IsSwaggerSpec(doc))
                    return [new SwaggerSpec("default", doc)];
            }
        }

        // Multiple URLs mode
        if (!root.TryGetProperty("urls", out var urls) || urls.ValueKind != JsonValueKind.Array)
            return [];

        var specs = new List<SwaggerSpec>();
        foreach (var urlEntry in urls.EnumerateArray())
        {
            var specUrl = urlEntry.TryGetProperty("url", out var u) ? u.GetString() : null;
            var name = urlEntry.TryGetProperty("name", out var n) ? n.GetString() ?? "default" : "default";
            if (specUrl == null) continue;

            var doc = await FetchWithFallback(baseUrl, specUrl);
            if (doc != null && IsSwaggerSpec(doc))
            {
                Console.WriteLine($"  Found: {name}");
                specs.Add(new SwaggerSpec(name, doc));
            }
        }

        return specs;
    }

    private async Task<List<SwaggerSpec>> TrySwaggerResources(string baseUrl)
    {
        var resourceDoc = await FetchJsonAsync($"{baseUrl}/swagger-resources");
        if (resourceDoc == null || resourceDoc.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        var specs = new List<SwaggerSpec>();
        foreach (var resource in resourceDoc.RootElement.EnumerateArray())
        {
            string? specUrl = null;
            if (resource.TryGetProperty("url", out var u))
                specUrl = u.GetString();
            else if (resource.TryGetProperty("location", out var l))
                specUrl = l.GetString();
            if (specUrl == null) continue;

            var name = resource.TryGetProperty("name", out var n) ? n.GetString() ?? "default" : "default";

            var doc = await FetchWithFallback(baseUrl, specUrl);
            if (doc != null && IsSwaggerSpec(doc))
            {
                Console.WriteLine($"  Found: {name}");
                specs.Add(new SwaggerSpec(name, doc));
            }
        }

        return specs;
    }

    /// <summary>
    /// Try resolving relative URL against the base URL first, then against the server root.
    /// </summary>
    private async Task<JsonDocument?> FetchWithFallback(string baseUrl, string relativeUrl)
    {
        if (relativeUrl.StartsWith("http"))
            return await FetchJsonAsync(relativeUrl);

        // Try 1: relative to base URL
        var url1 = baseUrl.TrimEnd('/') + (relativeUrl.StartsWith('/') ? relativeUrl : "/" + relativeUrl);
        var doc = await FetchJsonAsync(url1);
        if (doc != null) return doc;

        // Try 2: relative to server root
        var uri = new Uri(baseUrl);
        var url2 = $"{uri.Scheme}://{uri.Authority}" + (relativeUrl.StartsWith('/') ? relativeUrl : "/" + relativeUrl);
        if (url2 != url1)
            return await FetchJsonAsync(url2);

        return null;
    }

    private async Task<JsonDocument?> FetchJsonAsync(string url)
    {
        try
        {
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(content)) return null;

            return JsonDocument.Parse(content);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSwaggerSpec(JsonDocument doc)
    {
        var root = doc.RootElement;
        return root.TryGetProperty("swagger", out _) || root.TryGetProperty("openapi", out _);
    }

    private static bool IsSwaggerUiUrl(Uri uri)
    {
        var path = uri.AbsolutePath;
        return path.Contains("swagger-ui", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith("swagger-ui.html", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractBaseUrl(Uri uri)
    {
        var path = uri.AbsolutePath;
        var idx = path.IndexOf("/swagger-ui", StringComparison.OrdinalIgnoreCase);
        var basePath = idx >= 0 ? path[..idx] : path;
        return $"{uri.Scheme}://{uri.Authority}{basePath}";
    }

    private static string? ExtractUrlFromQuery(Uri uri)
    {
        var query = uri.Query;
        if (string.IsNullOrEmpty(query)) return null;

        foreach (var param in query.TrimStart('?').Split('&'))
        {
            var parts = param.Split('=', 2);
            if (parts.Length == 2 && parts[0] is "url" or "configUrl")
                return Uri.UnescapeDataString(parts[1]);
        }

        return null;
    }

    public void Dispose() => _http.Dispose();
}
