using System.ClientModel.Primitives;
using Azure;
using Azure.ResourceManager;
using Azure.ResourceManager.Dns;
using Azure.ResourceManager.Dns.Mocking;
using DNSUpdater.Library.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DNSUpdater.Tests;

/// <summary>
/// An in-memory stand-in for one Azure DNS zone, wired into a substitute <see cref="ArmClient"/>.
/// </summary>
internal sealed class FakeAzureDns
{
    public const string SubscriptionId = "00000000-0000-0000-0000-000000000001";
    public const string ResourceGroup = "dns-rg";

    private readonly List<DnsRecordData> records = new();
    private Exception? listFailure;

    public FakeAzureDns(string zoneName = "example.com")
    {
        ZoneName = zoneName;

        // ArmClient extension methods such as GetDnsZoneResource go through GetCachedClient,
        // which is virtual so tests can hand out a mockable client.
        var mockableClient = Substitute.For<MockableDnsArmClient>();
        Client.GetCachedClient(Arg.Any<Func<ArmClient, MockableDnsArmClient>>()).Returns(mockableClient);

        var zoneId = DnsZoneResource.CreateResourceIdentifier(SubscriptionId, ResourceGroup, zoneName);
        mockableClient.GetDnsZoneResource(zoneId).Returns(Zone);

        Zone.GetAllRecordDataAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => AsyncPageable<DnsRecordData>.FromPages(ListPages()));
        Zone.GetDnsARecords().Returns(ARecords);
        ARecords.CreateOrUpdateAsync(default, default!, default!, default, default, default)
            .ReturnsForAnyArgs(Substitute.For<ArmOperation<DnsARecordResource>>());
    }

    public string ZoneName { get; }
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

    /// <summary>Adds an A record set, using the JSON shape the Azure DNS REST API returns.</summary>
    public void AddARecord(string name, string[] ipv4Addresses, string type = "Microsoft.Network/dnszones/A")
    {
        var addresses = string.Join(",", ipv4Addresses.Select(ip => $$"""{ "ipv4Address": "{{ip}}" }"""));
        AddRecord(name, type, $$"""{ "TTL": 3600, "fqdn": "{{name}}.{{ZoneName}}.", "ARecords": [ {{addresses}} ] }""");
    }

    public void AddCnameRecord(string name, string target)
    {
        AddRecord(name, "Microsoft.Network/dnszones/CNAME",
            $$"""{ "TTL": 3600, "fqdn": "{{name}}.{{ZoneName}}.", "CNAMERecord": { "cname": "{{target}}" } }""");
    }

    /// <summary>Makes listing the zone fail the way the SDK does for a missing zone.</summary>
    public void FailListingWith(Exception exception) => listFailure = exception;

    /// <summary>Makes every write to an A record set fail.</summary>
    public void FailWritesWith(Exception exception) =>
        ARecords.CreateOrUpdateAsync(default, default!, default!, default, default, default)
            .ThrowsAsyncForAnyArgs(exception);

    private void AddRecord(string name, string type, string propertiesJson)
    {
        var shortType = type[(type.LastIndexOf('/') + 1)..];
        var json = $$"""
            {
              "id": "/subscriptions/{{SubscriptionId}}/resourceGroups/{{ResourceGroup}}/providers/Microsoft.Network/dnszones/{{ZoneName}}/{{shortType}}/{{name}}",
              "name": "{{name}}",
              "type": "{{type}}",
              "etag": "00000000-0000-0000-0000-000000000000",
              "properties": {{propertiesJson}}
            }
            """;
        records.Add(ModelReaderWriter.Read<DnsRecordData>(BinaryData.FromString(json))!);
    }

    // An iterator, so a configured failure is thrown while enumerating, as with the real SDK.
    private IEnumerable<Page<DnsRecordData>> ListPages()
    {
        if (listFailure != null)
            throw listFailure;

        yield return Page<DnsRecordData>.FromValues(records.ToList(), null, Substitute.For<Response>());
    }
}
