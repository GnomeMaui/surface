using System.Web;

namespace GnomeSurface.NuGetManager;

/// <summary>
/// Rewrites outgoing NuGet search requests: replaces the incorrect
/// "packageTypeFilter=" query parameter with the spec-correct "packageType=".
/// </summary>
sealed class PackageTypeFixHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (request.RequestUri is { } uri && uri.Query.Contains("packageTypeFilter=", StringComparison.Ordinal))
		{
			var query = HttpUtility.ParseQueryString(uri.Query);
			var typeFilter = query["packageTypeFilter"];
			if (typeFilter is not null)
			{
				query.Remove("packageTypeFilter");
				query["packageType"] = typeFilter;

				var builder = new UriBuilder(uri) { Query = query.ToString() };
				request.RequestUri = builder.Uri;
				Console.WriteLine($"NuGet search URL (fixed): {request.RequestUri}");
			}
		}

		return base.SendAsync(request, cancellationToken);
	}
}
