using AcControl.Server.Data;
using AcControl.Server.Services;
using Blazorise;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

builder.Services
    .AddControllersWithViews()
    .AddMicrosoftIdentityUI();

builder.Services
    .AddAuthorization(options =>
    {
        // By default, all incoming requests will be authorized according to the default policy
        options.FallbackPolicy = options.DefaultPolicy;
    });

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
        () => new HttpClientHandler() { 
            AllowAutoRedirect = false, // So we have a good signal for when logins work
            UseCookies = false, // No need and we want the server to always suggest a new cookie for us to store
        });

builder.Services
    .AddSingleton<AcDevicesService>()
    .AddSingleton<RingDevicesService>()
    .AddSingleton<ToshibaAcHttpService>()
    .AddSingleton<ToshibaAcMqttService>()
    .AddSingleton<ApplicationService>()
    .AddSingleton<OalHomeData>()
    .AddSingleton<LuxPowerTekService>();

builder.Services
    .AddApplicationInsightsTelemetry(options =>
    {
        options.ConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    _ = app.UseExceptionHandler("/Error")
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        .UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();

