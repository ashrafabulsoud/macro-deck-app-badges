using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Serilog;
using AppBadges;
using AppBadges.Badges;
using AppBadges.Platform;
using AppBadges.Widget;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Identity, description and icon are not set here: they come from manifest.json at the content root.
// Strings is generated from Localization/*.resx, so UseLocalization is what makes every LocalizedString
// below resolve in the user's language rather than falling back to its key.
var builder = MacroDeckPlugin.CreatePlugin(args);
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ICommandRunner, ProcessCommandRunner>();
builder.Services.AddSingleton<IBadgeReader, MacBadgeReader>();
builder.Services.AddSingleton<IAppCatalog, MacAppCatalog>(provider => new MacAppCatalog(
	provider.GetRequiredService<ICommandRunner>(), provider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IAppLauncher, MacAppLauncher>();
builder.Services.AddSingleton<BadgeMonitor>();
builder.Services.AddSingleton<IconResources>();

var plugin = builder
	.UseMacroDeckLogging()
	.UseLocalization(Strings.LocalizationCatalog)
	.RegisterIntegration<PluginIntegration>()
	.Build();

await plugin.RunAsync();
