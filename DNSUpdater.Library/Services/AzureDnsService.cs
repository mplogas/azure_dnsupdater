using System.Net;
using Azure;
using Azure.ResourceManager;
using Azure.ResourceManager.Dns;
using Azure.ResourceManager.Dns.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DNSUpdater.Library.Services
{

    public class AzureDnsService : IDnsService
    {
        private readonly string subscriptionId;
        private readonly string rgName;
        private readonly ILogger<AzureDnsService> logger;
        private readonly ArmClient client;

        public AzureDnsService(ArmClient armClient, string subscriptionId, IConfiguration config, ILogger<AzureDnsService> logger)
        {
            this.subscriptionId = subscriptionId;
            this.rgName = config["rgName"]!;
            this.logger = logger;
            this.client = armClient;
        }

        public async Task<bool> IsKnown(string fqdn)
        {
            try
            {
                var domain = DisectFqdn(fqdn);
                return await GetRecordSet(domain) != null;
            }
            catch (Exception e)
            {
                this.logger.LogError(e, "Fail to find domain");
            }

            return false;
        }

        public async Task<UpdateStatus> Update(string fqdn, string ip)
        {
            try
            {
                var domain = DisectFqdn(fqdn);
                var record = await GetRecordSet(domain);

                if (record == null)
                    return UpdateStatus.nohost;

                if (record.ResourceType != DnsARecordResource.ResourceType)
                {
                    this.logger.LogWarning(
                        $"Tried to update a non-A record. RecordType {record.ResourceType.GetLastType()}, domain: {domain.fqdn}, ip: {ip}");
                    return UpdateStatus.nohost;
                }

                var address = IPAddress.Parse(ip);
                if (record.DnsARecords.Any(r => address.Equals(r.IPv4Address)))
                {
                    this.logger.LogInformation($"IP update not required. Domain: {domain.fqdn}, ip: {ip}");
                    return UpdateStatus.nochg;
                }

                // Replaces all addresses in the record set with the new one.
                var data = new DnsARecordData { TtlInSeconds = 300 };
                data.DnsARecords.Add(new DnsARecordInfo { IPv4Address = address });
                await GetDnsZone(domain).GetDnsARecords()
                    .CreateOrUpdateAsync(WaitUntil.Completed, domain.subdomain, data);

                this.logger.LogInformation($"IP update finished. Domain: {domain.fqdn}, ip: {ip}");
                return UpdateStatus.good;
            }
            catch (ApplicationException e)
            {
                this.logger.LogError(e, $"Failed to parse fqdn.  Domain: {fqdn}, ip: {ip}");
                return UpdateStatus.notfqdn;
            }
            catch (Exception e)
            {
                this.logger.LogError(e, "Fail to update domain");
            }

            return UpdateStatus.othererr;
        }

        private Domain DisectFqdn(string fqdn)
        {
            if (Uri.CheckHostName(fqdn) != UriHostNameType.Dns)
                throw new ApplicationException($"{fqdn} is not a valid domain");
            var parts = fqdn.Split(".");
            if (parts.Length <= 2) throw new ApplicationException($"{fqdn} does not contain a subdomain");

            var domain = $"{parts[^2]}.{parts[^1]}";
            var subdomain = fqdn.Replace("." + domain, "");

            return new Domain { domain = domain, fqdn = fqdn, subdomain = subdomain };
        }

        private DnsZoneResource GetDnsZone(Domain domain)
        {
            var zoneId = DnsZoneResource.CreateResourceIdentifier(this.subscriptionId, this.rgName, domain.domain);
            return this.client.GetDnsZoneResource(zoneId);
        }

        private async Task<DnsRecordData?> GetRecordSet(Domain domain)
        {
            //fqdn are returned with a tailing dot, read: as proper fqdn
            return await GetDnsZone(domain).GetAllRecordDataAsync().FirstOrDefaultAsync(r =>
                string.Equals(r.Fqdn, domain.fqdn + ".", StringComparison.InvariantCultureIgnoreCase));
        }

        private struct Domain
        {
            internal string fqdn;
            internal string subdomain;
            internal string domain;
        }
    }

    public enum UpdateStatus
    {
        good,
        nochg,
        nohost,
        notfqdn,
        badauth,
        othererr
    }
}
