using System.Runtime.CompilerServices;
using System.Text;
using FontInfoParser;
using GnomeSurface;
using GnomeSurface.NuGetManager;
using GnomeSurface.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using Yaml.Localization;

partial class Program
{
	internal const string ApplicationId = "net.gnomemaui.surface";

	static async Task Main(string[] args)
	{
		// Magic spell: Makes app work from anywhere.
		Directory.SetCurrentDirectory(AppContext.BaseDirectory);
		// Ensure that the I18N class is initialized to prevent trimming issues
		// and to ensure that the necessary resources are available at runtime.
		RuntimeHelpers.RunClassConstructor(typeof(I18N).TypeHandle);


		var repository = CreateRepository("https://api.nuget.org/v3/index.json");
		// Keresés
		var searchResource = await repository.GetResourceAsync<PackageSearchResource>();
		var filter = new SearchFilter(includePrerelease: false, SearchFilterType.IsAbsoluteLatestVersion)
		{
			PackageTypes = ["dotnettool"]
		};
		var results = await searchResource.SearchAsync(
			"gnome",
			filter,
			skip: 0, take: 10,
			NullLogger.Instance,
			CancellationToken.None);

		foreach (var r in results)
		{
			var sb = new StringBuilder();
			sb.AppendLine($"Package: {r.Identity.Id}");
			sb.AppendLine($"Version: {r.Identity.Version}");
			Console.WriteLine(sb.ToString());
		}

		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		Adw.Module.Initialize();
		Gio.Module.Initialize();

		var host = Host.CreateDefaultBuilder()
			.ConfigureServices((context, services) =>
			{
				services = services
				.AddLogging(logging =>
				{
					logging = logging
					.ClearProviders()
					.AddSimpleConsole(options =>
					{
						options.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
						options.SingleLine = true;
						options.TimestampFormat = "HH:mm:ss ";
					});
				})
				.AddYamlFileSystemLocalization()
				.AddSingleton<InstalledTab>()
				.AddSingleton<SearchTab>()
				.AddSingleton(provider => MainWindow.CreateWithProperties(
					provider.GetRequiredService<InstalledTab>(),
					provider.GetRequiredService<SearchTab>(),
					provider.GetRequiredService<IStringLocalizer<I18N>>()));
			})
			.Build();

		var services = host.Services;
		services.SetFontInfoParserLogger();

		var application = Adw.Application.New(ApplicationId, Gio.ApplicationFlags.FlagsNone);
		application.OnActivate += (sender, args) =>
		{
			var window = services.GetRequiredService<MainWindow>();
			window.SetApplication((Adw.Application)sender);
			window.Present();
		};
		application.Run(args);
	}

	static SourceRepository CreateRepository(string sourceUrl)
	{
		Console.WriteLine($"NuGet source URL: {sourceUrl}");
		var packageSource = new PackageSource(RewriteNuGetSourceUrl(sourceUrl));
		return Repository.CreateSource(CreateResourceProviders(), packageSource);
	}

	static string RewriteNuGetSourceUrl(string sourceUrl)
	{
		var builder = new UriBuilder(sourceUrl);
		return builder.Uri.AbsoluteUri;
	}

	static IEnumerable<Lazy<INuGetResourceProvider>> CreateResourceProviders()
	{
		foreach (var provider in Repository.Provider.GetCoreV3())
		{
			if (provider.Value is ServiceIndexResourceV3Provider)
			{
				yield return new Lazy<INuGetResourceProvider>(() => new LocalServiceIndexResourceV3Provider());
				continue;
			}

			if (provider.Value is HttpHandlerResourceV3Provider)
			{
				yield return new Lazy<INuGetResourceProvider>(() => new PackageTypeFixHttpHandlerProvider());
				continue;
			}

			yield return provider;
		}
	}
}
