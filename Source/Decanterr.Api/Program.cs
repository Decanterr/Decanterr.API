using Decanterr.Api.Middleware;
using Decanterr.Api.Services;
using Decanterr.Api.Hubs;
using AppScaffolding;
using Microsoft.OpenApi;
using AudibleUtilities;
using LibationFileManager;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// --- Bootstrap Libation backend ---
ServerBootstrapper.Initialize(builder.Configuration);

// --- Services ---
builder.Services.AddControllers();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info = new() { Title = "Decanterr API", Version = "v1" };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            ["ApiKey"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-Api-Key",
                Description = "API key for authentication"
            }
        };
        if (document.Paths is not null)
        {
            foreach (var operation in document.Paths.Values.SelectMany(path => path.Operations ?? []))
            {
                operation.Value.Security ??= [];
                operation.Value.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("ApiKey", document)] = []
                });
            }
        }
        return Task.CompletedTask;
    });
});

builder.Services.AddSignalR();
builder.Services.AddSingleton<LiberationQueueService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LiberationQueueService>());
builder.Services.AddSingleton<AudiobookshelfSettingsStore>();
builder.Services.AddHttpClient<AudiobookshelfClient>();
builder.Services.AddSingleton<AudiobookshelfUploadService>();

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? ["http://localhost:3000", "http://localhost:5173"];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(origins)
              .AllowAnyMethod()
              .AllowAnyHeader();
    });

    // Named policy for SignalR (requires credentials)
    options.AddPolicy("SignalR", policy =>
    {
        policy.WithOrigins(origins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

AudiobookshelfLibraryCache.Configure(app.Services);

// --- Middleware ---
app.MapOpenApi();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/openapi/v1.json", "Decanterr API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors();
app.UseMiddleware<ApiKeyAuthMiddleware>();
app.MapControllers();
app.MapHub<ProgressHub>("/hubs/progress").RequireCors("SignalR");
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
   .AllowAnonymous();

app.Run();

