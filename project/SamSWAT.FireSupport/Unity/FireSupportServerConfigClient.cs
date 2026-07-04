using BepInEx.Configuration;
using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Text;
using System.Threading;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class FireSupportServerConfigClient
{
	private static readonly HttpClient s_httpClient = CreateHttpClient();
	private static CancellationTokenSource s_refreshCts;
	private static bool s_initialized;
	private static bool s_suppressedByFikaClient;
	private static string s_hostPurchaseBaseUrl;
	private static int s_hostPurchaseRevision;

	public static void Initialize()
	{
		if (s_initialized)
		{
			return;
		}

		s_initialized = true;
		SubscribeSetting(PluginSettings.UseServerConfigUrl);
		SubscribeSetting(PluginSettings.ServerConfigUrl);
		SubscribeSetting(PluginSettings.ServerConfigAuthToken);
		SubscribeSetting(PluginSettings.RequireServerConfigInFika);
		SubscribeSetting(PluginSettings.ServerConfigRefreshSeconds);
		RestartRefresh("plugin init");
	}

	public static void OnRaidStarted()
	{
		RestartRefresh("raid started");
	}

	public static void OnRaidEnded()
	{
		StopRefresh();
	}

	public static void SetFikaClientHostAuthorityActive(bool active, string reason)
	{
		if (s_suppressedByFikaClient == active)
		{
			return;
		}

		s_suppressedByFikaClient = active;
		TscDiagnostics.LogPayment(
			$"TSC server URL config {(active ? "suppressed by Fika host authority" : "resumed after Fika host authority cleared")}: {reason}");
		if (active)
		{
			StopRefresh();
			ClearServerOverrides(notify: true);
			return;
		}

		RestartRefresh(reason);
	}

	public static void SetHostPurchaseEndpoint(string baseUrl, int revision)
	{
		s_hostPurchaseBaseUrl = baseUrl;
		s_hostPurchaseRevision = revision;
		TscDiagnostics.LogPayment($"TSC Fika host purchase endpoint received revision={revision} url={baseUrl}");
	}

	public static void ClearHostPurchaseEndpoint()
	{
		s_hostPurchaseBaseUrl = null;
		s_hostPurchaseRevision = 0;
	}

	public static string GetConfiguredServerConfigUrl()
	{
		return PluginSettings.UseServerConfigUrl?.Value == true
			? PluginSettings.ServerConfigUrl?.Value ?? string.Empty
			: string.Empty;
	}

	public static async UniTask<FireSupportPurchaseResponse> PurchaseAuthorizationAsync(
		ESupportType supportType,
		int clientKnownRevision)
	{
		var fallback = new FireSupportPurchaseResponse
		{
			Ok = false,
			Reason = "ServerConfigUnavailable",
			SupportType = supportType.ToString(),
			Cost = FireSupportPayment.GetActiveCost(supportType),
			PaymentSource = nameof(PaymentSource.StashRoubles),
			NewBalance = FireSupportPayment.GetEffectiveBalance(),
			AuthorizationGranted = false,
			ServerRevision = Math.Max(clientKnownRevision, s_hostPurchaseRevision)
		};

		if (!TryBuildEndpoint("purchase", out Uri endpoint))
		{
			FireSupportPlugin.LogSource.LogWarning("FireSupport purchase request skipped: no usable server config URL.");
			return fallback;
		}

		try
		{
			var body = new FireSupportPurchaseRequest
			{
				Action = "BuyAuthorization",
				SessionId = GetLocalProfileId(),
				ProfileId = GetLocalProfileId(),
				SupportType = supportType.ToString(),
				ClientKnownRevision = clientKnownRevision,
				Quantity = 1
			};
			using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
			{
				Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json")
			};
			AddAuthHeader(request);

			using HttpResponseMessage response = await s_httpClient.SendAsync(request);
			string responseBody = await response.Content.ReadAsStringAsync();
			FireSupportPurchaseResponse result = JsonConvert.DeserializeObject<FireSupportPurchaseResponse>(responseBody);
			if (result == null)
			{
				fallback.Reason = "InvalidServerResponse";
				return fallback;
			}

			if (!response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(result.Reason))
			{
				result.Reason = $"Http{(int)response.StatusCode}";
			}

			return result;
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"FireSupport purchase request failed: {endpoint}. {ex}");
			fallback.Reason = "RequestFailed";
			return fallback;
		}
	}

	public static UniTask<FireSupportPurchaseResponse> ConsumeAuthorizationAsync(
		ESupportType supportType,
		string requestId,
		int clientKnownRevision)
	{
		return SendAuthorizationActionAsync("ConsumeAuthorization", supportType, requestId, clientKnownRevision);
	}

	public static UniTask<FireSupportPurchaseResponse> RefundAuthorizationAsync(
		ESupportType supportType,
		string requestId,
		int clientKnownRevision)
	{
		return SendAuthorizationActionAsync("RefundAuthorization", supportType, requestId, clientKnownRevision);
	}

	public static UniTask<FireSupportPurchaseResponse> CommitAuthorizationAsync(
		ESupportType supportType,
		string requestId,
		int clientKnownRevision)
	{
		return SendAuthorizationActionAsync("CommitAuthorization", supportType, requestId, clientKnownRevision);
	}

	private static async UniTask<FireSupportPurchaseResponse> SendAuthorizationActionAsync(
		string action,
		ESupportType supportType,
		string requestId,
		int clientKnownRevision)
	{
		var fallback = new FireSupportPurchaseResponse
		{
			Ok = false,
			Reason = "ServerConfigUnavailable",
			SupportType = supportType.ToString(),
			RequestId = requestId,
			ServerRevision = Math.Max(clientKnownRevision, s_hostPurchaseRevision)
		};

		if (!TryBuildEndpoint("purchase", out Uri endpoint))
		{
			FireSupportPlugin.LogSource.LogWarning($"FireSupport authorization {action} skipped: no usable server config URL.");
			return fallback;
		}

		try
		{
			var body = new FireSupportPurchaseRequest
			{
				Action = action,
				SessionId = GetLocalProfileId(),
				ProfileId = GetLocalProfileId(),
				SupportType = supportType.ToString(),
				RequestId = requestId,
				ClientKnownRevision = clientKnownRevision,
				Quantity = 1
			};
			using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
			{
				Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json")
			};
			AddAuthHeader(request);

			using HttpResponseMessage response = await s_httpClient.SendAsync(request);
			string responseBody = await response.Content.ReadAsStringAsync();
			FireSupportPurchaseResponse result = JsonConvert.DeserializeObject<FireSupportPurchaseResponse>(responseBody);
			if (result == null)
			{
				fallback.Reason = "InvalidServerResponse";
				return fallback;
			}

			if (!response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(result.Reason))
			{
				result.Reason = $"Http{(int)response.StatusCode}";
			}

			return result;
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"FireSupport authorization {action} failed: {endpoint}. {ex}");
			fallback.Reason = "RequestFailed";
			return fallback;
		}
	}

	private static void SubscribeSetting<T>(ConfigEntry<T> entry)
	{
		if (entry != null)
		{
			entry.SettingChanged += OnServerConfigSettingChanged;
		}
	}

	private static void OnServerConfigSettingChanged(object sender, EventArgs args)
	{
		string key = sender is ConfigEntryBase entry
			? $"{entry.Definition.Section}/{entry.Definition.Key}"
			: "<unknown>";
		RestartRefresh($"setting changed {key}");
	}

	private static void RestartRefresh(string reason)
	{
		StopRefresh();
		if (!ShouldFetchLocalServerConfig())
		{
			if (!s_suppressedByFikaClient)
			{
				ClearServerOverrides(notify: true);
			}

			return;
		}

		s_refreshCts = new CancellationTokenSource();
		RefreshLoop(reason, s_refreshCts.Token).Forget();
	}

	private static void StopRefresh()
	{
		s_refreshCts?.Cancel();
		s_refreshCts?.Dispose();
		s_refreshCts = null;
	}

	private static async UniTaskVoid RefreshLoop(string reason, CancellationToken cancellationToken)
	{
		TscDiagnostics.LogPayment($"TSC server config refresh started: {reason}");
		while (!cancellationToken.IsCancellationRequested && ShouldFetchLocalServerConfig())
		{
			await FetchConfigOnce(cancellationToken);

			int seconds = Math.Max(0, PluginSettings.ServerConfigRefreshSeconds?.Value ?? 10);
			if (seconds <= 0)
			{
				break;
			}

			await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: cancellationToken);
		}
	}

	private static async UniTask FetchConfigOnce(CancellationToken cancellationToken)
	{
		if (!TryBuildEndpoint(BuildConfigRoute(), out Uri endpoint))
		{
			HandleConfigFailure("invalid server config URL");
			return;
		}

		try
		{
			TscDiagnostics.LogPayment($"TSC server config URL requested: {endpoint}");
			using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
			AddAuthHeader(request);
			using HttpResponseMessage response = await s_httpClient.SendAsync(request, cancellationToken);
			string body = await response.Content.ReadAsStringAsync();
			if (!response.IsSuccessStatusCode)
			{
				HandleConfigFailure($"HTTP {(int)response.StatusCode}");
				return;
			}

			RaidOpsFireSupportServerConfig snapshot = JsonConvert.DeserializeObject<RaidOpsFireSupportServerConfig>(body);
			if (snapshot == null)
			{
				HandleConfigFailure("empty or invalid JSON snapshot");
				return;
			}

			ApplySnapshot(snapshot);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			HandleConfigFailure(ex.Message);
		}
	}

	private static void ApplySnapshot(RaidOpsFireSupportServerConfig snapshot)
	{
		int revision = Math.Max(0, snapshot.Revision);
		FireSupportPayment.SetServerConfigCosts(
			GetPrice(snapshot, "A10", ESupportType.Strafe),
			GetPrice(snapshot, "DoublePass", ESupportType.DoubleStrafe),
			GetPrice(snapshot, "Extraction", ESupportType.Extract),
			GetPrice(snapshot, "PriorityExfil", ESupportType.PriorityExfil),
			GetPrice(snapshot, "Uav", ESupportType.Uav),
			GetPrice(snapshot, "FocusedSweep", ESupportType.FocusedSweep),
			revision);
		FireSupportPayment.SetServerConfigPayment(
			ParseEnum(snapshot.PaymentMode, FireSupportPayment.GetConfiguredPaymentMode()),
			ParseEnum(snapshot.PaymentSource, FireSupportPayment.GetConfiguredPaymentSource()),
			revision,
			snapshot.StashRoubleBalance);
		FireSupportPayment.SetServerPurchasePersistence(
			snapshot.PurchasePersistence?.Enabled == true,
			snapshot.PurchasePersistence?.RefundFailedDispatch != false,
			snapshot.PurchasePersistence?.SpendCreditsBeforeCash != false,
			snapshot.PurchasePersistence?.AllowAutoPurchaseOnUse == true,
			revision);
		if (snapshot.Authorizations != null)
		{
			FireSupportAuthorizations.SetFromServer(snapshot.Authorizations);
		}
		FireSupportServiceAvailability.SetServerConfigAvailability(
			GetEnabled(snapshot, "PriorityExfil", FireSupportServiceAvailability.GetConfiguredPriorityExfilEnabled()),
			GetEnabled(snapshot, "DoublePass", FireSupportServiceAvailability.GetConfiguredDoublePassEnabled()),
			GetEnabled(snapshot, "FocusedSweep", FireSupportServiceAvailability.GetConfiguredFocusedSweepEnabled()),
			revision);
		FireSupportTuningSettings.SetServerConfigTuning(
			(snapshot.DoublePass ?? new RaidOpsFireSupportServerConfig.A10Settings()).SecondPassDelaySeconds,
			(snapshot.Extraction ?? new RaidOpsFireSupportServerConfig.ExtractionSettings()).WaitTimeSeconds,
			(snapshot.PriorityExfil ?? new RaidOpsFireSupportServerConfig.ExtractionSettings()).WaitTimeSeconds,
			(snapshot.PriorityExfil ?? new RaidOpsFireSupportServerConfig.ExtractionSettings()).DispatchDelaySeconds,
			(snapshot.Extraction ?? new RaidOpsFireSupportServerConfig.ExtractionSettings()).ExtractTimeSeconds,
			(snapshot.Extraction ?? new RaidOpsFireSupportServerConfig.ExtractionSettings()).SpeedMultiplier,
			(snapshot.PriorityExfil ?? new RaidOpsFireSupportServerConfig.ExtractionSettings()).SpeedMultiplier,
			snapshot.RequestCooldownSeconds,
			revision);
		FireSupportServerConfigClient.ApplyUavSettings(snapshot, revision);
		FireSupportPayment.NotifySettingsChanged(snapshot);
		TscDiagnostics.LogPayment(
			$"TSC server config loaded revision={revision} paymentSource={snapshot.PaymentSource} stashBalance={(snapshot.StashRoubleBalance.HasValue ? snapshot.StashRoubleBalance.Value.ToString() : "unknown")}");
	}

	private static void ApplyUavSettings(RaidOpsFireSupportServerConfig snapshot, int revision)
	{
		RaidOpsFireSupportServerConfig.UavSettings uav = snapshot.Uav ?? new RaidOpsFireSupportServerConfig.UavSettings();
		RaidOpsFireSupportServerConfig.UavSettings focusedSweep = snapshot.FocusedSweep ?? new RaidOpsFireSupportServerConfig.UavSettings();
		UavReconSettings.SetServerConfigDuration(
			uav.DurationSeconds,
			uav.ScanIntervalSeconds,
			uav.RangeMeters,
			revision);
		UavReconSettings.SetServerConfigFocusedSweep(
			focusedSweep.DurationSeconds,
			focusedSweep.ScanIntervalSeconds,
			focusedSweep.RangeMeters,
			revision);
	}
	private static void HandleConfigFailure(string reason)
	{
		FireSupportPlugin.LogSource.LogWarning($"TSC server config failed: {reason}");
		FireSupportPayment.MarkServerConfigUnavailable(reason);
		if (!ShouldRequireServerConfig())
		{
			ClearServerOverrides(notify: true);
		}
		else
		{
			FireSupportPayment.NotifySettingsChanged(reason);
		}
	}

	private static void ClearServerOverrides(bool notify)
	{
		FireSupportPayment.ClearServerConfig();
		FireSupportServiceAvailability.ClearServerConfigAvailability();
		FireSupportTuningSettings.ClearServerConfigTuning();
		UavReconSettings.ClearServerConfigDuration();
		if (notify)
		{
			FireSupportPayment.NotifySettingsChanged("TSC server URL config cleared");
		}
	}

	private static int GetPrice(
		RaidOpsFireSupportServerConfig snapshot,
		string key,
		ESupportType supportType)
	{
		if (snapshot.Prices != null &&
		    snapshot.Prices.TryGetValue(key, out int value))
		{
			return value;
		}

		return FireSupportPayment.GetConfiguredCost(supportType);
	}

	private static bool GetEnabled(
		RaidOpsFireSupportServerConfig snapshot,
		string key,
		bool fallback)
	{
		return snapshot.Enabled != null &&
		       snapshot.Enabled.TryGetValue(key, out bool value)
			? value
			: fallback;
	}

	private static TEnum ParseEnum<TEnum>(string value, TEnum fallback)
		where TEnum : struct
	{
		return Enum.TryParse(value, ignoreCase: true, out TEnum parsed)
			? parsed
			: fallback;
	}

	private static bool ShouldFetchLocalServerConfig()
	{
		return PluginSettings.UseServerConfigUrl?.Value == true && !s_suppressedByFikaClient;
	}

	private static bool ShouldRequireServerConfig()
	{
		return PluginSettings.UseServerConfigUrl?.Value == true &&
		       PluginSettings.RequireServerConfigInFika?.Value == true;
	}

	private static bool TryBuildEndpoint(string route, out Uri endpoint)
	{
		endpoint = null;

		// s_hostPurchaseBaseUrl is only ever set on a Fika client (from the host
		// settings packet). A host running default config broadcasts its own
		// loopback URL (127.0.0.1), which is meaningless to a remote client and
		// used to override the client's own correctly-configured host address.
		// Ignore a loopback host broadcast so the client's own ServerConfigUrl
		// (which the player points at the host's reachable IP) takes effect.
		string baseUrl =
			!string.IsNullOrWhiteSpace(s_hostPurchaseBaseUrl) && !IsLoopbackUrl(s_hostPurchaseBaseUrl)
				? s_hostPurchaseBaseUrl
				: PluginSettings.ServerConfigUrl?.Value;
		if (string.IsNullOrWhiteSpace(baseUrl) ||
		    !Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out Uri baseUri) ||
		    (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
		{
			return false;
		}

		endpoint = new Uri(baseUri, route);
		return true;
	}

	private static bool IsLoopbackUrl(string url)
	{
		if (!Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out Uri uri))
		{
			return false;
		}

		if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return IPAddress.TryParse(uri.Host, out IPAddress ip) && IPAddress.IsLoopback(ip);
	}

	private static string BuildConfigRoute()
	{
		string profileId = GetLocalProfileId();
		if (string.IsNullOrWhiteSpace(profileId))
		{
			return "config";
		}

		string encodedProfileId = Uri.EscapeDataString(profileId.Trim());
		return $"config?profileId={encodedProfileId}&sessionId={encodedProfileId}";
	}

	private static HttpClient CreateHttpClient()
	{
		var handler = new HttpClientHandler
		{
			ServerCertificateCustomValidationCallback = ShouldAcceptSptServerCertificate
		};

		return new HttpClient(handler);
	}

	private static bool ShouldAcceptSptServerCertificate(
		HttpRequestMessage request,
		System.Security.Cryptography.X509Certificates.X509Certificate2 certificate,
		System.Security.Cryptography.X509Certificates.X509Chain chain,
		SslPolicyErrors errors)
	{
		if (errors == SslPolicyErrors.None)
		{
			return true;
		}

		Uri uri = request?.RequestUri;
		if (uri == null || uri.Scheme != Uri.UriSchemeHttps)
		{
			return false;
		}

		string host = uri.Host;
		return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
		       IPAddress.TryParse(host, out _);
	}

	private static void AddAuthHeader(HttpRequestMessage request)
	{
		string token = PluginSettings.ServerConfigAuthToken?.Value;
		if (!string.IsNullOrWhiteSpace(token))
		{
			request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Trim());
			request.Headers.TryAddWithoutValidation("X-TSC-Token", token.Trim());
			request.Headers.TryAddWithoutValidation("X-RaidOps-FireSupport-Token", token.Trim());
		}
	}

	private static string GetLocalProfileId()
	{
		// In raid the main player carries the active profile. Outside raid fall
		// back to the backend session profile so config polls still identify the
		// player; without an id the server cannot include the stash balance or
		// ledger credits in its response, and the phone showed carried-only
		// balances until the first in-raid sync completed.
		string raidProfileId = Singleton<GameWorld>.Instance?.MainPlayer?.ProfileId;
		if (!string.IsNullOrWhiteSpace(raidProfileId))
		{
			return raidProfileId;
		}

		try
		{
			EFT.Profile sessionProfile = SPT.Reflection.Utils.PatchConstants.BackEndSession?.Profile;
			return sessionProfile != null ? sessionProfile.Id : string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}
}
