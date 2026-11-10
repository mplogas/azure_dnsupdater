using System.Net;
using Azure;
using Azure.ResourceManager;
using Azure.ResourceManager.Dns;
using Azure.ResourceManager.Dns.Mocking;
using Azure.ResourceManager.Dns.Models;
using DNSUpdater.Library.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DNSUpdater.Tests;

/// <summary>
/// An in-memory stand-in for the A records of one Azure DNS zone, wired into a substitute <see cref="ArmClient"/>.
/// </summary>
internal sealed class FakeAzureDns
{
    public const string SubscriptionId = "00000000-0000-0000-0000-000000000001";
    public const string ResourceGroup = "dns-rg";

    // Azure DNS record set names are case-insensitive.
    private readonly Dictionary<string, DnsARecordData> aRecords = new(StringComparer.OrdinalIgnoreCase);
    private Exception? readFailure;

    public FakeAzureDns(string zoneName = "example.com")
    {
        // ArmClient extension methods such as GetDnsZoneResource go through GetCachedClient,
        // which is virtual so tests can hand out a mockable client.
        var mockableClient = Substitute.For<MockableDnsArmClient>();
        Client.GetCachedClient(Arg.Any<Func<ArmClient, MockableDnsArmClient>>()).Returns(mockableClient);

        var zoneId = DnsZoneResource.CreateResourceIdentifier(SubscriptionId, ResourceGroup, zoneName);
        mockableClient.GetDnsZoneResource(zoneId).Returns(Zone);

        Zone.GetDnsARecords().Returns(ARecords);
        ARecords.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => GetARecord(call.Arg<string>()));
        ARecords.CreateOrUpdateAsync(default, default!, default!, default, default, default)
            .ReturnsForAnyArgs(Substitute.For<ArmOperation<DnsARecordResource>>());
    }

    public ArmClient Client { get; } = Substitute.For<ArmClient>();
    public DnsZoneResource Zone { get; } = Substitute.For<DnsZoneResource>();
    public DnsARecordCollection ARecords { get; } = Substitute.For<DnsARecordCollection>();

    public AzureDnsService CreateService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["rgName"] = ResourceGroup })
            .Build();

        return new AzureDnsService(Client, SubscriptionId, config, NullLogger<AzureDnsService>.Instance);
    }

    /// <summary>Adds an A record set with the given name relative to the zone.</summary>
    public void AddARecord(string name, string[] ipv4Addresses)
    {
        var data = new DnsARecordData { TtlInSeconds = 3600 };
        foreach (var ip in ipv4Addresses)
            data.DnsARecords.Add(new DnsARecordInfo { IPv4Address = IPAddress.Parse(ip) });
        aRecords[name] = data;
    }

    /// <summary>Makes every read of an A record set fail, for example with the 403 Azure returns outside the identity's scope.</summary>
    public void FailReadsWith(Exception exception) => readFailure = exception;

    /// <summary>Makes every write to an A record set fail.</summary>
    public void FailWritesWith(Exception exception) =>
        ARecords.CreateOrUpdateAsync(default, default!, default!, default, default, default)
            .ThrowsAsyncForAnyArgs(exception);

    // Faults the returned task instead of throwing, as the SDK's async methods do.
    private Task<Response<DnsARecordResource>> GetARecord(string name)
    {
        if (readFailure != null)
            return Task.FromException<Response<DnsARecordResource>>(readFailure);

        if (!aRecords.TryGetValue(name, out var data))
            return Task.FromException<Response<DnsARecordResource>>(
                new RequestFailedException(404, $"The resource record '{name}' does not exist."));

        var resource = Substitute.For<DnsARecordResource>();
        resource.Data.Returns(data);
        return Task.FromResult(Response.FromValue(resource, Substitute.For<Response>()));
    }
}
