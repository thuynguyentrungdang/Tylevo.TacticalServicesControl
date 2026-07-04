# Changelog

## 0.9.6 - Public Beta (released as v1.0.6)

### Fixed

- Stash purchases now work for every Fika client with no network configuration. TSC calls to the server (purchases and config/stash sync) now route through SPT's own backend connection instead of a separately configured HTTP URL, so they automatically reach the correct server for the host and for clients on any network (LAN, Radmin VPN, direct) and charge the right player's stash. The Server Config URL setting is no longer needed and can be left at its default; a wrong value no longer causes "Check the TSC server and dashboard connection."

## 0.9.5 - Public Beta (released as v1.0.5)

### Fixed

- Fika clients can now use stash-based purchases when they point their TSC server URL at the host. A host running default config broadcasts its own loopback address (127.0.0.1), which used to override each client's configured host address and send the purchase to the client's own machine. A loopback host broadcast is now ignored so the client's own Server Config URL takes effect. (For zero-config clients, the host can instead set its Server Config URL to its LAN IP; or use the CarriedRoubles payment source, which is fully client-side.)
- UH-60 extraction as a Fika host no longer strands the lobby. Extraction routed through EFT's session stop instead of Fika's extract flow, so a host extracting first killed the hosted session while other players kept playing into a dead lobby. Extraction now goes through Fika's extract path (host to spectate, session stays alive); solo and non-Fika installs are unchanged.

### Changed

- Phone navigation simplified. Number keys (1-3) now open a category directly instead of only highlighting it, RMB steps back one screen, and Escape closes the phone. Enter still only confirms on the final screen so stray input cannot spend money.
- The TerraGroup TSC Uplink now uses the special-equipment look: orange grid background and the orange SPEC tag, matching the rangefinder, so it sorts and reads as special-slot gear.

## 0.9.4 - Public Beta (released as v1.0.4)

### Fixed

- Carried-rouble purchases no longer lose your money. Authorizations bought with carried roubles used to vanish within seconds of purchase — the service showed AUTH REQ again unless you deployed it almost immediately, and the roubles were spent either way. These purchases now persist for the whole raid and can be deployed whenever you're ready.
- Non-host Fika players now see A-10 tracers reliably. Tracer playback was scheduled against the host's clock, which is unrelated to the client's; depending on which machine had more uptime, tracers rendered all at once or never. Clients now anchor playback to their own packet arrival time.
- Non-host Fika players now see the GAU-8 impact explosions. Only the host simulates the A-10 ballistics, so detonation effects existed only there; clients now emit the same big_smoky_explosion effect at each round's impact point during tracer playback, matching the host's view.
- Potentially fixed a freeze (movement and camera locked, weapon still usable) affecting loot pickups after the phone had been opened from its special slot and cancelled with the uplink hotkey. Two hand-restore flows raced; quick-use sessions are now restored by the game alone. The race was intermittent by nature, so please report if it still occurs on this version.
- Carried-rouble payment now counts money stored in the secure container. The previous inventory scan excluded it, so purchases failed with "Carried Roubles: 0" despite cash being on the character.
- Stash balance now syncs outside raids too. Config requests previously carried no profile id in menus, the hideout, or the first seconds of a raid, so stash-based payment sources displayed carried-only balances until an in-raid sync completed.

### Changed

- Rebalanced default prices for new installs: Extraction 300k (was 50k), Priority Exfil 450k (was 150k), UAV 125k (was 100k), Focused Sweep 90k (was 75k). A-10 Strafe and Double Pass unchanged. Max stored authorizations per service reduced from 3 to 2. Existing configs keep their saved values.
- TSC now declares a BepInEx incompatibility with SamSWAT's Fire Support: Arys Reloaded (requested by Arys). TSC is its derivative replacement and the two cannot run together; BepInEx now skips TSC with a clear message instead of letting them corrupt each other.
- Dashboard is easier to find: opening `/tsc` in a browser now redirects to the dashboard at `/tsc/admin` (the game's config polling is unaffected), the dashboard asset error now names the folder it expects so missing installs are self-diagnosable, and the README dashboard URL was corrected.

## 0.9.3 - Public Beta (released as v1.0.3)

### Fixed

- Rebuilt all eight asset bundles with unique internal archive (CAB) identities. The phone bundles previously shared CAB IDs with Manimal's Hacker mod, the radar HUD bundle was a byte-copy of Tyrian Radar Standalone's bundle, and the FireSupport-lineage bundles shared IDs with the original SamSWAT Fire Support. Unity refuses to load a bundle whose archive ID is already loaded, so running TSC alongside any of those mods broke the phone: missing inventory icon, red ERROR model (inventory and inspect screen), and crashes or failed raid loads with the phone equipped.
- Picking a dropped TSC Uplink off the ground with F no longer freezes the player. The equip patches previously intercepted items that were not yet in the player's inventory, breaking EFT's pickup interaction.
- Quick-using meds, grenades, or other items while the phone session is active no longer freezes the player; the swap is now declined cleanly instead of leaving EFT waiting forever.
- The phone no longer reacts to mouse clicks, number keys, or cancel input while the inventory screen is open.
- The uplink hotkey is ignored while the inventory screen is open.

## 0.9.2 - Public Beta (released as v1.0.2)

### Fixed

- Fixed infinite loading on installs without Fika, introduced in 0.9.1. The Fika plugin DLL contained packet types referencing Fika.Core; once the plugin started loading on non-Fika installs, other mods' assembly-wide type scans (for example WTT Client Common Lib) crashed with ReflectionTypeLoadException. All Fika-typed code now lives in `Tylevo.TacticalServicesControl.Fika.Interop.dll`, which is only loaded after Fika is confirmed present, so it stays invisible to type scans on single-player installs.

## 0.9.1 - Public Beta (released as v1.0.1)

### Fixed

- Fika is now a soft dependency. The TSC Fika plugin loads cleanly on installs without Fika and no longer logs a missing `com.fika.core` dependency error; multiplayer sync simply stays disabled.

## 0.9.0 - Public Beta

### Added

- TerraGroup TSC Uplink item.
- TerraGroup phone authorization flow.
- PhoneAuthorizations and Hybrid payment modes.
- Stash rouble payment support.
- Carried rouble payment support.
- A-10 Strafe support authorization.
- A-10 Double Pass support authorization.
- UH-60 Extraction support authorization.
- Priority Exfil support authorization.
- UAV Recon support authorization.
- Focused Sweep support authorization.
- Fika support request sync.
- Fika host-authoritative settings sync.
- Dynamic phone UI pricing.
- Opaque LCD backplate renderer.
- Local dashboard configuration UI.
- UAV radar overlay.
- UAV A-10 loiter visual.

### Changed

- Support purchase and support deployment are separated.
- Phone buys prepaid authorizations.
- YY/rangefinder deploys targeted support later.
- Base request amounts can be set to 0 without blocking prepaid phone authorizations.
- Public-facing branding now uses Tylevo's Tactical Services Control / TSC.

### Fixed

- Phone no longer shows white screen during startup.
- Phone LCD no longer shows world/xray transparency.
- Previous weapon restores after phone close.
- Non-host A-10 tracer visibility confirmed in Fika testing.

### Known Issues

- Phone inventory inspect model may still need polish.
- Mortar/artillery support is deferred.
- Phone-as-designator is deferred.
- Remote third-person phone animation sync is deferred.
