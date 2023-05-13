namespace AcControl.Server.Services;

using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;

public class ApplicationService
{
    public async Task UpdateConfiguration(IDictionary<string, string> values)
    {
        var client = new ArmClient(new DefaultAzureCredential());
        var defaultSubscription = await client.GetDefaultSubscriptionAsync();
        
        // Note if things aren't found then it just throws an exception, because of course it does..

        var acGroup = (await defaultSubscription.GetResourceGroupAsync("AC")).Value;
        var acSite = (await acGroup.GetWebSiteAsync("AcControlServer")).Value;
        var settings = (await acSite.GetApplicationSettingsAsync()).Value;

        foreach ((var key, var value) in values)
        {
            settings.Properties[key] = value;
        }

        _ = await acSite.UpdateApplicationSettingsAsync(settings);
    }
}
