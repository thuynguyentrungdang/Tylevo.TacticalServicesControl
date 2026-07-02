using System.Collections.Generic;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class RaidOpsFireSupportServerConfig
{
	public int Revision { get; set; }
	public string PaymentMode { get; set; } = nameof(global::SamSWAT.FireSupport.ArysReloaded.Unity.PaymentMode.PhoneAuthorizations);
	public string PaymentSource { get; set; } = nameof(global::SamSWAT.FireSupport.ArysReloaded.Unity.PaymentSource.CarriedRoubles);
	public int RequestCooldownSeconds { get; set; } = 300;
	public int? StashRoubleBalance { get; set; }
	public Dictionary<string, int> Prices { get; set; } = new();
	public Dictionary<string, bool> Enabled { get; set; } = new();
	public AdminDashboardSettings AdminDashboard { get; set; } = new();
	public UavSettings Uav { get; set; } = new();
	public UavSettings FocusedSweep { get; set; } = new();
	public ExtractionSettings Extraction { get; set; } = new();
	public ExtractionSettings PriorityExfil { get; set; } = new();
	public A10Settings A10 { get; set; } = new();
	public A10Settings DoublePass { get; set; } = new();
	public PurchasePersistenceSettings PurchasePersistence { get; set; } = new();
	public Dictionary<string, int> Authorizations { get; set; } = new();

	public sealed class UavSettings
	{
		public int DurationSeconds { get; set; }
		public float RangeMeters { get; set; }
		public float ScanIntervalSeconds { get; set; }
	}

	public sealed class ExtractionSettings
	{
		public float DispatchDelaySeconds { get; set; }
		public int WaitTimeSeconds { get; set; }
		public float ExtractTimeSeconds { get; set; }
		public float SpeedMultiplier { get; set; }
	}

	public sealed class A10Settings
	{
		public float SecondPassDelaySeconds { get; set; }
	}
	public sealed class AdminDashboardSettings
	{
		public bool Enabled { get; set; } = true;
		public bool AllowRemoteAccess { get; set; }
		public bool RequireTokenForLocalhost { get; set; }
	}

	public sealed class PurchasePersistenceSettings
	{
		public bool Enabled { get; set; } = true;
		public string Mode { get; set; } = "PersistentAuthorizations";
		public string ConsumeOn { get; set; } = "AuthorizationAccepted";
		public bool RefundFailedDispatch { get; set; } = true;
		public int MaxStoredAuthorizationsPerService { get; set; } = 2;
		public int PendingUseTimeoutSeconds { get; set; } = 180;
		public bool SpendCreditsBeforeCash { get; set; } = true;
		public bool AllowAutoPurchaseOnUse { get; set; } = true;
	}
}

public sealed class FireSupportPurchaseRequest
{
	public string Action { get; set; } = string.Empty;
	public string SessionId { get; set; } = string.Empty;
	public string ProfileId { get; set; } = string.Empty;
	public string SupportType { get; set; } = string.Empty;
	public string RequestId { get; set; } = string.Empty;
	public int ClientKnownRevision { get; set; }
	public int Quantity { get; set; } = 1;
}

public sealed class FireSupportPurchaseResponse
{
	public bool Ok { get; set; }
	public string Reason { get; set; } = string.Empty;
	public string SupportType { get; set; } = string.Empty;
	public int Cost { get; set; }
	public string PaymentSource { get; set; } = string.Empty;
	public int NewBalance { get; set; }
	public bool AuthorizationGranted { get; set; }
	public bool AuthorizationConsumed { get; set; }
	public int ServerRevision { get; set; }
	public int ChargedFromStash { get; set; }
	public string RequestId { get; set; } = string.Empty;
	public Dictionary<string, int> Authorizations { get; set; } = new();
}
