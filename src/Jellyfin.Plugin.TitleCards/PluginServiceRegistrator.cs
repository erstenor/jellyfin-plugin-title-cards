using Jellyfin.Plugin.TitleCards.Cards;
using Jellyfin.Plugin.TitleCards.Preferences;
using Jellyfin.Plugin.TitleCards.Variants;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.TitleCards;

/// <summary>
/// Registers the plugin's services with the server.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<CandidateSource>();
        serviceCollection.AddSingleton<TitleCardService>();
        serviceCollection.AddHostedService<ChangeMonitor>();
        serviceCollection.AddSingleton<VariantImageStore>();
        serviceCollection.AddSingleton<UserPreferenceStore>();
        serviceCollection.AddSingleton<VariantImageFilter>();
        serviceCollection.Configure<MvcOptions>(options => options.Filters.AddService<VariantImageFilter>());
    }
}
