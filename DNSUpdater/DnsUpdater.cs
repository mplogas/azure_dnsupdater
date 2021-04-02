using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DNSUpdater
{
    public class DnsUpdater
    {
        private readonly IConfiguration config;
        private readonly IAzure azureClient;

        public DnsUpdater(IConfiguration config, IAzure azureClient)
        {
            this.config = config;
            this.azureClient = azureClient;
        }

        [FunctionName("dnsUpdater")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Starting up...");

            string fqdn = req.Query["domain"];
            string key = req.Query["key"];
            string newIp = (req.Headers["X-Forwarded-For"].FirstOrDefault() ?? "").Split(new char[] { ':' }).FirstOrDefault();

            //debugging
            if (string.IsNullOrWhiteSpace(newIp))
            {

#if DEBUG
                newIp = req.HttpContext.Connection.RemoteIpAddress.ToString();
#else
                log.LogError($"IP is missing");
                return new BadRequestObjectResult("malformed request. please check logs.");
#endif
            }
                
            if(string.IsNullOrWhiteSpace(fqdn))
            {
                log.LogError($"hostname is missing, logged IP {newIp}");
                return new BadRequestObjectResult("malformed request. please check logs.");
            } else
            {
                fqdn = fqdn.ToLower();
                if (fqdn.EndsWith(".")) fqdn = fqdn.Remove(fqdn.Length - 1);
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                log.LogError($"key is missing, logged IP {newIp}");
                return new BadRequestObjectResult("malformed request. please check logs.");
            }
            else
            {
                key = key.ToLower();
            }


            var secret = this.config.GetValue<string>("secret");
            var hash = Helper.Helper.CreateHash(fqdn + ":" + secret);

            if (key != hash.ToLower())
            {
                log.LogError($"key does not match, logged IP {newIp}, requested domain {fqdn}");
                return new BadRequestObjectResult("malformed request. please check logs.");
            }
            string currentIp = "";

            try
            {
                currentIp = (await Dns.GetHostEntryAsync(fqdn)).AddressList.FirstOrDefault().ToString();
            }
            catch (SocketException e)
            {
                log.LogWarning($"Domain {fqdn} does not exist!");
                return new BadRequestObjectResult("malformed request. please check logs.");
            }

            if (newIp == currentIp)
            {
                log.LogInformation($"IP has not changed. logged IP {newIp}");
                return new OkObjectResult(newIp);
            }

            
            try
            {
                var rgName = this.config.GetValue<string>("rgName");

                if (Uri.CheckHostName(fqdn).Equals(UriHostNameType.Dns))
                {
                    var parts = fqdn.Split(".");
                    // for now its only subdomain.domain.tld
                    if(parts.Length == 3)
                    {
                        var rootDnsZone = await this.azureClient.DnsZones.GetByResourceGroupAsync(rgName, $"{parts[1]}.{parts[2]}");
                        rootDnsZone = rootDnsZone.Update()
                            .DefineARecordSet(parts[0])
                            .WithIPv4Address(newIp)
                            .WithTimeToLive(300)
                            .Attach()
                            .Apply();

                        log.LogInformation($"IP changed. logged IP {newIp}");
                        return new OkObjectResult(newIp);
                    } else
                    {
                        throw new ApplicationException($"unexpected fqdn length: {fqdn}");
                    }
                }
                else
                {
                    throw new ApplicationException($"unexpected fqdn format: {fqdn}");
                }
            }
            catch(Exception e)
            {
                log.LogError($"failed to update record. logged IP {newIp}");
                return new BadRequestObjectResult("server error. please check logs.");
            }
        }        
    }
}
