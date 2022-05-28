using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Management.Dns.Fluent.Models;
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
            
            try
            {
                var rgName = this.config.GetValue<string>("rgName");

                if (Uri.CheckHostName(fqdn).Equals(UriHostNameType.Dns))
                {
                    var parts = fqdn.Split(".");
                    if(parts.Length > 2)
                    {
                        var domain = $"{parts[parts.Length - 2]}.{parts[parts.Length - 1]}";
                        var subdomain = fqdn.Replace("." + domain, "");

                        var rootDnsZone = await this.azureClient.DnsZones.GetByResourceGroupAsync(rgName, domain);
                        var record = rootDnsZone.ListRecordSets().FirstOrDefault(r => r.Fqdn.Equals(fqdn + ".", StringComparison.InvariantCultureIgnoreCase)); //fqdn are returned with a tailing dot, read: as proper fqdn

                        if (record != null) 
                        {
                            switch (record.RecordType)
                            {
                                case RecordType.A:
                                    var aRec = record.Inner.ARecords.Where(r => r.Ipv4Address.Equals(newIp, StringComparison.InvariantCultureIgnoreCase));
                                    if (aRec != null)
                                    {
                                        log.LogInformation($"IP has not changed. logged IP {newIp}");
                                        return new OkObjectResult("no change");
                                    } else
                                    {
                                        rootDnsZone = rootDnsZone.Update()
                                            .DefineARecordSet(subdomain)
                                            .WithIPv4Address(newIp)
                                            .WithTimeToLive(300)
                                            .Attach()
                                            .Apply();

                                        log.LogInformation($"IP changed. logged IP {newIp}");
                                        return new OkObjectResult(newIp);
                                    }
                                default:
                                    //if you want to update MX records, create a new case. keep in mind, MX requires prio
                                    log.LogInformation($"Tried to update a non-A record. logged IP {newIp}, requested domain {fqdn}");
                                    return new BadRequestObjectResult("unexpected request. please check logs.");
                            }
                        } else
                        {
                            log.LogWarning($"Domain {fqdn} is not managed in Azure!");
                            return new BadRequestObjectResult("unexpected request. please check logs.");
                        }
                    }
                    else
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