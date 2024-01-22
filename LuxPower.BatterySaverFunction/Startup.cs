using LuxPower.BatterySaverFunction;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Net.Http;

[assembly: FunctionsStartup(typeof(Startup))]

namespace LuxPower.BatterySaverFunction;

public class Startup : FunctionsStartup
{
    public override void Configure(IFunctionsHostBuilder builder)
    {
        builder.Services
            .AddHttpClient("LUX")
            .ConfigurePrimaryHttpMessageHandler(
                () => new HttpClientHandler()
                {
                    AllowAutoRedirect = false, // So we have a good signal for when logins work
                    UseCookies = false, // No need and we want the server to always suggest a new cookie for us to store
                });
    }

    public override void ConfigureAppConfiguration(IFunctionsConfigurationBuilder builder)
    {
        var context = builder.GetContext();
        builder.ConfigurationBuilder
            .AddJsonFile(Path.Combine(context.ApplicationRootPath, "appsettings.json"), optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Combine(context.ApplicationRootPath, $"appsettings.{context.EnvironmentName}.json"), optional: true, reloadOnChange: false)
            .AddUserSecrets(typeof(Startup).Assembly, optional: true)
            .AddEnvironmentVariables();
    }
}
