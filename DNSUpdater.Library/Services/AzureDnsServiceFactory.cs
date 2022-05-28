using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Rest.Azure;

namespace DNSUpdater.Library.Services;

public class AzureDnsServiceFactory : IDnsServiceFactory
{
    private readonly IConfiguration config;
    private readonly ILogger<AzureDnsServiceFactory> logger;
    private readonly ILoggerFactory loggerFactory;

    public AzureDnsServiceFactory(IConfiguration config, ILoggerFactory loggerFactory)
    {
        this.config = config;
        this.logger = loggerFactory.CreateLogger<AzureDnsServiceFactory>();
        this.loggerFactory = loggerFactory;
    }

    public IDnsService GetDnsService()
    {
        var azureClient = GetAzureClient(this.config.GetSection("AzureAD"));
        var dnsService =
            new AzureDnsService(azureClient, this.config, this.loggerFactory.CreateLogger<AzureDnsService>());

        return dnsService;
    }

    private IAzure GetAzureClient(IConfigurationSection aadSection)
    {
        var credentials = SdkContext.AzureCredentialsFactory.FromServicePrincipal(aadSection["clientId"], aadSection["secret"], aadSection["tenantId"], AzureEnvironment.AzureGlobalCloud);
        var azure = Azure
            .Configure()
            .WithLogLevel(HttpLoggingDelegatingHandler.Level.BodyAndHeaders)
            .Authenticate(credentials)
            .WithSubscription(aadSection["subscriptionId"]);

        return azure;
    }
}