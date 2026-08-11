using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace YuruChara.Api.Health;

/// <summary>
/// Writes the health report as JSON. The framework's default response writer
/// returns only the word "Healthy" as plain text. That is sufficient for a load
/// balancer, but it gives a developer no information about why the application
/// cannot reach the database.
/// </summary>
internal static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            durationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                error = entry.Value.Exception?.Message,
                data = entry.Value.Data
            })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, Json));
    }
}
