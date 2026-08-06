namespace Decanterr.Api.Middleware;

public class ApiKeyAuthMiddleware
{
    private const string ApiKeyHeaderName = "X-Api-Key";
    private readonly RequestDelegate _next;
    private readonly HashSet<string> _apiKeys;

    public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _apiKeys = configuration.GetSection("ApiKeys").Get<string[]>()?.ToHashSet()
            ?? throw new InvalidOperationException("ApiKeys must be configured in appsettings.json");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();

        // Allow anonymous endpoints (health check, SignalR negotiate)
        if (endpoint?.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>() is not null)
        {
            await _next(context);
            return;
        }

        // Allow swagger UI and OpenAPI spec
        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Allow SignalR hub connections (they auth via query string)
        if (path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase))
        {
            // Check query string for SignalR connections
            if (context.Request.Query.TryGetValue("api_key", out var queryKey) &&
                _apiKeys.Contains(queryKey.ToString()))
            {
                await _next(context);
                return;
            }

            // Also check header for SignalR
            if (context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var hubHeaderKey) &&
                _apiKeys.Contains(hubHeaderKey.ToString()))
            {
                await _next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing API key" });
            return;
        }

        // Check header first
        if (context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey) &&
            _apiKeys.Contains(extractedApiKey.ToString()))
        {
            await _next(context);
            return;
        }

        // Fall back to query string (needed for <img src> tags that can't set headers)
        if (context.Request.Query.TryGetValue("api_key", out var qsKey) &&
            _apiKeys.Contains(qsKey.ToString()))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing API key" });
    }
}

