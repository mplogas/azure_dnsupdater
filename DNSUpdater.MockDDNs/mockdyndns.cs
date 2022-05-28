using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace DNSUpdater.MockDDNs;

public static class mockdyndns
{
    [FunctionName("update")]
    public static async Task<IActionResult> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req, ILogger log)
    {
        log.LogInformation("C# HTTP trigger function processed a request.");

        string hostname = req.Query["hostname"];
        string ip = req.Query["myip"];
        string system = req.Query["dyndns"];
        string token = req.Headers["Authorization"];
        //For "Basic" authentication the credentials are constructed by first combining the username and the password with a colon (aladdin:opensesame), and then by encoding the resulting string in base64 (YWxhZGRpbjpvcGVuc2VzYW1l).
        string agent = req.Headers["User-Agent"];
        
        log.LogDebug($"request details:\nhostname: {hostname}\nip: {ip}\ntoken: {token}\nagent: {agent}\n");

        
        
        return new OkObjectResult("good");
    }
}