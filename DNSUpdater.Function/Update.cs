using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using DNSUpdater.Library.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DNSUpdater.Function
{
    public class Update
    {
        private readonly IDnsService service;
        private readonly ILogger<Update> logger;
        private readonly List<string> tokenList;

        public Update(IDnsServiceFactory serviceFactory, IConfiguration config, ILogger<Update> logger)
        {
            this.service = serviceFactory.GetDnsService();
            this.logger = logger;
            this.tokenList = GetTokenList(config.GetSection("Authorization"));
        }


        [FunctionName("update")]
        public async Task<IActionResult> RunAsync(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)]
            HttpRequest req)
        {
            string hostname = req.Query["hostname"];
            string ip = req.Query["myip"];
            string system = req.Query["system"];
            string
                token = req.Headers[
                    "Authorization"]; //For "Basic" authentication the credentials are constructed by first combining the username and the password with a colon (aladdin:opensesame), and then by encoding the resulting string in base64 (YWxhZGRpbjpvcGVuc2VzYW1l).
            string agent = req.Headers["User-Agent"];

            if (string.IsNullOrWhiteSpace(hostname) ||
                string.IsNullOrWhiteSpace(ip) ||
                string.IsNullOrWhiteSpace(token) ||
                string.IsNullOrWhiteSpace(agent))
                return new BadRequestObjectResult(UpdateStatus.othererr.ToString());

            this.logger.LogDebug(
                $"request details:\nhostname: {hostname}\nip: {ip}\ntoken: {token}\nagent: {agent}\nsystem: {system}");

            token = token.Replace("Basic ", "");
            if (!tokenList.Contains(token))
            {
                this.logger.LogWarning($"Unauthorized request. Provided token {token}");
                return new ObjectResult(UpdateStatus.badauth.ToString()) { StatusCode = 401 };
                //return new UnauthorizedResult();
            }

            try
            {
                var result = await this.service.Update(hostname, ip);
                if (result == UpdateStatus.good || result == UpdateStatus.nochg)
                    return new OkObjectResult(result.ToString());
                else return new ConflictObjectResult(result.ToString());
            }
            catch (Exception e)
            {
                this.logger.LogError(e, "Exception thrown");
                return new InternalServerErrorResult();
            }
        }

        private List<string> GetTokenList(IConfigurationSection authSection)
        {
            return authSection.GetChildren().Select(entry => $"{entry["user"]}:{entry["secret"]}").Select(Base64Encode)
                .ToList();
        }

        private string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }

        private string Base64Decode(string base64EncodedData)
        {
            var base64EncodedBytes = System.Convert.FromBase64String(base64EncodedData);
            return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
        }
    }
}