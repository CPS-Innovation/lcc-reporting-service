using Azure.Identity;
using Azure.Monitor.Query;
using Azure.Storage.Blobs;
using CPS.ComplexCases.ReportingService.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureLogging((context, logging) =>
    {
        // Clear providers to avoid conflicts with default filtering
        logging.ClearProviders();

        var connectionString = context.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        if (!string.IsNullOrEmpty(connectionString))
        {
            logging.AddApplicationInsights(
                configureTelemetryConfiguration: (config) => config.ConnectionString = connectionString,
                configureApplicationInsightsLoggerOptions: (options) => { }
            );
        }

        if (context.HostingEnvironment.IsDevelopment())
        {
            logging.AddConsole();
        }

        // Read minimum log level from configuration, fallback to Information if not set or invalid
        var logLevelString = context.Configuration["Logging:LogLevel:Default"];
        if (!Enum.TryParse<LogLevel>(logLevelString, true, out var minLevel))
        {
            minLevel = LogLevel.Information;
        }
        logging.SetMinimumLevel(minLevel);
    })
    .ConfigureServices((context, services) =>
    {
        services
            .AddApplicationInsightsTelemetryWorkerService()
            .ConfigureFunctionsApplicationInsights();

        services.AddSingleton<BlobServiceClient>(provider =>
        {
            string? blobStorageAccountUrl = context.Configuration["BlobStorageAccountUrl"];
            if (string.IsNullOrEmpty(blobStorageAccountUrl))
            {
                throw new InvalidOperationException("BlobStorageAccountUrl configuration value is missing or empty.");
            }
            return new BlobServiceClient(new Uri(blobStorageAccountUrl), new DefaultAzureCredential());
        });
        services.AddSingleton<IBlobStorageService, BlobStorageService>();


        string? workspaceId = context.Configuration["APPLICATIONINSIGHTS_WORKSPACEID"];
        if (string.IsNullOrEmpty(workspaceId))
        {
            throw new InvalidOperationException("APPLICATIONINSIGHTS_WORKSPACEID is missing in configuration or environment variable.");
        }

        services.AddSingleton<LogsQueryClient>(new LogsQueryClient(new DefaultAzureCredential()));

        // Register QueryProcessor and pass workspaceId
        services.AddSingleton<IQueryProcessor>(provider =>
            new QueryProcessor(
                provider.GetRequiredService<ILogger<QueryProcessor>>(),
                provider.GetRequiredService<LogsQueryClient>(),
                workspaceId));

        services.AddSingleton<IReportingService, ReportingService>();
        services.AddSingleton<ITelemetryService, TelemetryService>();
    })
    .Build();

await host.RunAsync();