using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class FireSupportPayment
{
	private readonly struct CostLogState(int cost, string source)
	{
		public readonly int Cost = cost;
		public readonly string Source = source;
	}

	private static int? _syncedStrafeCost;
	private static int? _syncedDoubleStrafeCost;
	private static int? _syncedExtractionCost;
	private static int? _syncedPriorityExfilCost;
	private static int? _syncedUavCost;
	private static int? _syncedFocusedSweepCost;
	private static PaymentMode? _syncedPaymentMode;
	private static PaymentSource? _syncedPaymentSource;
	private static int? _serverStrafeCost;
	private static int? _serverDoubleStrafeCost;
	private static int? _serverExtractionCost;
	private static int? _serverPriorityExfilCost;
	private static int? _serverUavCost;
	private static int? _serverFocusedSweepCost;
	private static PaymentMode? _serverPaymentMode;
	private static PaymentSource? _serverPaymentSource;
	private static int? _serverStashRoubleBalance;
	private static int _serverConfigRevision;
	private static bool _serverConfigUnavailable;
	private static string _serverConfigUnavailableReason;
	private static bool _serverPurchasePersistenceEnabled;
	private static bool _serverRefundFailedDispatch = true;
	private static bool _serverSpendCreditsBeforeCash = true;
	private static bool _serverAllowAutoPurchaseOnUse = true;
	private static FireSupportPurchaseResponse _lastPurchaseDenial;
	private static readonly Dictionary<ESupportType, CostLogState> s_lastLoggedCost = new(new SupportTypeComparer());

	public static event EventHandler SettingsChanged;

	public static bool HasSyncedCosts =>
		_syncedStrafeCost.HasValue ||
		_syncedDoubleStrafeCost.HasValue ||
		_syncedExtractionCost.HasValue ||
		_syncedPriorityExfilCost.HasValue ||
		_syncedUavCost.HasValue ||
		_syncedFocusedSweepCost.HasValue;

	public static bool HasServerConfigCosts =>
		_serverStrafeCost.HasValue ||
		_serverDoubleStrafeCost.HasValue ||
		_serverExtractionCost.HasValue ||
		_serverPriorityExfilCost.HasValue ||
		_serverUavCost.HasValue ||
		_serverFocusedSweepCost.HasValue;

	public static int ServerConfigRevision => _serverConfigRevision;

	public static string GetLastPurchaseDenialTitle(ESupportType supportType)
	{
		FireSupportPurchaseResponse denial = GetLastPurchaseDenial(supportType);
		return denial?.Reason switch
		{
			"AuthorizationLimitReached" => "AUTHORIZATION LIMIT REACHED",
			"InsufficientRoubles" => "INSUFFICIENT FUNDS",
			"RateLimited" => "PURCHASE ALREADY PROCESSING",
			"ServerConfigUnavailable" or "RequestFailed" or "InvalidServerResponse" => "SERVER PAYMENT UNAVAILABLE",
			"ProfileNotFound" or "ProfileSessionMismatch" => "PROFILE VERIFY FAILED",
			"ServiceUnavailable" => "SERVICE UNAVAILABLE",
			"PaymentSourceNotServerBacked" => "SERVER PAYMENT DISABLED",
			"ProfileSaveFailed" => "PROFILE SAVE FAILED",
			_ => "AUTHORIZATION DENIED"
		};
	}

	public static string GetLastPurchaseDenialDetail(ESupportType supportType)
	{
		FireSupportPurchaseResponse denial = GetLastPurchaseDenial(supportType);
		if (denial == null)
		{
			return "No authorization was granted.";
		}

		switch (denial.Reason)
		{
			case "AuthorizationLimitReached":
				int held = GetAuthorizationCount(denial, supportType);
				return held > 0
					? $"{held} held. Use one from YY before buying more."
					: "Use an existing authorization from YY before buying more.";
			case "InsufficientRoubles":
				return $"{GetEffectiveBalanceLabel()}: {FormatRoubles(Math.Max(denial.NewBalance, GetEffectiveBalance()))}.";
			case "RateLimited":
				return "Wait a moment, then try the purchase again.";
			case "ServerConfigUnavailable":
			case "RequestFailed":
			case "InvalidServerResponse":
				return "Check the TSC server and dashboard connection.";
			case "ProfileNotFound":
			case "ProfileSessionMismatch":
				return "The server could not verify the active profile.";
			case "ServiceUnavailable":
				return $"{GetSupportName(supportType)} is disabled in host settings.";
			case "PaymentSourceNotServerBacked":
				return "Use stash-backed payment or carried roubles.";
			case "ProfileSaveFailed":
				return "The debit could not be saved to the profile.";
			default:
				return string.IsNullOrWhiteSpace(denial.Reason)
					? "No authorization was granted."
					: $"Server reason: {denial.Reason}.";
		}
	}

	public static void SetSyncedCosts(
		int strafeCost,
		int doubleStrafeCost,
		int extractionCost,
		int priorityExfilCost,
		int uavCost,
		int focusedSweepCost)
	{
		_syncedStrafeCost = strafeCost;
		_syncedDoubleStrafeCost = doubleStrafeCost;
		_syncedExtractionCost = extractionCost;
		_syncedPriorityExfilCost = priorityExfilCost;
		_syncedUavCost = uavCost;
		_syncedFocusedSweepCost = focusedSweepCost;
		TscDiagnostics.LogPayment(
			$"Using host TSC prices: A-10={FormatRoubles(strafeCost)}, A-10 double pass={FormatRoubles(doubleStrafeCost)}, UH-60={FormatRoubles(extractionCost)}, Priority exfil={FormatRoubles(priorityExfilCost)}, UAV={FormatRoubles(uavCost)}, Focused sweep={FormatRoubles(focusedSweepCost)}");
	}

	public static void ClearSyncedCosts()
	{
		bool hadSyncedSettings = HasSyncedCosts || _syncedPaymentMode.HasValue || _syncedPaymentSource.HasValue;
		_syncedStrafeCost = null;
		_syncedDoubleStrafeCost = null;
		_syncedExtractionCost = null;
		_syncedPriorityExfilCost = null;
		_syncedUavCost = null;
		_syncedFocusedSweepCost = null;
		_syncedPaymentMode = null;
		_syncedPaymentSource = null;
		if (hadSyncedSettings)
		{
			TscDiagnostics.LogPayment("Cleared host TSC prices, payment mode, and payment source.");
		}
	}

	public static void SetServerConfigCosts(
		int strafeCost,
		int doubleStrafeCost,
		int extractionCost,
		int priorityExfilCost,
		int uavCost,
		int focusedSweepCost,
		int revision)
	{
		_serverStrafeCost = strafeCost;
		_serverDoubleStrafeCost = doubleStrafeCost;
		_serverExtractionCost = extractionCost;
		_serverPriorityExfilCost = priorityExfilCost;
		_serverUavCost = uavCost;
		_serverFocusedSweepCost = focusedSweepCost;
		_serverConfigRevision = revision;
		_serverConfigUnavailable = false;
		_serverConfigUnavailableReason = null;
		TscDiagnostics.LogPayment(
			$"Using server URL TSC prices revision={revision}: A-10={FormatRoubles(strafeCost)}, A-10 double pass={FormatRoubles(doubleStrafeCost)}, UH-60={FormatRoubles(extractionCost)}, Priority exfil={FormatRoubles(priorityExfilCost)}, UAV={FormatRoubles(uavCost)}, Focused sweep={FormatRoubles(focusedSweepCost)}");
	}

	public static void ClearServerConfig()
	{
		bool hadServerSettings = HasServerConfigCosts ||
		                         _serverPaymentMode.HasValue ||
		                         _serverPaymentSource.HasValue ||
		                         _serverStashRoubleBalance.HasValue ||
		                         _serverConfigUnavailable;
		_serverStrafeCost = null;
		_serverDoubleStrafeCost = null;
		_serverExtractionCost = null;
		_serverPriorityExfilCost = null;
		_serverUavCost = null;
		_serverFocusedSweepCost = null;
		_serverPaymentMode = null;
		_serverPaymentSource = null;
		_serverStashRoubleBalance = null;
		_serverConfigRevision = 0;
		_serverConfigUnavailable = false;
		_serverConfigUnavailableReason = null;
		_serverPurchasePersistenceEnabled = false;
		_serverRefundFailedDispatch = true;
		_serverSpendCreditsBeforeCash = true;
		_serverAllowAutoPurchaseOnUse = true;
		if (hadServerSettings)
		{
			TscDiagnostics.LogPayment("Cleared server URL TSC prices and payment settings.");
		}
	}

	public static void SetSyncedPaymentMode(PaymentMode paymentMode)
	{
		_syncedPaymentMode = paymentMode;
		TscDiagnostics.LogPayment($"Using host TSC payment mode: {paymentMode}");
	}

	public static void SetSyncedPaymentSource(PaymentSource paymentSource)
	{
		_syncedPaymentSource = paymentSource;
		TscDiagnostics.LogPayment($"Using host TSC payment source: {paymentSource}");
	}

	public static void SetServerConfigPayment(
		PaymentMode paymentMode,
		PaymentSource paymentSource,
		int revision,
		int? stashRoubleBalance)
	{
		_serverPaymentMode = paymentMode;
		_serverPaymentSource = paymentSource;
		_serverConfigRevision = revision;
		_serverStashRoubleBalance = stashRoubleBalance;
		_serverConfigUnavailable = false;
		_serverConfigUnavailableReason = null;
		TscDiagnostics.LogPayment(
			$"Using server URL TSC payment revision={revision}: mode={paymentMode}, source={paymentSource}, stashBalance={(stashRoubleBalance.HasValue ? FormatRoubles(stashRoubleBalance.Value) : "unknown")}");
	}

	public static void SetServerPurchasePersistence(
		bool enabled,
		bool refundFailedDispatch,
		bool spendCreditsBeforeCash,
		bool allowAutoPurchaseOnUse,
		int revision)
	{
		_serverPurchasePersistenceEnabled = enabled;
		_serverRefundFailedDispatch = refundFailedDispatch;
		_serverSpendCreditsBeforeCash = spendCreditsBeforeCash;
		_serverAllowAutoPurchaseOnUse = allowAutoPurchaseOnUse;
		_serverConfigRevision = revision;
	}

	public static void MarkServerConfigUnavailable(string reason)
	{
		_serverConfigUnavailable = true;
		_serverConfigUnavailableReason = reason;
		FireSupportPlugin.LogSource.LogWarning($"Server URL TSC config unavailable: {reason}");
	}

	public static PaymentMode GetConfiguredPaymentMode()
	{
		return PluginSettings.PaymentMode.Value;
	}

	public static PaymentMode GetActivePaymentMode()
	{
		return _syncedPaymentMode ?? _serverPaymentMode ?? GetConfiguredPaymentMode();
	}

	public static PaymentSource GetConfiguredPaymentSource()
	{
		return PluginSettings.PaymentSource?.Value ?? PaymentSource.CarriedRoubles;
	}

	public static PaymentSource GetActivePaymentSource()
	{
		return _syncedPaymentSource ?? _serverPaymentSource ?? GetConfiguredPaymentSource();
	}

	public static int GetConfiguredCost(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Strafe => PluginSettings.StrafeRequestCostRoubles.Value,
			ESupportType.DoubleStrafe => PluginSettings.DoubleStrafeRequestCostRoubles.Value,
			ESupportType.Extract => PluginSettings.ExtractionRequestCostRoubles.Value,
			ESupportType.PriorityExfil => PluginSettings.PriorityExfilRequestCostRoubles.Value,
			ESupportType.Uav => PluginSettings.UavRequestCostRoubles.Value,
			ESupportType.FocusedSweep => PluginSettings.FocusedSweepRequestCostRoubles.Value,
			_ => 0
		};
	}

	public static int GetActiveCost(ESupportType supportType)
	{
		return GetCost(supportType);
	}

	public static int GetEffectiveCost(ESupportType supportType)
	{
		return GetActiveCost(supportType);
	}

	public static int GetCarriedRoubleBalance()
	{
		return GetCarriedRoubles();
	}

	public static int GetEffectiveBalance()
	{
		PaymentSource paymentSource = GetActivePaymentSource();
		int carriedRoubles = GetCarriedRoubles();
		return paymentSource switch
		{
			PaymentSource.CarriedRoubles => carriedRoubles,
			PaymentSource.StashRoubles => _serverStashRoubleBalance ?? -1,
			PaymentSource.PreferCarriedThenStash => _serverStashRoubleBalance.HasValue
				? carriedRoubles + _serverStashRoubleBalance.Value
				: carriedRoubles,
			PaymentSource.PreferStashThenCarried => _serverStashRoubleBalance.HasValue
				? carriedRoubles + _serverStashRoubleBalance.Value
				: carriedRoubles,
			_ => carriedRoubles
		};
	}

	public static string GetEffectiveBalanceLabel()
	{
		return GetActivePaymentSource() switch
		{
			PaymentSource.StashRoubles => "Stash Roubles",
			PaymentSource.PreferCarriedThenStash => "Available Roubles",
			PaymentSource.PreferStashThenCarried => "Available Roubles",
			_ => "Carried Roubles"
		};
	}

	public static bool CanAfford(ESupportType supportType, bool notify = false)
	{
		if (!FireSupportServiceAvailability.IsServiceEnabled(supportType))
		{
			if (notify)
			{
				NotifyServiceUnavailable(supportType);
			}

			return false;
		}

		int cost = GetCost(supportType);
		if (cost <= 0)
		{
			return true;
		}

		int effectiveBalance = GetEffectiveBalance();
		bool canAfford = effectiveBalance >= cost;

		if (!canAfford && notify)
		{
			NotifyInsufficientFunds(cost, effectiveBalance);
		}

		return canAfford;
	}

	public static bool TryCharge(ESupportType supportType)
	{
		return TryCharge(supportType, notifySuccess: true, notifyFailure: true);
	}

	public static bool TryCharge(ESupportType supportType, bool notifySuccess, bool notifyFailure = true)
	{
		if (!FireSupportServiceAvailability.IsServiceEnabled(supportType))
		{
			if (notifyFailure)
			{
				NotifyServiceUnavailable(supportType);
			}

			return false;
		}

		int cost = GetCost(supportType);
		if (cost <= 0)
		{
			return true;
		}

		if (!CanSpendCarriedForActivePaymentSource(cost))
		{
			if (notifyFailure)
			{
				NotifyServerPaymentRequired(supportType);
			}

			return false;
		}

		if (!TrySpendCarriedRoubles(cost, out int carriedRoubles))
		{
			if (notifyFailure)
			{
				NotifyInsufficientFunds(cost, carriedRoubles);
			}

			return false;
		}

		if (notifySuccess)
		{
			NotificationManagerClass.DisplayMessageNotification(
				$"Paid {FormatRoubles(cost)} for {GetSupportName(supportType)}.",
				ENotificationDurationType.Default,
				ENotificationIconType.Default,
				null);
		}

		return true;
	}

	public static bool CanDeployFromRadial(ESupportType supportType, bool notify = false)
	{
		if (!FireSupportServiceAvailability.IsServiceEnabled(supportType))
		{
			if (notify)
			{
				NotifyServiceUnavailable(supportType);
			}

			return false;
		}

		PaymentMode paymentMode = GetActivePaymentMode();
		if (paymentMode == PaymentMode.PhoneAuthorizations)
		{
			if (FireSupportAuthorizations.HasDeployable(supportType))
			{
				return true;
			}

			if (notify)
			{
				NotifyAuthorizationRequired(supportType);
			}

			return false;
		}

		if (paymentMode == PaymentMode.Hybrid && FireSupportAuthorizations.HasDeployable(supportType))
		{
			return true;
		}

		return CanAfford(supportType, notify);
	}

	public static bool TryPayForDeployment(ESupportType supportType, out bool consumedAuthorization)
	{
		return TryPayForDeployment(supportType, out consumedAuthorization, out _);
	}

	public static bool TryPayForDeployment(
		ESupportType supportType,
		out bool consumedAuthorization,
		out ESupportType consumedAuthorizationType)
	{
		consumedAuthorization = false;
		consumedAuthorizationType = supportType;
		if (!FireSupportServiceAvailability.IsServiceEnabled(supportType))
		{
			NotifyServiceUnavailable(supportType);
			return false;
		}

		PaymentMode paymentMode = GetActivePaymentMode();

		if (paymentMode == PaymentMode.PhoneAuthorizations ||
		    paymentMode == PaymentMode.Hybrid)
		{
			if (FireSupportAuthorizations.TryConsumeForDeployment(supportType, out consumedAuthorizationType))
			{
				consumedAuthorization = true;
				NotificationManagerClass.DisplayMessageNotification(
					$"Used prepaid {GetSupportName(consumedAuthorizationType)} authorization.",
					ENotificationDurationType.Default,
					ENotificationIconType.Default,
					null);
				return true;
			}
		}

		if (paymentMode == PaymentMode.PhoneAuthorizations)
		{
			NotifyAuthorizationRequired(supportType);
			return false;
		}

		return TryCharge(supportType);
	}

	public static async UniTask<FireSupportAuthorizationUse> TryPayForDeploymentAsync(ESupportType supportType)
	{
		if (!_serverPurchasePersistenceEnabled)
		{
			bool ok = TryPayForDeployment(supportType, out bool consumedAuthorization, out ESupportType localConsumedType);
			return new FireSupportAuthorizationUse
			{
				Ok = ok,
				ConsumedAuthorization = consumedAuthorization,
				ConsumedAuthorizationType = localConsumedType
			};
		}

		if (!FireSupportServiceAvailability.IsServiceEnabled(supportType))
		{
			NotifyServiceUnavailable(supportType);
			return FireSupportAuthorizationUse.Failed(supportType);
		}

		PaymentMode paymentMode = GetActivePaymentMode();
		if ((paymentMode == PaymentMode.PhoneAuthorizations ||
		     paymentMode == PaymentMode.Hybrid && _serverSpendCreditsBeforeCash) &&
		    FireSupportAuthorizations.TryConsumeForDeployment(supportType, out ESupportType consumedType, out bool serverBacked))
		{
			// Local credits (carried-rouble purchases) have no ledger entry; asking
			// the server to consume one gets rejected and the credit becomes
			// unusable. Consume them purely client-side.
			if (!serverBacked)
			{
				NotificationManagerClass.DisplayMessageNotification(
					$"Used prepaid {GetSupportName(consumedType)} authorization.",
					ENotificationDurationType.Default,
					ENotificationIconType.Default,
					null);
				return new FireSupportAuthorizationUse
				{
					Ok = true,
					ConsumedAuthorization = true,
					ConsumedAuthorizationType = consumedType,
					ServerBacked = false
				};
			}

			string requestId = Guid.NewGuid().ToString("N");
			FireSupportPurchaseResponse response = await FireSupportServerConfigClient.ConsumeAuthorizationAsync(
				consumedType,
				requestId,
				_serverConfigRevision);
			if (response.Authorizations != null && response.Authorizations.Count > 0)
			{
				FireSupportAuthorizations.SetFromServer(response.Authorizations);
			}

			if (response.Ok)
			{
				NotificationManagerClass.DisplayMessageNotification(
					$"Used TerraGroup {GetSupportName(consumedType)} authorization.",
					ENotificationDurationType.Default,
					ENotificationIconType.Default,
					null);
				return new FireSupportAuthorizationUse
				{
					Ok = true,
					ConsumedAuthorization = true,
					ConsumedAuthorizationType = consumedType,
					RequestId = requestId,
					ServerBacked = true
				};
			}

			FireSupportAuthorizations.Refund(consumedType, serverBacked: true);
			NotifyAuthorizationRequired(supportType);
			return FireSupportAuthorizationUse.Failed(consumedType);
		}

		if (paymentMode == PaymentMode.PhoneAuthorizations)
		{
			NotifyAuthorizationRequired(supportType);
			return FireSupportAuthorizationUse.Failed(supportType);
		}

		if (_serverAllowAutoPurchaseOnUse && RequiresServerPurchase(GetActivePaymentSource()))
		{
			FireSupportPurchaseResponse purchase = await PurchaseAuthorizationAsync(supportType, notify: true);
			if (purchase.Authorizations != null && purchase.Authorizations.Count > 0)
			{
				FireSupportAuthorizations.SetFromServer(purchase.Authorizations);
			}

			if (purchase.Ok)
			{
				return await TryPayForDeploymentAsync(supportType);
			}

			return FireSupportAuthorizationUse.Failed(supportType);
		}

		bool charged = TryCharge(supportType);
		return new FireSupportAuthorizationUse
		{
			Ok = charged,
			ConsumedAuthorization = false,
			ConsumedAuthorizationType = supportType
		};
	}

	public static void RefundConsumedAuthorization(FireSupportAuthorizationUse authorizationUse)
	{
		if (!authorizationUse.ConsumedAuthorization)
		{
			return;
		}

		FireSupportAuthorizations.Refund(
			authorizationUse.ConsumedAuthorizationType,
			authorizationUse.ServerBacked);
		if (authorizationUse.ServerBacked && _serverRefundFailedDispatch)
		{
			FireSupportServerConfigClient.RefundAuthorizationAsync(
				authorizationUse.ConsumedAuthorizationType,
				authorizationUse.RequestId,
				_serverConfigRevision).Forget();
		}
	}

	public static void CommitConsumedAuthorization(FireSupportAuthorizationUse authorizationUse)
	{
		if (!authorizationUse.ConsumedAuthorization || !authorizationUse.ServerBacked)
		{
			return;
		}

		FireSupportServerConfigClient.CommitAuthorizationAsync(
			authorizationUse.ConsumedAuthorizationType,
			authorizationUse.RequestId,
			_serverConfigRevision).Forget();
	}

	public static bool TryPurchaseAuthorization(ESupportType supportType)
	{
		return TryPurchaseAuthorization(supportType, notify: true);
	}

	public static bool TryPurchaseAuthorization(ESupportType supportType, bool notify)
	{
		if (!FireSupportServiceAvailability.IsServiceEnabled(supportType))
		{
			if (notify)
			{
				NotifyServiceUnavailable(supportType);
			}

			return false;
		}

		if (RequiresServerPurchase(GetActivePaymentSource()))
		{
			if (notify)
			{
				NotifyServerPaymentRequired(supportType);
			}

			return false;
		}

		if (!TryCharge(supportType, notifySuccess: false, notifyFailure: notify))
		{
			return false;
		}

		FireSupportAuthorizations.Grant(supportType);
		if (notify)
		{
			NotifyAuthorizationPurchased(supportType);
		}

		return true;
	}

	public static void TryPurchaseAuthorizationAsync(
		ESupportType supportType,
		bool notify,
		Action<bool, FireSupportPurchaseResponse> callback)
	{
		TryPurchaseAuthorizationAsyncInternal(supportType, notify, callback).Forget();
	}

	private static async UniTaskVoid TryPurchaseAuthorizationAsyncInternal(
		ESupportType supportType,
		bool notify,
		Action<bool, FireSupportPurchaseResponse> callback)
	{
		FireSupportPurchaseResponse result = await PurchaseAuthorizationAsync(supportType, notify);
		callback?.Invoke(result.Ok, result);
	}

	private static async UniTask<FireSupportPurchaseResponse> PurchaseAuthorizationAsync(ESupportType supportType, bool notify)
	{
		var result = new FireSupportPurchaseResponse
		{
			Ok = false,
			SupportType = supportType.ToString(),
			Cost = GetCost(supportType),
			PaymentSource = GetActivePaymentSource().ToString(),
			NewBalance = GetEffectiveBalance(),
			AuthorizationGranted = false,
			ServerRevision = _serverConfigRevision
		};

		if (!FireSupportServiceAvailability.IsServiceEnabled(supportType))
		{
			result.Reason = "ServiceUnavailable";
			RememberPurchaseDenial(supportType, result);
			if (notify)
			{
				NotifyServiceUnavailable(supportType);
			}

			return result;
		}

		if (_serverConfigUnavailable && ShouldRequireServerConfig())
		{
			result.Reason = "ServerConfigUnavailable";
			RememberPurchaseDenial(supportType, result);
			if (notify)
			{
				NotifyServerConfigUnavailable(supportType);
			}

			return result;
		}

		if (result.Cost <= 0)
		{
			GrantAuthorization(supportType, notify);
			result.Ok = true;
			result.AuthorizationGranted = true;
			return result;
		}

		PaymentSource paymentSource = GetActivePaymentSource();
		if (ShouldUseCarriedForPurchase(paymentSource, result.Cost))
		{
			if (!TryCharge(supportType, notifySuccess: false, notifyFailure: notify))
			{
				result.Reason = "InsufficientRoubles";
				result.NewBalance = GetEffectiveBalance();
				RememberPurchaseDenial(supportType, result);
				return result;
			}

			result.Ok = true;
			_lastPurchaseDenial = null;
			result.PaymentSource = nameof(PaymentSource.CarriedRoubles);
			result.NewBalance = GetEffectiveBalance();
			GrantAuthorization(supportType, notify);
			result.AuthorizationGranted = true;
			FireSupportPlugin.LogSource.LogInfo($"TSC authorization purchased: {GetSupportName(supportType)}.");
			return result;
		}

		TscDiagnostics.LogPayment(
			$"TSC purchase request sent source=Stash supportType={supportType} cost={result.Cost} revision={_serverConfigRevision}.");
		FireSupportPurchaseResponse serverResult = await FireSupportServerConfigClient.PurchaseAuthorizationAsync(
			supportType,
			_serverConfigRevision);
		serverResult.SupportType = string.IsNullOrWhiteSpace(serverResult.SupportType)
			? supportType.ToString()
			: serverResult.SupportType;
		serverResult.PaymentSource = string.IsNullOrWhiteSpace(serverResult.PaymentSource)
			? nameof(PaymentSource.StashRoubles)
			: serverResult.PaymentSource;
		serverResult.Cost = serverResult.Cost > 0 ? serverResult.Cost : result.Cost;
		serverResult.ServerRevision = serverResult.ServerRevision > 0 ? serverResult.ServerRevision : _serverConfigRevision;

		if (serverResult.NewBalance >= 0)
		{
			_serverStashRoubleBalance = serverResult.NewBalance;
		}

		if (!serverResult.Ok)
		{
			RememberPurchaseDenial(supportType, serverResult);
			if (notify)
			{
				NotifyAuthorizationPurchaseDenied(supportType, serverResult);
			}

			FireSupportPlugin.LogSource.LogWarning(
				$"TSC purchase denied source=Stash supportType={supportType} cost={serverResult.Cost} reason={serverResult.Reason} newBalance={serverResult.NewBalance} revision={serverResult.ServerRevision}.");
			return serverResult;
		}

		if (serverResult.Authorizations != null && serverResult.Authorizations.Count > 0)
		{
			FireSupportAuthorizations.SetFromServer(serverResult.Authorizations);
		}
		else
		{
			GrantServerAuthorization(supportType, notify);
		}

		serverResult.AuthorizationGranted = true;
		_lastPurchaseDenial = null;
		FireSupportPlugin.LogSource.LogInfo($"TSC authorization purchased: {GetSupportName(supportType)}.");
		return serverResult;
	}

	public static void NotifyAuthorizationPurchased(ESupportType supportType)
	{
		int cost = GetCost(supportType);
		string supportName = GetSupportName(supportType);
		string message = cost > 0
			? $"Paid {FormatRoubles(cost)}. {supportName} authorization added to YY menu."
			: $"{supportName} authorization added to YY menu.";

		NotificationManagerClass.DisplayMessageNotification(
			message,
			ENotificationDurationType.Default,
			ENotificationIconType.Default,
			null);
	}

	public static void NotifyAuthorizationPurchaseDenied(ESupportType supportType)
	{
		NotifyAuthorizationPurchaseDenied(supportType, _lastPurchaseDenial);
	}

	public static void NotifyAuthorizationPurchaseDenied(ESupportType supportType, FireSupportPurchaseResponse response)
	{
		if (!FireSupportServiceAvailability.IsServiceEnabled(supportType))
		{
			NotifyServiceUnavailable(supportType);
			return;
		}

		if (response != null)
		{
			RememberPurchaseDenial(supportType, response);
		}

		string reason = response?.Reason ?? _lastPurchaseDenial?.Reason;
		if (string.Equals(reason, "AuthorizationLimitReached", StringComparison.OrdinalIgnoreCase))
		{
			int held = GetAuthorizationCount(response ?? _lastPurchaseDenial, supportType);
			string countText = held > 0 ? $" You already hold {held}." : string.Empty;
			NotificationManagerClass.DisplayWarningNotification(
				$"{GetSupportName(supportType)} authorization limit reached.{countText} Use one from YY before buying more.",
				ENotificationDurationType.Long);
			return;
		}

		if (!string.Equals(reason, "InsufficientRoubles", StringComparison.OrdinalIgnoreCase) &&
		    !string.IsNullOrWhiteSpace(reason))
		{
			NotificationManagerClass.DisplayWarningNotification(
				$"{GetSupportName(supportType)} authorization denied: {GetLastPurchaseDenialDetail(supportType)}",
				ENotificationDurationType.Long);
			return;
		}

		NotifyInsufficientFunds(GetCost(supportType), GetEffectiveBalance());
	}

	public static void NotifyServiceUnavailable(ESupportType supportType)
	{
		NotificationManagerClass.DisplayWarningNotification(
			$"{GetSupportName(supportType)} is unavailable in the host's FireSupport settings.",
			ENotificationDurationType.Long);
	}

	public static void NotifyServerConfigUnavailable(ESupportType supportType)
	{
		NotificationManagerClass.DisplayWarningNotification(
			$"{GetSupportName(supportType)} is unavailable: TerraGroup server config is not synced.",
			ENotificationDurationType.Long);
	}

	public static void NotifySettingsChanged(object source = null)
	{
		SettingsChanged?.Invoke(source, EventArgs.Empty);
	}

	private static FireSupportPurchaseResponse GetLastPurchaseDenial(ESupportType supportType)
	{
		if (_lastPurchaseDenial == null)
		{
			return null;
		}

		if (Enum.TryParse(_lastPurchaseDenial.SupportType, ignoreCase: true, out ESupportType deniedType) &&
		    deniedType != ESupportType.None &&
		    deniedType != supportType)
		{
			return null;
		}

		return _lastPurchaseDenial;
	}

	private static void RememberPurchaseDenial(ESupportType supportType, FireSupportPurchaseResponse response)
	{
		if (response == null)
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(response.SupportType))
		{
			response.SupportType = supportType.ToString();
		}

		_lastPurchaseDenial = response;
	}

	private static int GetAuthorizationCount(FireSupportPurchaseResponse response, ESupportType supportType)
	{
		if (response?.Authorizations != null &&
		    response.Authorizations.TryGetValue(GetAuthorizationLedgerKey(supportType), out int count))
		{
			return Math.Max(0, count);
		}

		return FireSupportAuthorizations.Get(supportType);
	}

	private static string GetAuthorizationLedgerKey(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Strafe => "A10",
			ESupportType.DoubleStrafe => "DoublePass",
			ESupportType.Extract => "Extraction",
			ESupportType.PriorityExfil => "PriorityExfil",
			ESupportType.Uav => "Uav",
			ESupportType.FocusedSweep => "FocusedSweep",
			_ => supportType.ToString()
		};
	}

	private static int GetCost(ESupportType supportType)
	{
		int cost = supportType switch
		{
			ESupportType.Strafe => _syncedStrafeCost ?? _serverStrafeCost ?? GetConfiguredCost(supportType),
			ESupportType.DoubleStrafe => _syncedDoubleStrafeCost ?? _serverDoubleStrafeCost ?? GetConfiguredCost(supportType),
			ESupportType.Extract => _syncedExtractionCost ?? _serverExtractionCost ?? GetConfiguredCost(supportType),
			ESupportType.PriorityExfil => _syncedPriorityExfilCost ?? _serverPriorityExfilCost ?? GetConfiguredCost(supportType),
			ESupportType.Uav => _syncedUavCost ?? _serverUavCost ?? GetConfiguredCost(supportType),
			ESupportType.FocusedSweep => _syncedFocusedSweepCost ?? _serverFocusedSweepCost ?? GetConfiguredCost(supportType),
			_ => 0
		};
		LogEffectiveCostIfChanged(supportType, cost, GetCostSource(supportType));
		return cost;
	}

	private static string GetCostSource(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Strafe when _syncedStrafeCost.HasValue => "FikaHost",
			ESupportType.DoubleStrafe when _syncedDoubleStrafeCost.HasValue => "FikaHost",
			ESupportType.Extract when _syncedExtractionCost.HasValue => "FikaHost",
			ESupportType.PriorityExfil when _syncedPriorityExfilCost.HasValue => "FikaHost",
			ESupportType.Uav when _syncedUavCost.HasValue => "FikaHost",
			ESupportType.FocusedSweep when _syncedFocusedSweepCost.HasValue => "FikaHost",
			ESupportType.Strafe when _serverStrafeCost.HasValue => "ServerURL",
			ESupportType.DoubleStrafe when _serverDoubleStrafeCost.HasValue => "ServerURL",
			ESupportType.Extract when _serverExtractionCost.HasValue => "ServerURL",
			ESupportType.PriorityExfil when _serverPriorityExfilCost.HasValue => "ServerURL",
			ESupportType.Uav when _serverUavCost.HasValue => "ServerURL",
			ESupportType.FocusedSweep when _serverFocusedSweepCost.HasValue => "ServerURL",
			_ => "LocalF12"
		};
	}

	private static void GrantAuthorization(ESupportType supportType, bool notify)
	{
		FireSupportAuthorizations.Grant(supportType);
		if (notify)
		{
			NotifyAuthorizationPurchased(supportType);
		}
	}

	private static void GrantServerAuthorization(ESupportType supportType, bool notify)
	{
		// The server charged for this credit, so it belongs to the ledger-backed
		// store; the next config sync will confirm or correct it.
		FireSupportAuthorizations.GrantServer(supportType);
		if (notify)
		{
			NotifyAuthorizationPurchased(supportType);
		}
	}

	private static bool ShouldRequireServerConfig()
	{
		return PluginSettings.UseServerConfigUrl?.Value == true &&
		       PluginSettings.RequireServerConfigInFika?.Value == true;
	}

	private static bool RequiresServerPurchase(PaymentSource paymentSource)
	{
		return paymentSource == PaymentSource.StashRoubles ||
		       paymentSource == PaymentSource.PreferCarriedThenStash ||
		       paymentSource == PaymentSource.PreferStashThenCarried;
	}

	private static bool ShouldUseCarriedForPurchase(PaymentSource paymentSource, int cost)
	{
		return paymentSource == PaymentSource.CarriedRoubles ||
		       paymentSource == PaymentSource.PreferCarriedThenStash && GetCarriedRoubles() >= cost ||
		       paymentSource == PaymentSource.PreferStashThenCarried &&
		       _serverStashRoubleBalance.HasValue &&
		       _serverStashRoubleBalance.Value < cost &&
		       GetCarriedRoubles() >= cost;
	}

	private static bool CanSpendCarriedForActivePaymentSource(int cost)
	{
		PaymentSource paymentSource = GetActivePaymentSource();
		return paymentSource == PaymentSource.CarriedRoubles ||
		       paymentSource == PaymentSource.PreferCarriedThenStash && GetCarriedRoubles() >= cost ||
		       paymentSource == PaymentSource.PreferStashThenCarried &&
		       _serverStashRoubleBalance.HasValue &&
		       _serverStashRoubleBalance.Value < cost &&
		       GetCarriedRoubles() >= cost;
	}

	private static void LogEffectiveCostIfChanged(ESupportType supportType, int cost, string source)
	{
		if (supportType == ESupportType.None)
		{
			return;
		}

		if (s_lastLoggedCost.TryGetValue(supportType, out CostLogState last) &&
		    last.Cost == cost &&
		    string.Equals(last.Source, source, StringComparison.Ordinal))
		{
			return;
		}

		s_lastLoggedCost[supportType] = new CostLogState(cost, source);
		TscDiagnostics.LogPayment($"Effective TSC cost product={supportType} source={source} cost={cost}");
	}

	private static int GetCarriedRoubles()
	{
		Player player = Singleton<GameWorld>.Instance?.MainPlayer;
		if (player == null)
		{
			return 0;
		}

		int total = 0;
		foreach (Item item in GetCarriedRoubleStacks(player))
		{
			if (item != null && item.StackObjectsCount > 0)
			{
				total += item.StackObjectsCount;
			}
		}

		return total;
	}

	private static bool TrySpendCarriedRoubles(int cost, out int carriedRoubles)
	{
		carriedRoubles = 0;
		Player player = Singleton<GameWorld>.Instance?.MainPlayer;
		if (player == null)
		{
			return false;
		}

		var roubleStacks = new List<Item>();
		foreach (Item item in GetCarriedRoubleStacks(player))
		{
			if (item == null || item.StackObjectsCount <= 0)
			{
				continue;
			}

			roubleStacks.Add(item);
			carriedRoubles += item.StackObjectsCount;
		}

		if (carriedRoubles < cost)
		{
			return false;
		}

		int remainingCost = cost;
		foreach (Item stack in roubleStacks)
		{
			if (remainingCost <= 0)
			{
				break;
			}

			int amountToTake = Math.Min(stack.StackObjectsCount, remainingCost);
			remainingCost -= amountToTake;

			if (amountToTake >= stack.StackObjectsCount)
			{
				RemoveStack(stack);
				continue;
			}

			stack.StackObjectsCount -= amountToTake;
			stack.RaiseRefreshEvent(refreshIcon: true, checkMagazine: false);
		}

		return true;
	}

	private static IEnumerable<Item> GetCarriedRoubleStacks(Player player)
	{
		if (player.InventoryController != null)
		{
			foreach (Item item in player.InventoryController.GetReachableItemsOfType<Item>(IsRouble))
			{
				yield return item;
			}

			yield break;
		}

		foreach (Item item in player.Profile.Inventory.AllRealPlayerItems)
		{
			if (IsRouble(item))
			{
				yield return item;
			}
		}
	}

	private static bool IsRouble(Item item)
	{
		return item != null && item.TemplateId == ItemConstants.ROUBLES_TPL;
	}

	private static void RemoveStack(Item stack)
	{
		ItemAddress address = stack.CurrentAddress ?? stack.Parent;
		if (address != null)
		{
			address.RemoveWithoutRestrictions(stack);
			return;
		}

		stack.StackObjectsCount = 0;
		stack.RaiseRefreshEvent(refreshIcon: true, checkMagazine: false);
	}

	private static void NotifyInsufficientFunds(int cost, int carriedRoubles)
	{
		if (carriedRoubles < 0)
		{
			NotificationManagerClass.DisplayWarningNotification(
				$"Fire support requires {FormatRoubles(cost)}. {GetEffectiveBalanceLabel()} are still syncing.",
				ENotificationDurationType.Long);
			return;
		}

		NotificationManagerClass.DisplayWarningNotification(
			$"Fire support requires {FormatRoubles(cost)}. {GetEffectiveBalanceLabel()}: {FormatRoubles(carriedRoubles)}.",
			ENotificationDurationType.Long);
	}

	private static void NotifyServerPaymentRequired(ESupportType supportType)
	{
		NotificationManagerClass.DisplayWarningNotification(
			$"{GetSupportName(supportType)} requires TerraGroup server payment confirmation.",
			ENotificationDurationType.Long);
	}

	private static void NotifyAuthorizationRequired(ESupportType supportType)
	{
		NotificationManagerClass.DisplayWarningNotification(
			$"{GetSupportName(supportType)} requires a TerraGroup phone authorization.",
			ENotificationDurationType.Long);
	}

	private static string FormatRoubles(int amount)
	{
		return $"{amount:N0} RUB";
	}

	public static string GetSupportName(ESupportType supportType)
	{
		return supportType switch
		{
			ESupportType.Strafe => "A-10 strafe",
			ESupportType.DoubleStrafe => "A-10 double pass",
			ESupportType.Extract => "UH-60 extraction",
			ESupportType.PriorityExfil => "priority exfil",
			ESupportType.Uav => "UAV recon",
			ESupportType.FocusedSweep => "focused sweep",
			_ => "fire support"
		};
	}
}
