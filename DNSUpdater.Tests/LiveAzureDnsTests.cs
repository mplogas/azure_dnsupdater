using DNSUpdater.Library.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DNSUpdater.Tests;

/// <summary>
/// Runs against a real Azure DNS zone with the credentials from user secrets or environment variables.
/// Skipped unless LiveTest:fqdn (env: LiveTest__fqdn) names an existing A record that may be overwritten.
/// </summary>
public class LiveAzureDnsTests(ITestOutputHelper output)
{
    private readonly IConfiguration config = new ConfigurationBuilder()
        .AddUserSecrets<LiveAzureDnsTests>(optional: true)
        .AddEnvironmentVariables()
        .Build();

    [Fact]
    public async Task Update_AgainstRealZone_BehavesLikeDynDns()
    {
        var fqdn = config["LiveTest:fqdn"];
        Assert.SkipWhen(string.IsNullOrEmpty(fqdn), "Set LiveTest:fqdn to a test A record to run this against Azure DNS.");

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new TestOutputLoggerProvider(output)));
        var service = new AzureDnsServiceFactory(config, loggerFactory).GetDnsService();
        var zone = string.Join('.', fqdn!.Split('.')[^2..]);

        Assert.True(await service.IsKnown(fqdn), $"{fqdn} must exist as a record before running this test.");

        // Documentation addresses (RFC 5737), so the record never points at a real host.
        Assert.Contains(await service.Update(fqdn, "192.0.2.1"), new[] { UpdateStatus.good, UpdateStatus.nochg });
        Assert.Equal(UpdateStatus.nochg, await service.Update(fqdn, "192.0.2.1"));
        Assert.Equal(UpdateStatus.good, await service.Update(fqdn, "192.0.2.2"));
        Assert.Equal(UpdateStatus.nohost, await service.Update($"dnsupdater-missing-{Guid.NewGuid():N}.{zone}", "192.0.2.1"));
    }

    private sealed class TestOutputLoggerProvider(ITestOutputHelper output) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new TestOutputLogger(output, categoryName);

        public void Dispose()
        {
        }

        private sealed class TestOutputLogger(ITestOutputHelper output, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                output.WriteLine($"[{logLevel}] {category}: {formatter(state, exception)}");
                if (exception != null)
                    output.WriteLine(exception.ToString());
            }
        }
    }
}
