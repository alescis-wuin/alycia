using Alycia.Api.OwnerInitialization;
using Alycia.Modules.Identity.Application;
using Alycia.Modules.Identity.Infrastructure;
using Npgsql;
using Scalar.AspNetCore;
using Microsoft.OpenApi;
using System.Diagnostics;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

// Administrative CLI runs only with local process/OS access, never via HTTP.
if (args is ["bootstrap", "generate"])
{
    var connectionString = BootstrapConnectionString(
        Environment.GetEnvironmentVariable("ALYCIA_WORKSPACES_CONNECTION"));
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine("A local bootstrap database connection is not configured.");
        return;
    }

    try
    {
        var secret = await new OwnerBootstrapIssuer(connectionString, TimeProvider.System)
            .GenerateAsync();
        Console.Out.WriteLine(secret); // Display once; do not write to log files.
    }
    catch (ActiveBootstrapSecretException)
    {
        Console.Error.WriteLine("BOOTSTRAP_ACTIVE: a valid secret already exists.");
        Environment.ExitCode = 1;
    }
    catch (BootstrapAlreadyInitializedException)
    {
        Console.Error.WriteLine("OWNER_ALREADY_INITIALIZED: generation permanently disabled.");
        Environment.ExitCode = 1;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"BOOTSTRAP_GENERATION_FAILED: {exception.GetType().Name}");
        Environment.ExitCode = 1;
    }
    return;
}

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

var identityConnection = BootstrapConnectionString(
    builder.Configuration["ALYCIA_WORKSPACES_CONNECTION"]);
if (!string.IsNullOrWhiteSpace(identityConnection))
{
    builder.Services.AddSingleton(new OwnerInitializationPersistence(identityConnection));
    builder.Services.AddSingleton(LocalBlockedOwnerPasswords.CreatePolicy());
    builder.Services.AddSingleton<IOwnerPasswordHasher, OwnerPasswordHasher>();
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton(sp => new BootstrapOwnerService(identityConnection,
        sp.GetRequiredService<OwnerInitializationPersistence>(),
        sp.GetRequiredService<OwnerPasswordPolicy>(),
        sp.GetRequiredService<IOwnerPasswordHasher>(),
        sp.GetRequiredService<TimeProvider>()));
}


// S7: the S6 anonymous business-route exception is retired.
// Only the guarded, local owner bootstrap endpoint will be added in issue #42.

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

if (!string.IsNullOrWhiteSpace(identityConnection))
{
    // Early gate: reject Docker peers and remote HTTP before body parsing or DI.
    // Host/X-Forwarded-For cannot override the actual TCP peer.
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api/identity/bootstrap")
            && !BootstrapEndpoints.IsLocalConnection(context))
        {
            await BootstrapEndpoints.Denied(context).ExecuteAsync(context);
            return;
        }
        await next(context);
    });

    app.MapBootstrapEndpoints();
}

app.MapHealthChecks("/healthz");

app.Run();

// Docker receives the DB password as a distinct environment variable so
// arbitrary characters (notably semicolons) remain safe in connection strings.
static string? BootstrapConnectionString(string? configured)
{
    if (!string.IsNullOrWhiteSpace(configured)) return configured;
    var password = Environment.GetEnvironmentVariable("ALYCIA_POSTGRES_PASSWORD");
    if (string.IsNullOrEmpty(password)) return null;
    return new NpgsqlConnectionStringBuilder
    {
        Host = "postgres",
        Database = "alycia",
        Username = "alycia",
        Password = password
    }.ConnectionString;
}
