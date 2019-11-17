using InvestmentManager.HealthChecks;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class FilePathHealthCheckBuilderExtensions
    {
        public static IHealthChecksBuilder AddFilePathWriter(this IHealthChecksBuilder builder,
            string filePath, HealthStatus failureStatus, IEnumerable<string> tags = default)
        {
            if (filePath == null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            return builder.Add(new HealthCheckRegistration(
                "Filepath write",
                new FilePathWriterHealthCheck(filePath),
                failureStatus,
                tags));
        }
    }
}