using AcControl.Server.Data;
using AcControl.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using MQTTnet.AspNetCore;
using MQTTnet.Protocol;
using MQTTnet.Server;
using MQTTnet.Server.Disconnecting;
using System.Text.Json;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

builder.Services
    .AddControllersWithViews()
    .AddMicrosoftIdentityUI();

builder.Services
    .AddRazorPages();

builder.Services
    .AddServerSideBlazor()
    .AddMicrosoftIdentityConsentHandler();

var dbConnectionString = new SqliteConnectionStringBuilder() { DataSource = "./HomeData.sqlite" }.ToString();
builder.Services
    .AddDbContextFactory<HomeDbContext>(o =>
        o.UseSqlite(dbConnectionString)
    );

builder.Services
    .AddHttpClient(LuxPowerTekService.HTTP_CLIENT_NAME)
    .ConfigurePrimaryHttpMessageHandler(
        () => new HttpClientHandler()
        {
            AllowAutoRedirect = false, // So we have a good signal for when logins work
            UseCookies = false, // No need and we want the server to always suggest a new cookie for us to store
        });

builder.Services
    .AddSingleton<AcDevicesService>()
    .AddSingleton<ToshibaAcHttpService>()
    .AddSingleton<ToshibaAcMqttService>()
    .AddSingleton<ApplicationService>()
    .AddSingleton<OalHomeData>()
    .AddSingleton<LuxPowerTekService>();

builder.Services
    .AddHostedService<UpdateBatteryDischargeLimitService>()
    .AddHostedService<AirGradientMqttRecorder>();

builder.Services
    .AddApplicationInsightsTelemetry(options =>
    {
        options.ConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
    });

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(builderContext =>
    {
        builderContext.AddOriginalHost(true);
    });

builder.Services
    .AddLettuceEncrypt();

builder.Services.AddHostedMqttServer(
    o =>
    {
        o.WithDefaultEndpoint();
    })
    .AddMqttConnectionHandler()
    .AddConnections();

builder.WebHost.UseKestrel(k =>
{
    k.ConfigureHttpsDefaults(h =>
    {
        _ = h.UseLettuceEncrypt(k.ApplicationServices);
    });
    
    k.ListenAnyIP(1883, l => l.UseMqtt());
    k.ListenAnyIP(443, l => l.UseHttps());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    _ = app.UseExceptionHandler("/Error");
}

app.Use(async (context, next) =>
{
    var host = context.Request.Host.Host;

    if (host != "jelly.m000.co.uk" && context.User.Identity?.IsAuthenticated != true)
    {
        await context.ChallengeAsync();
    }
    else
    {
        await next();
    }
});

app.UseHttpsRedirection();

app.MapReverseProxy();

app.UseStaticFiles();
app.UseRouting();

app.MapControllers();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.UseMqttServer(
    server =>
    {
        server.ValidatingConnectionAsync += e =>
        {
            Console.WriteLine("MQTT ValidatingConnectionAsync: " + JsonSerializer.Serialize(e));

            if (!string.Equals(e.Password, builder.Configuration.GetValue<string>("Mqtt:Password"), StringComparison.Ordinal)) {
                e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
            }
            
            return Task.CompletedTask;
        };

        app.Lifetime.ApplicationStopping.Register(async () =>
        {
            // We have to manually stop the server, otherwise ASP will just wait for a while then stop it
            // (Presumably trying a graceful stop of some kind that we're not handling?)
            await server.StopAsync(
                new MqttServerStopOptions()
                {
                    DefaultClientDisconnectOptions = new MqttServerClientDisconnectOptions()
                    {
                        ReasonCode = MqttDisconnectReasonCode.ServerShuttingDown,
                    },
                }
            );
        });
    });

await using (var setupScope = app.Services.CreateAsyncScope())
{
    var dbFactory = setupScope.ServiceProvider.GetRequiredService<IDbContextFactory<HomeDbContext>>();
    await using var context = await dbFactory.CreateDbContextAsync();
    await context.Database.MigrateAsync();

    var dnsUpdateUrl = app.Configuration.GetValue<string>("Dns:UpdateUrl");
    if (!string.IsNullOrEmpty(dnsUpdateUrl))
    {
        using var httpClient = setupScope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient();
        await httpClient.GetAsync(dnsUpdateUrl);
    }
}

app.Run();
