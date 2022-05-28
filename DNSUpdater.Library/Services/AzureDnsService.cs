using Microsoft.Azure.Management.Dns.Fluent;
using Microsoft.Azure.Management.Dns.Fluent.Models;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DNSUpdater.Library.Services
{

    public class AzureDnsService : IDnsService
    {
        private readonly string rgName;
        private readonly ILogger<AzureDnsService> logger;
        private readonly IAzure client;

        public AzureDnsService(IAzure azureClient, IConfiguration config, ILogger<AzureDnsService> logger)
        {
            this.rgName = config["rgName"];
            this.logger = logger;
            this.client = azureClient;
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

                if (record != null)
                {
                    switch (record.RecordType)
                    {
                        case RecordType.A:
                            var rec = record.Inner.ARecords.FirstOrDefault(r =>
                                r.Ipv4Address.Equals(ip, StringComparison.InvariantCultureIgnoreCase));
                            if (rec != null)
                            {
                                this.logger.LogInformation($"IP update not required. Domain: {domain.fqdn}, ip: {ip}");
                                return UpdateStatus.nochg;
                            }
                            else
                            {
                                // TODO: vorm essen hier
                                // var rootDnsZone = record. .Update()
                                //     .DefineARecordSet(subdomain)
                                //     .WithIPv4Address(newIp)
                                //     .WithTimeToLive(300)
                                //     .Attach()
                                //     .Apply();
                                
                                this.logger.LogInformation($"IP update finished. Domain: {domain.fqdn}, ip: {ip}");
                                return UpdateStatus.good;
                            }
                            break;
                        case RecordType.AAAA:
                        case RecordType.CAA:
                        case RecordType.CNAME:
                        case RecordType.MX:
                        case RecordType.NS:
                        case RecordType.PTR:
                        case RecordType.SOA:
                        case RecordType.SRV:
                        case RecordType.TXT:
                        default:
                            this.logger.LogWarning($"Tried to update a non-A record. RecordType {record.RecordType}, domain: {domain.fqdn}, ip: {ip}");
                            return UpdateStatus.nohost;
                    }
                }
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

        private async Task<IDnsRecordSet?> GetRecordSet(Domain domain)
        {
            var rootDnsZone = await this.client.DnsZones.GetByResourceGroupAsync(rgName, domain.domain);
            var record = rootDnsZone.ListRecordSets().FirstOrDefault(r =>
                r.Fqdn.Equals(domain.fqdn + ".",
                    StringComparison
                        .InvariantCultureIgnoreCase)); //fqdn are returned with a tailing dot, read: as proper fqdn

            return record;
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
        othererr
    }
}