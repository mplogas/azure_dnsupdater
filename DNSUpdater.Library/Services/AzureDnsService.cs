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
                return await GetARecord(domain) != null;
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
                var record = await GetARecord(domain);

                if (record == null)
                    return UpdateStatus.nohost;

                var address = IPAddress.Parse(ip);
                if (record.DnsARecords.Any(r => address.Equals(r.IPv4Address)))
                {
                    this.logger.LogInformation($"IP update not required. Domain: {domain.fqdn}, ip: {address}");
                    return UpdateStatus.nochg;
                }

                // Replaces all addresses in the record set with the new one.
                var data = new DnsARecordData { TtlInSeconds = 300 };
                data.DnsARecords.Add(new DnsARecordInfo { IPv4Address = address });
                await GetDnsZone(domain).GetDnsARecords()
                    .CreateOrUpdateAsync(WaitUntil.Completed, domain.subdomain, data);

                this.logger.LogInformation($"IP update finished. Domain: {domain.fqdn}, ip: {address}");
                return UpdateStatus.good;
            }
            catch (ApplicationException e)
            {
                // The hostname failed validation here, so it may contain anything: escape it before logging.
                this.logger.LogError(e, $"Failed to parse fqdn. Domain: {LogValue.Escape(fqdn)}, ip: {LogValue.Escape(ip)}");
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
            // The messages leave out the hostname; the caller logs it escaped.
            if (Uri.CheckHostName(fqdn) != UriHostNameType.Dns)
                throw new ApplicationException("The hostname is not a valid DNS name");
            var parts = fqdn.Split(".");
            if (parts.Length <= 2) throw new ApplicationException("The hostname does not contain a subdomain");

            var domain = $"{parts[^2]}.{parts[^1]}";
            var subdomain = fqdn.Replace("." + domain, "");

            return new Domain { domain = domain, fqdn = fqdn, subdomain = subdomain };
        }

        private DnsZoneResource GetDnsZone(Domain domain)
        {
            var zoneId = DnsZoneResource.CreateResourceIdentifier(this.subscriptionId, this.rgName, domain.domain);
            return this.client.GetDnsZoneResource(zoneId);
        }

        /// <summary>
        /// Reads the A record set directly instead of listing the zone, so the app identity only needs
        /// a role on the record sets it may update. Returns null for a missing A record or zone (404),
        /// and for a record set the identity has no role on (403).
        /// </summary>
        private async Task<DnsARecordData?> GetARecord(Domain domain)
        {
            try
            {
                var record = await GetDnsZone(domain).GetDnsARecords().GetAsync(domain.subdomain);
                return record.Value.Data;
            }
            catch (RequestFailedException e) when (e.Status is 403 or 404)
            {
                this.logger.LogWarning($"No A record the app may update. Domain: {domain.fqdn}, status: {e.Status} {e.ErrorCode}");
                return null;
            }
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
