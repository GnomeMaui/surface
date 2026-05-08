using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace GnomeSurface.NuGetManager;

/// <summary>
/// Replaces the default <see cref="HttpHandlerResourceV3Provider"/> to inject
/// <see cref="PackageTypeFixHandler"/> into the HTTP pipeline.
/// </summary>
sealed class PackageTypeFixHttpHandlerProvider : ResourceProvider
{
	readonly HttpHandlerResourceV3Provider _inner = new();

	public PackageTypeFixHttpHandlerProvider()
		: base(
			typeof(HttpHandlerResource),
			nameof(PackageTypeFixHttpHandlerProvider),
			NuGetResourceProviderPositions.Last)
	{ }

	public override async Task<Tuple<bool, INuGetResource?>> TryCreate(SourceRepository source, CancellationToken token)
	{
		if (!source.PackageSource.IsEnabled)
			return Tuple.Create<bool, INuGetResource?>(false, null);

		var result = await _inner.TryCreate(source, token);

		if (!result.Item1 || result.Item2 is not HttpHandlerResourceV3 original)
			return result;

		var wrapped = new PackageTypeFixHandler(original.MessageHandler);
		var resource = new HttpHandlerResourceV3(original.ClientHandler, wrapped);
		return Tuple.Create<bool, INuGetResource?>(true, resource);
	}
}
