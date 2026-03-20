using System.Text;
using System.Text.Json;

namespace SwaggerToMarkdownTool;

public class MarkdownConverter
{
    private static readonly string[] HttpMethods = ["get", "post", "put", "delete", "patch", "options", "head"];

    private JsonElement _definitions;
    private bool _isV3;
    private readonly HashSet<string> _referencedModels = [];

    public string Convert(List<SwaggerSpec> specs)
    {
        var sb = new StringBuilder();

        if (specs.Count == 1)
        {
            ConvertSingleSpec(sb, specs[0]);
        }
        else
        {
            sb.AppendLine("# API Documentation");
            sb.AppendLine();

            sb.AppendLine("## Services");
            sb.AppendLine();
            foreach (var spec in specs)
            {
                var title = GetInfoField(spec.Document, "title") ?? spec.Name;
                sb.AppendLine($"- [{title}](#{Slugify(title)})");
            }
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            foreach (var spec in specs)
            {
                ConvertSpecSection(sb, spec, headingOffset: 1);
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private void ConvertSingleSpec(StringBuilder sb, SwaggerSpec spec)
    {
        InitDefinitions(spec.Document);

        var root = spec.Document.RootElement;
        var title = GetInfoField(spec.Document, "title") ?? "API Documentation";

        sb.AppendLine($"# {title}");
        sb.AppendLine();
        AppendApiInfo(sb, spec.Document);

        if (root.TryGetProperty("paths", out var paths))
            AppendPaths(sb, root, paths, hBase: 2);

        AppendModelsSection(sb, hBase: 2);
    }

    private void ConvertSpecSection(StringBuilder sb, SwaggerSpec spec, int headingOffset)
    {
        InitDefinitions(spec.Document);

        var root = spec.Document.RootElement;
        var title = GetInfoField(spec.Document, "title") ?? spec.Name;
        var h = Heading(1 + headingOffset);

        sb.AppendLine($"{h} {title}");
        sb.AppendLine();
        AppendApiInfo(sb, spec.Document);

        if (root.TryGetProperty("paths", out var paths))
            AppendPaths(sb, root, paths, hBase: 2 + headingOffset);

        AppendModelsSection(sb, hBase: 2 + headingOffset);
    }

    private void InitDefinitions(JsonDocument doc)
    {
        var root = doc.RootElement;
        _isV3 = root.TryGetProperty("openapi", out _);
        _referencedModels.Clear();

        _definitions = default;
        if (_isV3)
        {
            if (root.TryGetProperty("components", out var c) && c.TryGetProperty("schemas", out var s))
                _definitions = s;
        }
        else
        {
            if (root.TryGetProperty("definitions", out var d))
                _definitions = d;
        }
    }

    // ──────────────────────── API Info ────────────────────────

    private static void AppendApiInfo(StringBuilder sb, JsonDocument doc)
    {
        var root = doc.RootElement;
        var desc = GetInfoField(doc, "description");
        if (!string.IsNullOrEmpty(desc))
        {
            sb.AppendLine($"> {desc}");
            sb.AppendLine();
        }

        var version = GetInfoField(doc, "version");
        if (!string.IsNullOrEmpty(version))
            sb.AppendLine($"**Version:** `{version}`  ");

        var baseUrl = GetBaseUrl(root);
        if (!string.IsNullOrEmpty(baseUrl))
            sb.AppendLine($"**Base URL:** `{baseUrl}`  ");

        // Security schemes summary
        AppendSecurityInfo(sb, root);

        sb.AppendLine();
    }

    private static void AppendSecurityInfo(StringBuilder sb, JsonElement root)
    {
        JsonElement schemes;
        bool found = false;

        if (root.TryGetProperty("components", out var comp)
            && comp.TryGetProperty("securitySchemes", out schemes))
            found = true;
        else if (root.TryGetProperty("securityDefinitions", out schemes))
            found = true;

        if (!found || schemes.ValueKind != JsonValueKind.Object) return;

        sb.AppendLine();
        sb.AppendLine("**Authentication:**");
        sb.AppendLine();
        foreach (var scheme in schemes.EnumerateObject())
        {
            var type = scheme.Value.TryGetProperty("type", out var t) ? t.GetString() : "unknown";
            var sDesc = scheme.Value.TryGetProperty("description", out var d) ? d.GetString() : null;
            var name = scheme.Value.TryGetProperty("name", out var n) ? n.GetString() : null;
            var inLoc = scheme.Value.TryGetProperty("in", out var il) ? il.GetString() : null;

            var detail = type switch
            {
                "apiKey" => $"API Key (`{name}` in {inLoc})",
                "oauth2" => "OAuth 2.0",
                "http" => scheme.Value.TryGetProperty("scheme", out var s) ? $"HTTP {s.GetString()}" : "HTTP",
                _ => type
            };

            sb.Append($"- **{scheme.Name}**: {detail}");
            if (!string.IsNullOrEmpty(sDesc))
                sb.Append($" — {sDesc}");
            sb.AppendLine();
        }
    }

    // ──────────────────────── Paths & Endpoints ────────────────────────

    private void AppendPaths(StringBuilder sb, JsonElement root, JsonElement paths, int hBase)
    {
        var grouped = GroupEndpointsByTag(paths);

        // Table of Contents
        sb.AppendLine($"{Heading(hBase)} Table of Contents");
        sb.AppendLine();
        foreach (var group in grouped)
        {
            sb.AppendLine($"- [{group.Key}](#{Slugify(group.Key)})");
        }
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        // Endpoint groups
        foreach (var group in grouped)
        {
            sb.AppendLine($"{Heading(hBase)} {group.Key}");
            sb.AppendLine();

            AppendTagDescription(sb, root, group.Key);

            foreach (var (method, path, operation) in group.Value)
            {
                AppendEndpoint(sb, method, path, operation, hBase + 1);
            }
        }
    }

    private static void AppendTagDescription(StringBuilder sb, JsonElement root, string tagName)
    {
        if (!root.TryGetProperty("tags", out var tags)) return;

        foreach (var tag in tags.EnumerateArray())
        {
            if (tag.TryGetProperty("name", out var tn) && tn.GetString() == tagName
                && tag.TryGetProperty("description", out var td))
            {
                sb.AppendLine($"> {td.GetString()}");
                sb.AppendLine();
                break;
            }
        }
    }

    private void AppendEndpoint(StringBuilder sb, string method, string path, JsonElement op, int hLevel)
    {
        var h = Heading(hLevel);
        var summary = op.TryGetProperty("summary", out var s) ? s.GetString() : null;

        sb.AppendLine($"{h} `{method.ToUpperInvariant()}` {path}");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(summary))
        {
            sb.AppendLine($"**{summary}**");
            sb.AppendLine();
        }

        if (op.TryGetProperty("description", out var desc))
        {
            var descText = desc.GetString();
            if (!string.IsNullOrEmpty(descText) && descText != summary)
            {
                sb.AppendLine(descText);
                sb.AppendLine();
            }
        }

        if (op.TryGetProperty("deprecated", out var dep) && dep.ValueKind == JsonValueKind.True)
        {
            sb.AppendLine("> **Deprecated**");
            sb.AppendLine();
        }

        AppendParameters(sb, op);
        AppendRequestBody(sb, op);
        AppendResponses(sb, op);

        sb.AppendLine("---");
        sb.AppendLine();
    }

    // ──────────────────────── Parameters ────────────────────────

    private void AppendParameters(StringBuilder sb, JsonElement op)
    {
        if (!op.TryGetProperty("parameters", out var paramsArr)) return;

        var parameters = new List<JsonElement>();
        foreach (var p in paramsArr.EnumerateArray())
        {
            if (!_isV3 && p.TryGetProperty("in", out var inVal) && inVal.GetString() == "body")
                continue;
            parameters.Add(p);
        }

        if (parameters.Count == 0) return;

        sb.AppendLine("**Parameters:**");
        sb.AppendLine();
        sb.AppendLine("| Name | Location | Type | Required | Description |");
        sb.AppendLine("|------|----------|------|----------|-------------|");

        foreach (var p in parameters)
        {
            var name = p.TryGetProperty("name", out var n) ? n.GetString() : "";
            var inLoc = p.TryGetProperty("in", out var il) ? il.GetString() : "";
            var type = ResolveParameterType(p);
            var required = p.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True ? "Yes" : "No";
            var pDesc = p.TryGetProperty("description", out var pd) ? Escape(pd.GetString() ?? "") : "";

            if (p.TryGetProperty("enum", out var enumVals))
                pDesc += FormatEnum(enumVals);
            else if (p.TryGetProperty("schema", out var schema) && schema.TryGetProperty("enum", out var schemaEnum))
                pDesc += FormatEnum(schemaEnum);

            sb.AppendLine($"| {name} | {inLoc} | `{type}` | {required} | {pDesc} |");
        }
        sb.AppendLine();
    }

    // ──────────────────────── Request Body ────────────────────────

    private void AppendRequestBody(StringBuilder sb, JsonElement op)
    {
        if (_isV3)
        {
            if (!op.TryGetProperty("requestBody", out var body)) return;

            sb.AppendLine("**Request Body:**");
            sb.AppendLine();

            if (body.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.True)
                sb.AppendLine("*Required*  ");

            if (body.TryGetProperty("description", out var desc) && !string.IsNullOrEmpty(desc.GetString()))
            {
                sb.AppendLine(desc.GetString());
                sb.AppendLine();
            }

            if (body.TryGetProperty("content", out var content))
            {
                foreach (var ct in content.EnumerateObject())
                {
                    sb.AppendLine($"Content-Type: `{ct.Name}`");
                    sb.AppendLine();
                    if (ct.Value.TryGetProperty("schema", out var schema))
                        AppendSchemaDetail(sb, schema, 0);
                }
            }
        }
        else
        {
            // Swagger 2.0: body parameter
            if (!op.TryGetProperty("parameters", out var pArr)) return;

            foreach (var p in pArr.EnumerateArray())
            {
                if (!p.TryGetProperty("in", out var inVal) || inVal.GetString() != "body") continue;

                sb.AppendLine("**Request Body:**");
                sb.AppendLine();

                if (p.TryGetProperty("description", out var desc) && !string.IsNullOrEmpty(desc.GetString()))
                {
                    sb.AppendLine(desc.GetString());
                    sb.AppendLine();
                }

                if (p.TryGetProperty("schema", out var schema))
                    AppendSchemaDetail(sb, schema, 0);

                break;
            }
        }
    }

    // ──────────────────────── Responses ────────────────────────

    private void AppendResponses(StringBuilder sb, JsonElement op)
    {
        if (!op.TryGetProperty("responses", out var responses)) return;

        sb.AppendLine("**Responses:**");
        sb.AppendLine();
        sb.AppendLine("| Code | Description |");
        sb.AppendLine("|------|-------------|");

        foreach (var resp in responses.EnumerateObject())
        {
            var rDesc = resp.Value.TryGetProperty("description", out var rd) ? Escape(rd.GetString() ?? "") : "";
            sb.AppendLine($"| {resp.Name} | {rDesc} |");
        }
        sb.AppendLine();

        foreach (var resp in responses.EnumerateObject())
        {
            var schema = GetResponseSchema(resp.Value);
            if (schema == null) continue;

            sb.AppendLine($"**{resp.Name} Response Schema:**");
            sb.AppendLine();
            AppendSchemaDetail(sb, schema.Value, 0);
        }
    }

    private JsonElement? GetResponseSchema(JsonElement response)
    {
        if (_isV3)
        {
            if (!response.TryGetProperty("content", out var content)) return null;
            foreach (var ct in content.EnumerateObject())
            {
                if (ct.Value.TryGetProperty("schema", out var sch))
                    return sch;
            }
            return null;
        }

        return response.TryGetProperty("schema", out var s) ? s : null;
    }

    // ──────────────────────── Schema Rendering ────────────────────────

    private void AppendSchemaDetail(StringBuilder sb, JsonElement schema, int depth)
    {
        if (depth > 6) return;

        var resolved = ResolveRef(schema);
        if (resolved == null) return;
        schema = resolved.Value;

        // Array type
        if (HasType(schema, "array") && schema.TryGetProperty("items", out var items))
        {
            sb.AppendLine($"Type: `{ResolveTypeName(items)}[]`");
            sb.AppendLine();
            AppendSchemaDetail(sb, items, depth + 1);
            return;
        }

        // Object with properties
        if (schema.TryGetProperty("properties", out _))
        {
            AppendPropertyTable(sb, schema);
            return;
        }

        // allOf composition
        if (schema.TryGetProperty("allOf", out var allOf))
        {
            foreach (var sub in allOf.EnumerateArray())
                AppendSchemaDetail(sb, sub, depth + 1);
            return;
        }

        // additionalProperties (Map/Dictionary)
        if (schema.TryGetProperty("additionalProperties", out var addProps))
        {
            sb.AppendLine($"Type: `Map<string, {ResolveTypeName(addProps)}>`");
            sb.AppendLine();
            return;
        }

        // Simple type
        var typeName = ResolveTypeName(schema);
        if (!string.IsNullOrEmpty(typeName) && typeName != "object")
        {
            sb.AppendLine($"Type: `{typeName}`");
            sb.AppendLine();
        }
    }

    private void AppendPropertyTable(StringBuilder sb, JsonElement schema)
    {
        if (!schema.TryGetProperty("properties", out var props)) return;

        var requiredSet = new HashSet<string>();
        if (schema.TryGetProperty("required", out var reqArr) && reqArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in reqArr.EnumerateArray())
            {
                if (r.GetString() is { } rn) requiredSet.Add(rn);
            }
        }

        sb.AppendLine("| Field | Type | Required | Description |");
        sb.AppendLine("|-------|------|----------|-------------|");

        foreach (var prop in props.EnumerateObject())
        {
            var typeName = ResolveTypeName(prop.Value);
            var required = requiredSet.Contains(prop.Name) ? "Yes" : "No";
            var pDesc = prop.Value.TryGetProperty("description", out var d) ? Escape(d.GetString() ?? "") : "";

            if (prop.Value.TryGetProperty("enum", out var enumVals))
                pDesc += FormatEnum(enumVals);

            if (prop.Value.TryGetProperty("example", out var ex))
                pDesc += $" Example: `{ex}`";

            sb.AppendLine($"| {prop.Name} | `{typeName}` | {required} | {pDesc} |");
        }
        sb.AppendLine();
    }

    // ──────────────────────── Models Section ────────────────────────

    private void AppendModelsSection(StringBuilder sb, int hBase)
    {
        if (_definitions.ValueKind != JsonValueKind.Object || _referencedModels.Count == 0) return;

        sb.AppendLine($"{Heading(hBase)} Models");
        sb.AppendLine();

        var rendered = new HashSet<string>();
        // Iteratively render models because rendering one model may reference others
        while (true)
        {
            var pending = _referencedModels.Except(rendered).OrderBy(m => m).ToList();
            if (pending.Count == 0) break;

            foreach (var modelName in pending)
            {
                rendered.Add(modelName);
                if (!_definitions.TryGetProperty(modelName, out var schema)) continue;

                sb.AppendLine($"{Heading(hBase + 1)} {modelName}");
                sb.AppendLine();

                if (schema.TryGetProperty("description", out var desc) && !string.IsNullOrEmpty(desc.GetString()))
                {
                    sb.AppendLine($"> {desc.GetString()}");
                    sb.AppendLine();
                }

                // allOf: merge and render
                if (schema.TryGetProperty("allOf", out var allOf))
                {
                    foreach (var sub in allOf.EnumerateArray())
                    {
                        var resolved = ResolveRef(sub);
                        if (resolved?.TryGetProperty("properties", out _) == true)
                            AppendPropertyTable(sb, resolved.Value);
                    }
                }
                else
                {
                    AppendPropertyTable(sb, schema);
                }

                // Enum values for enum-only models
                if (schema.TryGetProperty("enum", out var enumVals))
                {
                    sb.AppendLine($"Enum values: {FormatEnum(enumVals)}");
                    sb.AppendLine();
                }
            }
        }
    }

    // ──────────────────────── Type Resolution ────────────────────────

    private string ResolveTypeName(JsonElement schema)
    {
        if (schema.TryGetProperty("$ref", out var refVal))
        {
            var refName = ExtractRefName(refVal.GetString()!);
            _referencedModels.Add(refName);
            return refName;
        }

        var type = schema.TryGetProperty("type", out var t) ? t.GetString() : null;
        var format = schema.TryGetProperty("format", out var f) ? f.GetString() : null;

        if (type == "array" && schema.TryGetProperty("items", out var items))
            return $"{ResolveTypeName(items)}[]";

        if (schema.TryGetProperty("allOf", out var allOf))
        {
            var names = new List<string>();
            foreach (var sub in allOf.EnumerateArray())
                names.Add(ResolveTypeName(sub));
            return string.Join(" & ", names);
        }

        if (schema.TryGetProperty("oneOf", out var oneOf))
        {
            var names = new List<string>();
            foreach (var sub in oneOf.EnumerateArray())
                names.Add(ResolveTypeName(sub));
            return string.Join(" | ", names);
        }

        if (schema.TryGetProperty("additionalProperties", out var addProps))
            return $"Map<string, {ResolveTypeName(addProps)}>";

        if (format != null)
            return $"{type}({format})";

        return type ?? "object";
    }

    private string ResolveParameterType(JsonElement param)
    {
        if (param.TryGetProperty("schema", out var schema))
            return ResolveTypeName(schema);

        var type = param.TryGetProperty("type", out var t) ? t.GetString() : null;
        var format = param.TryGetProperty("format", out var f) ? f.GetString() : null;

        if (type == "array" && param.TryGetProperty("items", out var items))
        {
            var itemType = items.TryGetProperty("type", out var it) ? it.GetString() : "object";
            return $"{itemType}[]";
        }

        if (format != null) return $"{type}({format})";
        return type ?? "string";
    }

    private JsonElement? ResolveRef(JsonElement element)
    {
        if (!element.TryGetProperty("$ref", out var refVal)) return element;

        var refName = ExtractRefName(refVal.GetString()!);
        _referencedModels.Add(refName);

        if (_definitions.ValueKind == JsonValueKind.Object && _definitions.TryGetProperty(refName, out var resolved))
            return resolved;

        return null;
    }

    // ──────────────────────── Grouping ────────────────────────

    private static Dictionary<string, List<(string Method, string Path, JsonElement Operation)>> GroupEndpointsByTag(
        JsonElement paths)
    {
        var grouped = new Dictionary<string, List<(string, string, JsonElement)>>();

        foreach (var path in paths.EnumerateObject())
        {
            foreach (var method in HttpMethods)
            {
                if (!path.Value.TryGetProperty(method, out var operation)) continue;

                var tags = new List<string>();
                if (operation.TryGetProperty("tags", out var tagsArr))
                {
                    foreach (var tag in tagsArr.EnumerateArray())
                    {
                        if (tag.GetString() is { } tagName) tags.Add(tagName);
                    }
                }

                if (tags.Count == 0) tags.Add("default");

                foreach (var tag in tags)
                {
                    if (!grouped.TryGetValue(tag, out var list))
                    {
                        list = [];
                        grouped[tag] = list;
                    }
                    list.Add((method, path.Name, operation));
                }
            }
        }

        return grouped;
    }

    // ──────────────────────── Helpers ────────────────────────

    private static string? GetInfoField(JsonDocument doc, string field)
    {
        if (doc.RootElement.TryGetProperty("info", out var info) && info.TryGetProperty(field, out var val))
            return val.GetString();
        return null;
    }

    private static string GetBaseUrl(JsonElement root)
    {
        if (root.TryGetProperty("servers", out var servers) && servers.GetArrayLength() > 0)
            return servers[0].TryGetProperty("url", out var url) ? url.GetString() ?? "" : "";

        var host = root.TryGetProperty("host", out var h) ? h.GetString() : null;
        var basePath = root.TryGetProperty("basePath", out var bp) ? bp.GetString() : "";
        var scheme = "https";
        if (root.TryGetProperty("schemes", out var schemes) && schemes.GetArrayLength() > 0)
            scheme = schemes[0].GetString() ?? "https";

        return host != null ? $"{scheme}://{host}{basePath}" : basePath ?? "";
    }

    private static bool HasType(JsonElement schema, string type)
    {
        return schema.TryGetProperty("type", out var t) && t.GetString() == type;
    }

    private static string ExtractRefName(string refPath)
    {
        var lastSlash = refPath.LastIndexOf('/');
        return lastSlash >= 0 ? refPath[(lastSlash + 1)..] : refPath;
    }

    private static string Heading(int level) => new('#', Math.Min(level, 6));

    private static string Slugify(string text)
    {
        return text.ToLowerInvariant()
            .Replace(' ', '-')
            .Replace("/", "")
            .Replace("(", "")
            .Replace(")", "");
    }

    private static string Escape(string text)
    {
        return text.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
    }

    private static string FormatEnum(JsonElement enumVals)
    {
        var vals = new List<string>();
        foreach (var v in enumVals.EnumerateArray())
            vals.Add(v.ToString());
        return $" Enum: [`{string.Join("`, `", vals)}`]";
    }
}
