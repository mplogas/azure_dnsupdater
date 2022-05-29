
using System;
using System.Reflection;
using DNSUpdater.Function;
using DNSUpdater.Library.Services;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: FunctionsStartup(typeof(Startup))]
namespace DNSUpdater.Function
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Environment.CurrentDirectory)
                .AddJsonFile("appsettings.json", true)
                .AddUserSecrets(Assembly.GetExecutingAssembly(), true)
                .AddEnvironmentVariables()
                .Build();
            
            // Log.Logger = new LoggerConfiguration()
            //     .ReadFrom.Configuration(config)
            //     .CreateLogger();

            builder.Services.AddSingleton(config);
            //builder.Services.AddLogging(c => c.AddSerilog().AddConsole());
            builder.Services.AddSingleton<IDnsServiceFactory, AzureDnsServiceFactory>();
        }
    }
}

