namespace SwaggerToMarkdownTool;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        string? url = null;
        string? output = null;
        bool skipSsl = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--url":
                    if (i + 1 < args.Length) url = args[++i];
                    break;
                case "--output" or "-o":
                    if (i + 1 < args.Length) output = args[++i];
                    break;
                case "--skip-ssl":
                    skipSsl = true;
                    break;
            }
        }

        if (string.IsNullOrEmpty(url))
        {
            Console.Error.WriteLine("Error: --url is required.");
            PrintUsage();
            return 1;
        }

        output ??= "swagger.md";

        try
        {
            using var client = new SwaggerClient(skipSsl);

            Console.WriteLine($"Fetching Swagger specs from: {url}");
            var specs = await client.FetchSpecsAsync(url);

            if (specs.Count == 0)
            {
                Console.Error.WriteLine("Error: No Swagger/OpenAPI specs found at the given URL.");
                return 1;
            }

            Console.WriteLine($"Found {specs.Count} API spec(s). Converting to Markdown...");

            var converter = new MarkdownConverter();
            var markdown = converter.Convert(specs);

            await File.WriteAllTextAsync(output, markdown);
            Console.WriteLine($"Done! Markdown written to: {Path.GetFullPath(output)}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine(
            """
            SwaggerToMarkdownTool - Convert Swagger/OpenAPI to Markdown

            Usage:
              SwaggerToMarkdownTool --url <swagger-url> [--output <file>] [--skip-ssl]

            Options:
              --url        Swagger UI URL or API docs JSON URL (required)
              --output/-o  Output markdown file path (default: swagger.md)
              --skip-ssl   Skip SSL certificate validation
              --help/-h    Show this help

            Examples:
              SwaggerToMarkdownTool --url https://example.com/swagger-ui/index.html --output api.md
              SwaggerToMarkdownTool --url https://example.com/v3/api-docs -o api.md --skip-ssl
            """);
    }
}
