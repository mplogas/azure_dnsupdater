using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

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
        var aadSection = this.config.GetSection("AzureAD");
        var subscriptionId = aadSection["subscriptionId"]!;
        var armClient = GetArmClient(aadSection, subscriptionId);
        var dnsService = new AzureDnsService(armClient, subscriptionId, this.config,
            this.loggerFactory.CreateLogger<AzureDnsService>());

        return dnsService;
    }

    private static ArmClient GetArmClient(IConfigurationSection aadSection, string subscriptionId)
    {
        var credential = new ClientSecretCredential(aadSection["tenantId"], aadSection["clientId"], aadSection["secret"]);
        return new ArmClient(credential, subscriptionId);
    }
}
