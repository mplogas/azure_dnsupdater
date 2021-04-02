using DNSUpdater;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Reflection;


// register the assembly
[assembly: FunctionsStartup(typeof(Startup))]
namespace DNSUpdater
{
    class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            var config = new ConfigurationBuilder()
               .SetBasePath(Environment.CurrentDirectory)
               .AddJsonFile("appsettings.json", true)
               .AddUserSecrets(Assembly.GetExecutingAssembly(), true)
               .AddEnvironmentVariables()
               .Build();

            builder.Services.AddSingleton<IConfiguration>(config);

            var subscriptionId = config.GetValue<string>("AzureAD:subscriptionId");
            var tenantId = config.GetValue<string>("AzureAD:tenantId");
            var clientId = config.GetValue<string>("AzureAD:clientId");
            var secret = config.GetValue<string>("AzureAD:secret");

            var credentials = SdkContext.AzureCredentialsFactory.FromServicePrincipal(clientId, secret, tenantId, AzureEnvironment.AzureGlobalCloud);
            var azure = Azure
            .Configure()
            .WithLogLevel(HttpLoggingDelegatingHandler.Level.BodyAndHeaders)
            .Authenticate(credentials)
            .WithSubscription(subscriptionId);

            builder.Services.AddSingleton<IAzure>(azure);
        }
    }
}
