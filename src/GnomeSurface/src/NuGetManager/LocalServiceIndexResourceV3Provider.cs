using Newtonsoft.Json.Linq;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace GnomeSurface.NuGetManager;

sealed class LocalServiceIndexResourceV3Provider : ResourceProvider
{
	public LocalServiceIndexResourceV3Provider()
		: base(typeof(ServiceIndexResourceV3), nameof(LocalServiceIndexResourceV3Provider))
	{
	}

	public override async Task<Tuple<bool, INuGetResource?>> TryCreate(SourceRepository source, CancellationToken token)
	{
		if (!source.PackageSource.IsEnabled)
			return Tuple.Create<bool, INuGetResource?>(false, null);

		var fallback = new ServiceIndexResourceV3Provider();
		var result = await fallback.TryCreate(source, token);
		Console.WriteLine($"Service index resource provider: {result.Item1}, {result.Item2?.GetType().Name}");
		if (!result.Item1 || result.Item2 is not ServiceIndexResourceV3 resource)
		{
			Console.WriteLine("Remote NuGet service index could not be loaded.");
			return result;
		}

		Console.WriteLine($"Loaded remote NuGet service index from: {source.PackageSource.Source}");

		var searchEndpoints = resource.GetServiceEntryUris(ServiceTypes.SearchQueryService);
		Console.WriteLine($"Search endpoints used for PackageSearchResource ({searchEndpoints.Count}):");
		foreach (var uri in searchEndpoints)
		{
			Console.WriteLine($"  {uri}");
		}

		return result;
	}
}
