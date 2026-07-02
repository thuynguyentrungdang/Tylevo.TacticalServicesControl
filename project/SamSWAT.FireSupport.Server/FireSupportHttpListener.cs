using Microsoft.AspNetCore.Http;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers.Http;
using System.Net;
using System.Text;
using System.Text.Json;

namespace SamSWAT.FireSupport.ArysReloaded;

[Injectable(TypePriority = 0)]
public sealed class FireSupportHttpListener(FireSupportServerConfigService configService) : IHttpListener
{
	private const string PublicRoot = "/tsc";
	private const string LegacyRoot = "/raidops/firesupport";

	private static readonly Dictionary<string, string> s_adminAssetContentTypes = new(StringComparer.OrdinalIgnoreCase)
	{
		["index.html"] = "text/html; charset=utf-8",
		["app.mjs"] = "application/javascript; charset=utf-8",
		["styles.css"] = "text/css; charset=utf-8"
	};

	private static readonly JsonSerializerOptions s_jsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = false
	};

	public bool CanHandle(MongoId sessionId, HttpContext httpContext)
	{
		string? path = httpContext.Request.Path.Value;
		return IsRouteRoot(path, PublicRoot) || IsRouteRoot(path, LegacyRoot);
	}

	public async Task Handle(MongoId sessionId, HttpContext httpContext)
	{
		string path = NormalizeRoutePath(httpContext.Request.Path.Value);
		string method = httpContext.Request.Method;

		if (string.Equals(method, HttpMethods.Get, StringComparison.OrdinalIgnoreCase) &&
		    string.Equals(path, "/tsc/health", StringComparison.OrdinalIgnoreCase))
		{
			await WriteJsonAsync(httpContext, 200, configService.GetHealth(includeDiagnostics: false, IsLocalRequest(httpContext)));
			return;
		}

		if (string.Equals(method, HttpMethods.Get, StringComparison.OrdinalIgnoreCase) &&
		    string.Equals(path, "/tsc/admin/health", StringComparison.OrdinalIgnoreCase))
		{
			await HandleAdminHealthAsync(httpContext);
			return;
		}

		if (string.Equals(method, HttpMethods.Get, StringComparison.OrdinalIgnoreCase) &&
		    (string.Equals(path, "/tsc/admin", StringComparison.OrdinalIgnoreCase) ||
		     path.StartsWith("/tsc/admin/", StringComparison.OrdinalIgnoreCase)))
		{
			await HandleAdminAssetAsync(path, httpContext);
			return;
		}

		if (string.Equals(method, HttpMethods.Get, StringComparison.OrdinalIgnoreCase) &&
		    (string.Equals(path, "/tsc", StringComparison.OrdinalIgnoreCase) ||
		     string.Equals(path, "/tsc/config", StringComparison.OrdinalIgnoreCase)))
		{
			// A browser opening /tsc wants the dashboard, not the config snapshot
			// the game client polls. The client never sends an html Accept header,
			// so redirecting browsers does not affect config polling.
			if (string.Equals(path, "/tsc", StringComparison.OrdinalIgnoreCase) &&
			    httpContext.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
			{
				httpContext.Response.Redirect("/tsc/admin");
				return;
			}

			await WriteJsonAsync(httpContext, 200, configService.GetSnapshot(sessionId, ReadIdentityFromQuery(httpContext)));
			return;
		}

		if (string.Equals(method, HttpMethods.Get, StringComparison.OrdinalIgnoreCase) &&
		    string.Equals(path, "/tsc/schema", StringComparison.OrdinalIgnoreCase))
		{
			await WriteJsonAsync(httpContext, 200, configService.GetDashboardSchema());
			return;
		}

		if (string.Equals(method, HttpMethods.Post, StringComparison.OrdinalIgnoreCase) &&
		    string.Equals(path, "/tsc/purchase", StringComparison.OrdinalIgnoreCase))
		{
			await HandlePurchaseAsync(sessionId, httpContext);
			return;
		}

		if (string.Equals(method, HttpMethods.Post, StringComparison.OrdinalIgnoreCase) &&
		    (string.Equals(path, "/tsc", StringComparison.OrdinalIgnoreCase) ||
		     string.Equals(path, "/tsc/config", StringComparison.OrdinalIgnoreCase)))
		{
			await HandleConfigUpdateAsync(httpContext);
			return;
		}

		if (string.Equals(method, HttpMethods.Post, StringComparison.OrdinalIgnoreCase) &&
		    string.Equals(path, "/tsc/reload", StringComparison.OrdinalIgnoreCase))
		{
			await HandleReloadAsync(httpContext);
			return;
		}

		if (string.Equals(method, HttpMethods.Post, StringComparison.OrdinalIgnoreCase) &&
		    string.Equals(path, "/tsc/reset", StringComparison.OrdinalIgnoreCase))
		{
			await HandleResetAsync(httpContext);
			return;
		}

		httpContext.Response.Headers.Allow = "GET, POST";
		await WriteJsonAsync(httpContext, 404, new { error = "Unknown TSC route." });
	}

	private async Task HandleAdminAssetAsync(string path, HttpContext httpContext)
	{
		if (!configService.IsAdminDashboardAccessible(IsLocalRequest(httpContext), out string denialReason))
		{
			await WriteJsonAsync(httpContext, 403, new { error = denialReason });
			return;
		}

		if (!TryGetAdminAsset(path, out string fileName, out string contentType) ||
		    !TryResolveAdminAssetPath(fileName, out string filePath))
		{
			// Name the expected folder so a missing/partial install is
			// self-diagnosable from the browser (this route is local-only by
			// default, so the path disclosure stays on the admin's machine).
			await WriteJsonAsync(httpContext, 404, new
			{
				error = "TSC dashboard asset not found.",
				hint = $"Expected dashboard files (index.html, app.mjs, styles.css) in: {configService.WebRootPath}. Re-extract the TSC release zip with the server stopped if the folder is missing."
			});
			return;
		}

		byte[] body = await File.ReadAllBytesAsync(filePath, httpContext.RequestAborted);
		httpContext.Response.StatusCode = 200;
		httpContext.Response.ContentType = contentType;
		httpContext.Response.ContentLength = body.Length;
		httpContext.Response.Headers.CacheControl = "no-store";
		await httpContext.Response.StartAsync(httpContext.RequestAborted);
		await httpContext.Response.Body.WriteAsync(body.AsMemory(0, body.Length), httpContext.RequestAborted);
		await httpContext.Response.CompleteAsync();
	}

	private async Task HandleAdminHealthAsync(HttpContext httpContext)
	{
		if (!IsAdminRequestAuthorized(httpContext))
		{
			await WriteJsonAsync(httpContext, 403, new { error = "TSC admin diagnostics require an admin token." });
			return;
		}

		await WriteJsonAsync(httpContext, 200, configService.GetHealth(includeDiagnostics: true, IsLocalRequest(httpContext)));
	}

	private async Task HandlePurchaseAsync(MongoId sessionId, HttpContext httpContext)
	{
		FireSupportPurchaseRequest? request = await ReadJsonAsync<FireSupportPurchaseRequest>(httpContext);
		if (request == null)
		{
			await WriteJsonAsync(httpContext, 400, new FireSupportPurchaseResponse
			{
				Ok = false,
				Reason = "InvalidRequest"
			});
			return;
		}

		FireSupportPurchaseResponse response;
		if (string.Equals(request.Action, "ConsumeAuthorization", StringComparison.OrdinalIgnoreCase))
		{
			response = configService.TryConsumeAuthorization(sessionId, request);
		}
		else if (string.Equals(request.Action, "CommitAuthorization", StringComparison.OrdinalIgnoreCase))
		{
			response = configService.TryCommitAuthorization(sessionId, request);
		}
		else if (string.Equals(request.Action, "RefundAuthorization", StringComparison.OrdinalIgnoreCase))
		{
			response = configService.TryRefundAuthorization(sessionId, request);
		}
		else
		{
			response = await configService.TryPurchaseAsync(sessionId, request);
		}

		await WriteJsonAsync(httpContext, 200, response);
	}

	private async Task HandleConfigUpdateAsync(HttpContext httpContext)
	{
		if (!IsAdminRequestAuthorized(httpContext))
		{
			await WriteJsonAsync(httpContext, 403, new { error = "TSC config updates require an admin token." });
			return;
		}

		RaidOpsFireSupportServerConfig? request = await ReadJsonAsync<RaidOpsFireSupportServerConfig>(httpContext);
		if (request == null)
		{
			await WriteJsonAsync(httpContext, 400, new { error = "Invalid TSC config JSON." });
			return;
		}

		if (!configService.TryUpdateConfig(request, out string error))
		{
			await WriteJsonAsync(httpContext, 400, new { error });
			return;
		}

		await WriteJsonAsync(httpContext, 200, configService.GetSnapshot(default, null));
	}

	private async Task HandleReloadAsync(HttpContext httpContext)
	{
		if (!IsAdminRequestAuthorized(httpContext))
		{
			await WriteJsonAsync(httpContext, 403, new { error = "TSC config reload requires an admin token." });
			return;
		}

		if (!configService.TryReloadConfig(out RaidOpsFireSupportServerConfig snapshot, out string error))
		{
			await WriteJsonAsync(httpContext, 400, new { error });
			return;
		}

		await WriteJsonAsync(httpContext, 200, snapshot);
	}

	private async Task HandleResetAsync(HttpContext httpContext)
	{
		if (!IsAdminRequestAuthorized(httpContext))
		{
			await WriteJsonAsync(httpContext, 403, new { error = "TSC config reset requires an admin token." });
			return;
		}

		if (!configService.TryResetConfig(out RaidOpsFireSupportServerConfig snapshot, out string error))
		{
			await WriteJsonAsync(httpContext, 400, new { error });
			return;
		}

		await WriteJsonAsync(httpContext, 200, snapshot);
	}

	private static FireSupportPurchaseRequest? ReadIdentityFromQuery(HttpContext httpContext)
	{
		var request = new FireSupportPurchaseRequest();
		if (httpContext.Request.Query.TryGetValue("profileId", out var profileId))
		{
			request.ProfileId = profileId.FirstOrDefault() ?? string.Empty;
		}

		if (httpContext.Request.Query.TryGetValue("sessionId", out var sessionId))
		{
			request.SessionId = sessionId.FirstOrDefault() ?? string.Empty;
		}

		return string.IsNullOrWhiteSpace(request.ProfileId) &&
		       string.IsNullOrWhiteSpace(request.SessionId)
			? null
			: request;
	}

	private bool IsAdminRequestAuthorized(HttpContext httpContext)
	{
		string? authHeader = httpContext.Request.Headers.Authorization.FirstOrDefault();
		string? tokenHeader = httpContext.Request.Headers["X-TSC-Admin-Token"].FirstOrDefault() ??
		                      httpContext.Request.Headers["X-RaidOps-FireSupport-Admin-Token"].FirstOrDefault();
		return configService.IsAdminRequestAuthorized(authHeader, tokenHeader, IsLocalRequest(httpContext));
	}

	private static bool IsRouteRoot(string? path, string root)
	{
		return string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
		       path?.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase) == true;
	}

	private static string NormalizeRoutePath(string? path)
	{
		string normalized = path?.TrimEnd('/') ?? string.Empty;
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return string.Empty;
		}

		if (string.Equals(normalized, LegacyRoot, StringComparison.OrdinalIgnoreCase))
		{
			return PublicRoot;
		}

		if (normalized.StartsWith(LegacyRoot + "/", StringComparison.OrdinalIgnoreCase))
		{
			return PublicRoot + normalized[LegacyRoot.Length..];
		}

		return string.IsNullOrEmpty(normalized) ? PublicRoot : normalized;
	}

	private static bool IsLocalRequest(HttpContext httpContext)
	{
		IPAddress? remoteAddress = httpContext.Connection.RemoteIpAddress;
		return remoteAddress == null ||
		       IPAddress.IsLoopback(remoteAddress) ||
		       remoteAddress.Equals(httpContext.Connection.LocalIpAddress);
	}

	private static bool TryGetAdminAsset(string path, out string fileName, out string contentType)
	{
		const string adminRoot = "/tsc/admin";
		fileName = string.Empty;
		contentType = string.Empty;

		if (!path.StartsWith(adminRoot, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		string relativePath = path[adminRoot.Length..].Trim('/');
		if (string.IsNullOrWhiteSpace(relativePath))
		{
			relativePath = "index.html";
		}

		if (relativePath.Contains('/') ||
		    relativePath.Contains('\\') ||
		    relativePath.Contains("..", StringComparison.Ordinal))
		{
			return false;
		}

		if (!s_adminAssetContentTypes.TryGetValue(relativePath, out string? mappedContentType))
		{
			return false;
		}

		fileName = relativePath;
		contentType = mappedContentType;
		return true;
	}

	private bool TryResolveAdminAssetPath(string fileName, out string filePath)
	{
		filePath = string.Empty;
		if (string.IsNullOrWhiteSpace(configService.WebRootPath))
		{
			return false;
		}

		string webRoot = Path.GetFullPath(configService.WebRootPath);
		string resolvedPath = Path.GetFullPath(Path.Combine(webRoot, fileName));
		string webRootPrefix = webRoot.EndsWith(Path.DirectorySeparatorChar)
			? webRoot
			: webRoot + Path.DirectorySeparatorChar;
		if (!resolvedPath.StartsWith(webRootPrefix, StringComparison.OrdinalIgnoreCase) ||
		    !File.Exists(resolvedPath))
		{
			return false;
		}

		filePath = resolvedPath;
		return true;
	}

	private static async Task<T?> ReadJsonAsync<T>(HttpContext httpContext)
	{
		using var reader = new StreamReader(
			httpContext.Request.Body,
			Encoding.UTF8,
			detectEncodingFromByteOrderMarks: false,
			bufferSize: 1024,
			leaveOpen: true);

		string body = await reader.ReadToEndAsync(httpContext.RequestAborted);
		if (string.IsNullOrWhiteSpace(body))
		{
			return default;
		}

		return JsonSerializer.Deserialize<T>(body, s_jsonOptions);
	}

	private static async Task WriteJsonAsync(HttpContext httpContext, int statusCode, object value)
	{
		byte[] body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, s_jsonOptions));
		httpContext.Response.StatusCode = statusCode;
		httpContext.Response.ContentType = "application/json; charset=utf-8";
		httpContext.Response.ContentLength = body.Length;

		await httpContext.Response.StartAsync(httpContext.RequestAborted);
		await httpContext.Response.Body.WriteAsync(body.AsMemory(0, body.Length), httpContext.RequestAborted);
		await httpContext.Response.CompleteAsync();
	}
}
