using System.Net;
using Azure;
using Azure.ResourceManager;
using Azure.ResourceManager.Dns;
using DNSUpdater.Library.Services;
using NSubstitute;

namespace DNSUpdater.Tests;

public class AzureDnsServiceTests
{
    private readonly FakeAzureDns azure = new();

    [Fact]
    public async Task Update_RecordAlreadyHasIp_ReturnsNochgWithoutWriting()
    {
        azure.AddARecord("home", ["203.0.113.10"]);

        var result = await azure.CreateService().Update("home.example.com", "203.0.113.10");

        Assert.Equal(UpdateStatus.nochg, result);
        await AssertNoWrite();
    }

    [Fact]
    public async Task Update_OneOfSeveralIpsMatches_ReturnsNochg()
    {
        azure.AddARecord("home", ["198.51.100.1", "203.0.113.10"]);

        var result = await azure.CreateService().Update("home.example.com", "203.0.113.10");

        Assert.Equal(UpdateStatus.nochg, result);
        await AssertNoWrite();
    }

    [Fact]
    public async Task Update_NewIp_ReplacesRecordSetAndReturnsGood()
    {
        azure.AddARecord("home", ["198.51.100.1", "198.51.100.2"]);

        var result = await azure.CreateService().Update("home.example.com", "203.0.113.10");

        Assert.Equal(UpdateStatus.good, result);
        await AssertWritten("home", "203.0.113.10");
    }

    [Fact]
    public async Task Update_NestedSubdomain_WritesRelativeRecordName()
    {
        azure.AddARecord("vpn.home", ["198.51.100.1"]);

        var result = await azure.CreateService().Update("vpn.home.example.com", "203.0.113.10");

        Assert.Equal(UpdateStatus.good, result);
        await AssertWritten("vpn.home", "203.0.113.10");
    }

    [Fact]
    public async Task Update_NoSuchRecord_ReturnsNohost()
    {
        azure.AddARecord("other", ["198.51.100.1"]);

        var result = await azure.CreateService().Update("home.example.com", "203.0.113.10");

        Assert.Equal(UpdateStatus.nohost, result);
        await AssertNoWrite();
    }

    [Fact]
    public async Task Update_RecordOutsideIdentityScope_ReturnsNohost()
    {
        azure.AddARecord("home", ["198.51.100.1"]);
        azure.FailReadsWith(new RequestFailedException(403, "AuthorizationFailed"));

        var result = await azure.CreateService().Update("home.example.com", "203.0.113.10");

        Assert.Equal(UpdateStatus.nohost, result);
        await AssertNoWrite();
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("not a hostname")]
    [InlineData("")]
    public async Task Update_HostnameWithoutSubdomainOrInvalid_ReturnsNotfqdn(string hostname)
    {
        var result = await azure.CreateService().Update(hostname, "203.0.113.10");

        Assert.Equal(UpdateStatus.notfqdn, result);
        await AssertNoWrite();
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("300.1.1.1")]
    public async Task Update_InvalidIp_ReturnsOthererrWithoutWriting(string ip)
    {
        azure.AddARecord("home", ["198.51.100.1"]);

        var result = await azure.CreateService().Update("home.example.com", ip);

        Assert.Equal(UpdateStatus.othererr, result);
        await AssertNoWrite();
    }

    [Fact]
    public async Task Update_ReadFails_ReturnsOthererr()
    {
        azure.AddARecord("home", ["198.51.100.1"]);
        azure.FailReadsWith(new RequestFailedException(500, "InternalServerError"));

        var result = await azure.CreateService().Update("home.example.com", "203.0.113.10");

        Assert.Equal(UpdateStatus.othererr, result);
        await AssertNoWrite();
    }

    [Fact]
    public async Task Update_WriteFails_ReturnsOthererr()
    {
        azure.AddARecord("home", ["198.51.100.1"]);
        azure.FailWritesWith(new RequestFailedException(403, "Forbidden"));

        var result = await azure.CreateService().Update("home.example.com", "203.0.113.10");

        Assert.Equal(UpdateStatus.othererr, result);
    }

    [Fact]
    public async Task IsKnown_ExistingARecord_ReturnsTrue()
    {
        azure.AddARecord("home", ["198.51.100.1"]);

        Assert.True(await azure.CreateService().IsKnown("home.example.com"));
    }

    [Fact]
    public async Task IsKnown_RecordOutsideIdentityScope_ReturnsFalse()
    {
        azure.AddARecord("home", ["198.51.100.1"]);
        azure.FailReadsWith(new RequestFailedException(403, "AuthorizationFailed"));

        Assert.False(await azure.CreateService().IsKnown("home.example.com"));
    }

    [Theory]
    [InlineData("missing.example.com")]
    [InlineData("example.com")]
    public async Task IsKnown_MissingRecordOrInvalidName_ReturnsFalse(string fqdn)
    {
        azure.AddARecord("home", ["198.51.100.1"]);

        Assert.False(await azure.CreateService().IsKnown(fqdn));
    }

    private Task<ArmOperation<DnsARecordResource>> AssertWritten(string recordName, string ip) =>
        azure.ARecords.Received(1).CreateOrUpdateAsync(
            WaitUntil.Completed,
            recordName,
            Arg.Is<DnsARecordData>(data =>
                data.TtlInSeconds == 300 &&
                data.DnsARecords.Count == 1 &&
                IPAddress.Parse(ip).Equals(data.DnsARecords[0].IPv4Address)),
            Arg.Any<ETag?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());

    private Task<ArmOperation<DnsARecordResource>> AssertNoWrite() =>
        azure.ARecords.DidNotReceiveWithAnyArgs().CreateOrUpdateAsync(default, default!, default!, default, default, default);
}
