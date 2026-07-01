using BepInEx;
using BepInEx.Bootstrap;
using System.Runtime.CompilerServices;

namespace SamSWAT.FireSupport.ArysReloaded.Fika;

[BepInPlugin(
	"com.tylevo.tacticalservicescontrol.fika",
	"Tylevo-TacticalServicesControl-Fika",
	ModMetadata.VERSION)]
[BepInDependency("com.tylevo.tacticalservicescontrol", ModMetadata.VERSION)]
[BepInDependency(FikaCoreGuid, BepInDependency.DependencyFlags.SoftDependency)]
public class FireSupportFikaPlugin : BaseUnityPlugin
{
	public const string FikaCoreGuid = "com.fika.core";

	// Fika.Core.dll is absent on single-player installs, so this class must never
	// reference Fika types: the runtime resolves a method's type references when it
	// is first compiled, and would throw even if the Fika code path is never taken.
	// Everything that touches Fika lives in FikaIntegration, reached only through
	// the [MethodImpl(NoInlining)] trampolines below after com.fika.core is
	// confirmed loaded.
	private static bool s_fikaPresent;

	private void Awake()
	{
		s_fikaPresent = Chainloader.PluginInfos.ContainsKey(FikaCoreGuid);
		if (!s_fikaPresent)
		{
			Logger.LogInfo("Fika not detected; multiplayer sync disabled. This is normal for single-player installs.");
			return;
		}

		EnableIntegration();
	}

	private void Update()
	{
		if (s_fikaPresent)
		{
			UpdateIntegration();
		}
	}

	private void OnDestroy()
	{
		if (s_fikaPresent)
		{
			DisableIntegration();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void EnableIntegration()
	{
		FikaIntegration.Enable(Logger);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void UpdateIntegration()
	{
		FikaIntegration.OnUpdate();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void DisableIntegration()
	{
		FikaIntegration.Disable();
	}
}
