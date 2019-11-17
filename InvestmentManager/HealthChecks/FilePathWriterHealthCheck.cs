using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

public class FilePathWriterHealthCheck : IHealthCheck
{
    private readonly string _filePath;
    private IReadOnlyDictionary<string, object> _healthCheckData;

    public FilePathWriterHealthCheck(string filePath)
    {
        _filePath = filePath;
        _healthCheckData = new Dictionary<string, object>
            {
                ["filePath"] = filePath
            };
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var testFile = $"{_filePath}\\test.txt";
            var fs = File.Create(testFile);
            fs.Close();
            File.Delete(testFile);

            return Task.FromResult(
                HealthCheckResult.Healthy($"Application has read and write permissions to {_filePath}"));
        }
        catch (Exception e)
        {
            switch(context.Registration.FailureStatus)
            {
                case HealthStatus.Degraded:
                    return Task.FromResult(
                        HealthCheckResult.Degraded("Issues writing to file path.",
                            e, _healthCheckData));
                case HealthStatus.Healthy:
                    return Task.FromResult(
                        HealthCheckResult.Healthy("Issues writing to file path.", _healthCheckData));

                default: 
                    return Task.FromResult(
                        HealthCheckResult.Unhealthy("Issues writing to file path.",
                            e, _healthCheckData));
            }
        }
    }
}
