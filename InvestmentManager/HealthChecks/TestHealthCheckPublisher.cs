using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace InvestmentManager.HealthChecks
{
    public class TestHealthCheckPublisher : IHealthCheckPublisher
    {
        public async Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            // do something with HealthReport...
        }
    }
}
