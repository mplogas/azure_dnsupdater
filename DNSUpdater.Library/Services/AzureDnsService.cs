using Microsoft.Azure.Management.Fluent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DNSUpdater.Library.Services
{

    public class AzureDnsService : IDnsService
    {
        private readonly IConfiguration config;
        private readonly ILogger<AzureDnsService> logger;
        private readonly IAzure client;

        public AzureDnsService(IAzure azureClient, IConfiguration config, ILogger<AzureDnsService> logger)
        {
            this.config = config;
            this.logger = logger;
            this.client = azureClient;
        }

        public bool IsKnown(string fqdn)
        {
            return true;
        }

        public async Task<UpdateStatus> Update(string fqdn, string ip)
        {
            return UpdateStatus.good;
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