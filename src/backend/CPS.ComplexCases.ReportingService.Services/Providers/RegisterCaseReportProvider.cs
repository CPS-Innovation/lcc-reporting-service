using System.Globalization;
using System.Text;
using Azure;
using Azure.Monitor.Query;
using CPS.ComplexCases.ReportingService.Domain.Configuration;
using CPS.ComplexCases.ReportingService.Domain.Models;

namespace CPS.ComplexCases.ReportingService.Services.Providers;

public class RegisterCaseReportProvider(ILogger<RegisterCaseReportProvider> logger) : IReportProvider
{
    public const string ReportKeyName = "RegisterCase";

    /// <summary>
    /// Binding expression for this report's TimerTrigger. The Functions host resolves it against
    /// the same configuration value that <see cref="ReportConfig.CronSchedule"/> is bound from.
    /// </summary>
    public const string CronScheduleSetting = $"%Reports:{ReportKeyName}:CronSchedule%";

    public const string CsvHeader = "JourneyId,Username,AreaOrDivision,StartedAt,CompletedAt,DropOffScreen,Urn,Outcome";

    private const string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    private const string Query = """
        let events =
            AppEvents
            | where tostring(Properties.appName) =~ "Cps.RegisterCase"
            | where Name in ("UiTelemetry", "CaseRegistered")
            | extend
                eventName = iff(Name == "CaseRegistered", "CaseRegistered", tostring(Properties["name"])),
                journeyId = tostring(Properties.journeyId),
                username = tostring(Properties.username),
                areaOrDivisionText = coalesce(tostring(Properties.areaOrDivisionText), tostring(Properties.areaOrDivision), ""),
                urn = tostring(Properties.urn),
                pageName = ""
            | where isnotempty(journeyId)
            | where eventName in ("JourneyStarted", "JourneyCancelled", "CaseRegistered")
            | project timestamp = TimeGenerated, eventName, journeyId, username, areaOrDivisionText, urn, pageName;
        let pages =
            AppPageViews
            | where tostring(Properties.appName) =~ "Cps.RegisterCase"
            | extend
                eventName = "PageView",
                journeyId = tostring(Properties.journeyId),
                username = tostring(Properties.username),
                areaOrDivisionText = "",
                urn = "",
                pageName = Name
            | where isnotempty(journeyId) and isnotempty(pageName)
            | where pageName != "Cancel Case Registration Confirmation"
            | project timestamp = TimeGenerated, eventName, journeyId, username, areaOrDivisionText, urn, pageName;
        union events, pages
        | summarize
            usernames = make_set_if(username, isnotempty(username)),
            startedAt = min(iff(eventName == "JourneyStarted", timestamp, datetime(null))),
            completedAt = min(iff(eventName == "CaseRegistered", timestamp, datetime(null))),
            cancelledAt = min(iff(eventName == "JourneyCancelled", timestamp, datetime(null))),
            areaOrDivision = max(iff(eventName == "CaseRegistered", areaOrDivisionText, "")),
            urn = max(iff(eventName == "CaseRegistered", urn, "")),
            lastPageKey = max(iff(eventName == "PageView", strcat(format_datetime(timestamp, "yyyy-MM-dd HH:mm:ss.fff"), "||", pageName), ""))
            by journeyId
        | where isnotnull(startedAt)
        | extend username = tostring(usernames[0])
        | extend
            completedAt = iff(isnotnull(completedAt) and completedAt >= startedAt, completedAt, datetime(null)),
            lastPageName = iff(lastPageKey contains "||", tostring(split(lastPageKey, "||")[1]), "")
        | extend
            dropOffScreen = iff(isnotnull(completedAt), "", lastPageName),
            areaOrDivision = iff(isnotnull(completedAt), areaOrDivision, ""),
            urn = iff(isnotnull(completedAt), urn, ""),
            outcome = case(
                isnotnull(completedAt), "Completed",
                isnotnull(cancelledAt), "Cancelled",
                "Abandoned"
            )
        | project
            JourneyId = journeyId,
            Username = username,
            AreaOrDivision = areaOrDivision,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            DropOffScreen = dropOffScreen,
            Urn = urn,
            Outcome = outcome
        | order by StartedAt desc
        """;

    private readonly ILogger<RegisterCaseReportProvider> _logger = logger;

    public string ReportKey => ReportKeyName;

    public string GenerateFileName()
    {
        DateTime now = DateTime.UtcNow;
        string folderName = now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        return $"{folderName}/RAC_Journey_Report_{now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv";
    }

    public async Task<string> GenerateCsvContentAsync(LogsQueryClient client, string workspaceId, double timeRangeInDays)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new ArgumentException("Workspace id cannot be null or empty.", nameof(workspaceId));
        }

        if (timeRangeInDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeRangeInDays), "Time range in days must be greater than zero.");
        }

        var journeys = await QueryJourneysAsync(client, workspaceId, timeRangeInDays);

        if (journeys.Count == 0)
        {
            _logger.LogInformation("No Register a Case journey data found for the specified time range.");
            return string.Empty;
        }

        _logger.LogInformation("Writing {JourneyCount} Register a Case journeys to CSV.", journeys.Count);

        var sb = new StringBuilder();
        sb.AppendLine(CsvHeader);

        foreach (var journey in journeys)
        {
            sb.AppendLine(FormatLine(journey));
        }

        return sb.ToString();
    }

    private async Task<IReadOnlyList<QueryResultRegisterCase>> QueryJourneysAsync(LogsQueryClient client, string workspaceId, double timeRangeInDays)
    {
        try
        {
            var timeRange = new QueryTimeRange(TimeSpan.FromDays(timeRangeInDays));

            var response = await client.QueryWorkspaceAsync<QueryResultRegisterCase>(workspaceId, Query, timeRange);

            return response.Value ?? [];
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status403Forbidden)
        {
            _logger.LogError(ex, "Access denied (403) when querying Register a Case journeys. Please check the Log Analytics Reader role assignment.");
            throw;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Request to Log Analytics failed with status {status}.", ex.Status);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred while querying Register a Case journeys.");
            throw;
        }
    }

    private static string FormatLine(QueryResultRegisterCase journey) =>
        string.Join(',',
            Escape(journey.JourneyId),
            Escape(journey.Username),
            Escape(journey.AreaOrDivision),
            FormatDate(journey.StartedAt),
            FormatDate(journey.CompletedAt),
            Escape(journey.DropOffScreen),
            Escape(journey.Urn),
            Escape(journey.Outcome));

    private static string FormatDate(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString(DateTimeFormat, CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
