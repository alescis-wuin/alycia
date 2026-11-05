using Scalar.AspNetCore;
using Microsoft.OpenApi;
using System.Diagnostics;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, _, logger) =>
{
    var configuration = context.Configuration;

    var logPath = configuration["ALYCIA_LOG_PATH"]
        ?? "logs/alycia-.jsonl";

    var fileSizeLimit = configuration.GetValue<long?>(
        "ALYCIA_LOG_FILE_SIZE_BYTES") ?? 10_485_760L;

    var retainedFiles = configuration.GetValue<int?>(
        "ALYCIA_LOG_RETAINED_FILES") ?? 14;

    logger
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console(
            outputTemplate:
                "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            new CompactJsonFormatter(),
            logPath,
            rollingInterval: RollingInterval.Day,
            fileSizeLimitBytes: fileSizeLimit,
            rollOnFileSizeLimit: true,
            retainedFileCountLimit: retainedFiles);
});

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        var healthPath = new OpenApiPathItem();

        healthPath.AddOperation(HttpMethod.Get, new OpenApiOperation
        {
            Summary = "Check API health",
            Description = "Technical health check without a PostgreSQL dependency.",
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Description = "Healthy",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["text/plain"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = JsonSchemaType.String
                            }
                        }
                    }
                },
                ["503"] = new OpenApiResponse
                {
                    Description = "Unhealthy",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["text/plain"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = JsonSchemaType.String
                            }
                        }
                    }
                }
            }
        });

        document.Paths ??= new OpenApiPaths();
        document.Paths["/healthz"] = healthPath;

        return Task.CompletedTask;
    });
});

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set(
            "TraceId",
            Activity.Current?.TraceId.ToString()
                ?? httpContext.TraceIdentifier);
    };
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.MapScalarApiReference(options =>
        options.DisableAgent().DisableDefaultFonts());
}

app.MapHealthChecks("/healthz");

app.Run();
