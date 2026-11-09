using Azure.Identity;
using DNSUpdater.Library.Services;
using Microsoft.Extensions.Configuration;

namespace DNSUpdater.Tests;

public class AzureDnsServiceFactoryTests
{
    [Fact]
    public void CreateCredential_WithClientSecret_UsesAppRegistration()
    {
        var section = AadSection(new()
        {
            ["AzureAD:tenantId"] = "00000000-0000-0000-0000-000000000002",
            ["AzureAD:clientId"] = "00000000-0000-0000-0000-000000000003",
            ["AzureAD:secret"] = "client-secret",
        });

        Assert.IsType<ClientSecretCredential>(AzureDnsServiceFactory.CreateCredential(section));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000004")]
    public void CreateCredential_WithoutClientSecret_UsesManagedIdentity(string? managedIdentityClientId)
    {
        var section = AadSection(new()
        {
            ["AzureAD:subscriptionId"] = "00000000-0000-0000-0000-000000000001",
            ["AzureAD:managedIdentityClientId"] = managedIdentityClientId,
        });

        Assert.IsType<ManagedIdentityCredential>(AzureDnsServiceFactory.CreateCredential(section));
    }

    private static IConfigurationSection AadSection(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("AzureAD");
}
