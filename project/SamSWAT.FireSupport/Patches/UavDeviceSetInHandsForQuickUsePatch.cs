using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SamSWAT.FireSupport.ArysReloaded.Unity;
using SPT.Reflection.Patching;
using System;
using System.Linq;
using System.Reflection;

namespace SamSWAT.FireSupport.ArysReloaded.Patches;

internal sealed class UavDeviceSetInHandsForQuickUsePatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return typeof(Player)
			.GetMethods(BindingFlags.Public | BindingFlags.Instance)
			.First(method =>
				method.Name == nameof(Player.SetInHandsForQuickUse) &&
				method.GetParameters().Length == 2 &&
				method.GetParameters()[0].ParameterType == typeof(Item));
	}

	[PatchPrefix]
	private static bool Prefix(Player __instance, Item __0, Callback<IOnHandsUseCallback> __1)
	{
		Item item = __0;
		Callback<IOnHandsUseCallback> callback = __1;

		if (UavDeviceController.ShouldSuppressQuickUse(__instance) &&
		    !UavDeviceConstants.IsUavDeviceTemplate(item))
		{
			TscDiagnostics.LogPhone(
				$"TSC Uplink suppressed quick-slot hand swap while active. item={item?.Id ?? "<null>"}, tpl={item?.StringTemplateId ?? "<null>"}.");
			// Complete the callback with a failure instead of dropping it: EFT's
			// caller waits on it, and an uninvoked callback freezes the player in
			// the interaction state. This froze quick-use of OTHER items (meds,
			// grenades, ground pickups) while the phone session was active.
			callback?.Invoke(new Result<IOnHandsUseCallback>(null, "TSC Uplink session is active.", 0));
			return false;
		}

		if (item is not UavDeviceItem)
		{
			if (UavDeviceConstants.IsUavDeviceTemplate(item))
			{
				FireSupportPlugin.LogSource.LogWarning(
					$"TerraGroup TSC Uplink quick-use not routed: runtime item type is {item.GetType().FullName}, expected {typeof(UavDeviceItem).FullName}.");
			}

			return true;
		}

		// Ground pickups can enter this method before the item is inside the
		// player's inventory. Intercepting then breaks EFT's pickup interaction
		// and freezes the player, so let vanilla handle any item we do not own.
		if (__instance.InventoryController.FindItem<UavDeviceItem>(item.Id) == null)
		{
			TscDiagnostics.LogPhone(
				$"TSC Uplink quick-use not intercepted: item is not in the player's inventory. item={item.Id}.");
			return true;
		}

		TscDiagnostics.LogPhone(
			$"TerraGroup TSC Uplink quick-use forwarding to SetInHandsUsableItem. item={item.Id}, tpl={item.TemplateId}, type={item.GetType().FullName}.");

		Callback<GInterface202> wrappedCallback = result =>
		{
			if (callback == null)
			{
				return;
			}

			if (result.Failed)
			{
				callback.Invoke(new Result<IOnHandsUseCallback>(null, result.Error, result.ErrorCode));
				return;
			}

			if (result.Value is IOnHandsUseCallback quickUseController)
			{
				callback.Invoke(new Result<IOnHandsUseCallback>(quickUseController));
				return;
			}

			callback.Invoke(new Result<IOnHandsUseCallback>(
				null,
				"TerraGroup TSC Uplink controller did not implement IOnHandsUseCallback.",
				0));
		};

		try
		{
			__instance.SetInHandsUsableItem(item, wrappedCallback);
		}
		catch (Exception ex)
		{
			// EFT's caller waits on this callback; leaving it uninvoked freezes
			// the player in the interaction state.
			FireSupportPlugin.LogSource.LogWarning($"TSC Uplink quick-use equip failed. {ex}");
			callback?.Invoke(new Result<IOnHandsUseCallback>(null, ex.Message, 0));
		}

		return false;
	}
}
