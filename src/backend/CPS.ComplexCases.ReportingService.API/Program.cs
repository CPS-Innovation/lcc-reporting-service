using Azure.Identity;
using Azure.Monitor.Query;
using Azure.Storage.Blobs;
using CPS.ComplexCases.ReportingService.Services;
using Microsoft.ApplicationInsights.WorkerService;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.ApplicationInsights;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureLogging(options => options.AddApplicationInsights())
    .ConfigureServices((context, services) =>
    {
        services
            .AddApplicationInsightsTelemetryWorkerService(new ApplicationInsightsServiceOptions
            {
                EnableAdaptiveSampling = false,
                ConnectionString = context.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
            })
            .ConfigureFunctionsApplicationInsights();

        services.Configure<LoggerFilterOptions>(options =>
        {
            // See: https://learn.microsoft.com/en-us/azure/azure-functions/dotnet-isolated-process-guide?tabs=windows#managing-log-levels
            // The Application Insights SDK adds a default logging filter that instructs ILogger to capture only Warning and more severe logs. Application Insights requires an explicit override.
            // Log levels can also be configured using appsettings.json. For more information, see https://learn.microsoft.com/en-us/azure/azure-monitor/app/worker-service#ilogger-logs
            var toRemove = options.Rules
                .FirstOrDefault(rule =>
                    string.Equals(rule.ProviderName, typeof(ApplicationInsightsLoggerProvider).FullName));

            if (toRemove is not null)
            {
                options.Rules.Remove(toRemove);
            }
        });

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

        services.AddSingleton<IQueryProcessor>(provider =>
        {
            var timeRangeEnv = context.Configuration["TimeRangeInDays"];
            if (string.IsNullOrEmpty(timeRangeEnv) || !double.TryParse(timeRangeEnv, out var days) || days <= 0)
            {
                throw new InvalidOperationException("TimeRangeInDays is not configured or invalid.");
            }
            return new QueryProcessor(
                provider.GetRequiredService<ILogger<QueryProcessor>>(),
                provider.GetRequiredService<LogsQueryClient>(),
                workspaceId, days);
        });

        services.AddSingleton<IReportingService>(provider =>
        {
            var containerName = context.Configuration["BlobContainerNameReporting"];
            if (string.IsNullOrEmpty(containerName))
            {
                throw new InvalidOperationException("BlobContainerNameReporting is missing in configuration.");
            }

            return new ReportingService(
                provider.GetRequiredService<ILogger<ReportingService>>(),
                provider.GetRequiredService<ITelemetryService>(),
                provider.GetRequiredService<IBlobStorageService>(),
                containerName);
        });

        services.AddSingleton<ITelemetryService, TelemetryService>();
    })
    .Build();

await host.RunAsync();