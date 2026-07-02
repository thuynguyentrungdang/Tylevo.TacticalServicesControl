using EFT;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class FireSupportExtraction
{
	public delegate bool ExtractOverrideHandler(Player player, string exitName);

	// Installed by the Fika integration when a Fika session is possible.
	// Fika reroutes extraction so an extracting host keeps the session alive
	// for remaining players; stopping the session directly instead strands
	// everyone else in a dead lobby.
	public static ExtractOverrideHandler ExtractOverride;

	public static bool TryOverrideExtract(Player player, string exitName)
	{
		return ExtractOverride?.Invoke(player, exitName) == true;
	}
}
