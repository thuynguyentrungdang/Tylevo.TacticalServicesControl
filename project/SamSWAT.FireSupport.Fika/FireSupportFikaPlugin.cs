using BepInEx;
using BepInEx.Bootstrap;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SamSWAT.FireSupport.ArysReloaded.Fika;

[BepInPlugin(
	"com.tylevo.tacticalservicescontrol.fika",
	"Tylevo-TacticalServicesControl-Fika",
	ModMetadata.VERSION)]
[BepInDependency("com.tylevo.tacticalservicescontrol", ModMetadata.VERSION)]
[BepInDependency(FikaCoreGuid, BepInDependency.DependencyFlags.SoftDependency)]
// Mirror the core plugin's incompatibility so this plugin is skipped with a
// clear incompatibility message instead of a missing-dependency error.
[BepInIncompatibility("com.samswat.firesupport.arysreloaded")]
public class FireSupportFikaPlugin : BaseUnityPlugin
{
	public const string FikaCoreGuid = "com.fika.core";
	private const string InteropFileName = "Tylevo.TacticalServicesControl.Fika.Interop.dll";

	// Fika.Core.dll is absent on single-player installs, so this class must never
	// reference Fika types. It also must not contain them anywhere in the assembly:
	// other mods scan every loaded assembly with Assembly.GetTypes(), which throws
	// for any type whose Fika reference cannot be resolved. All Fika-typed code
	// therefore lives in the separate Interop assembly, which has no BepInPlugin
	// and is only loaded below once com.fika.core is confirmed present.
	private static bool s_integrationActive;

	private void Awake()
	{
		if (!Chainloader.PluginInfos.ContainsKey(FikaCoreGuid))
		{
			Logger.LogInfo("Fika not detected; multiplayer sync disabled. This is normal for single-player installs.");
			return;
		}

		try
		{
			string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			Assembly.LoadFrom(Path.Combine(pluginDir, InteropFileName));
			EnableIntegration();
			s_integrationActive = true;
		}
		catch (Exception ex)
		{
			Logger.LogWarning(
				$"Fika detected but TSC Fika integration failed to start; multiplayer sync disabled. " +
				$"Reinstall TSC if {InteropFileName} is missing. {ex}");
		}
	}

	private void Update()
	{
		if (s_integrationActive)
		{
			UpdateIntegration();
		}
	}

	private void OnDestroy()
	{
		if (s_integrationActive)
		{
			s_integrationActive = false;
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
