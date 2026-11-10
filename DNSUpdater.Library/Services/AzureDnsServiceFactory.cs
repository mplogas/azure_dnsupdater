using Azure.Core;
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
        var armClient = new ArmClient(CreateCredential(aadSection), subscriptionId);
        var dnsService = new AzureDnsService(armClient, subscriptionId, this.config,
            this.loggerFactory.CreateLogger<AzureDnsService>());

        return dnsService;
    }

    /// <summary>
    /// Uses the app registration from the AzureAD section when a client secret is configured (local development).
    /// Otherwise uses a managed identity: the user-assigned one named by managedIdentityClientId,
    /// or the system-assigned one when that isn't set.
    /// </summary>
    internal static TokenCredential CreateCredential(IConfigurationSection aadSection)
    {
        var secret = aadSection["secret"];
        if (!string.IsNullOrEmpty(secret))
            return new ClientSecretCredential(aadSection["tenantId"], aadSection["clientId"], secret);

        var managedIdentityClientId = aadSection["managedIdentityClientId"];
        return new ManagedIdentityCredential(string.IsNullOrEmpty(managedIdentityClientId)
            ? ManagedIdentityId.SystemAssigned
            : ManagedIdentityId.FromUserAssignedClientId(managedIdentityClientId));
    }
}
