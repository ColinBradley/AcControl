using AcControl.Server.Data;
using AcControl.Server.Services;
using Blazorise;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
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

builder.Services
    .AddBlazorise()
    .AddEmptyProviders();

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
    .AddHostedService<UpdateBatteryDischargeLimitService>();

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

builder.WebHost.UseKestrel(k =>
{
    k.ConfigureHttpsDefaults(h =>
    {
        _ = h.UseLettuceEncrypt(k.ApplicationServices);
    });
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

app.Run();

