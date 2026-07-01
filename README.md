# Tylevo's Tactical Services Control

Tylevo's Tactical Services Control, or TSC, is a public beta Fire Support rework for SPT. It adds the TerraGroup TSC Uplink, phone-based support authorization, stash and carried rouble payment modes, A-10 and UH-60 support options, UAV recon tools, Fika support sync, and a local dashboard for host configuration.

This project is derivative of SamSWAT's original Fire Support and SamSWAT's Fire Support - Arys Reloaded by Arys. Public redistribution is prepared with upstream permission recorded in `PERMISSIONS.md`, with full credit retained in `THIRD_PARTY_NOTICES.md` and `docs/credits.md`.

## Features

- TerraGroup TSC Uplink item.
- Phone-based support authorization flow.
- PhoneAuthorizations and Hybrid payment modes.
- Stash rouble payment and carried rouble payment.
- A-10 Strafe and A-10 Double Pass.
- UH-60 Extraction and Priority Exfil.
- UAV Recon and Focused Sweep.
- UAV radar overlay and UAV A-10 loiter visual.
- Fika support request sync.
- Fika host-authoritative settings sync.
- Local TSC Dashboard configuration.

## Requirements

- SPT 4.0.13.
- UnityToolkit v2.0.1.
- WTT Client Common Lib and WTT Server Common Lib, installed separately as required dependencies.
- Project Fika for multiplayer/Fika use.

TSC does not bundle WTT Common Lib. The client and server projects reference the installed WTT dependency DLLs at runtime/build time, so WTT should be listed as a dependency on Forge rather than redistributed inside the TSC package.

## Installation

1. Back up your profiles before testing the public beta.
2. Install the required dependencies listed above.
3. Extract the release ZIP directly into your SPT root.
4. Confirm these folders exist after extraction:
   - `BepInEx/plugins/Tylevo.TacticalServicesControl/`
   - `SPT/user/mods/Tylevo.TacticalServicesControl/`
5. Start SPT normally.

Do not place the ZIP contents inside an extra nested folder.

## Fika Installation

Install the same TSC version on the host, any headless host, and every client. The host config is authoritative while connected. Client local config does not override host settings during a joined raid, dashboard changes on the host sync to clients, and disconnect clears synced overrides.

## How To Use

- The TerraGroup TSC Uplink appears at the configured trader/assort.
- Press `U` to equip/open the TSC Uplink.
- Left click advances through non-payment screens.
- Use `1`, `2`, and `3` to choose valid categories/options.
- Press Enter to start the confirmation sequence.
- Right click or `U` cancels before payment commit.
- The swipe animation is visual only.
- Previous weapon restores cleanly.
- YY/rangefinder deployment still handles targeted support after authorization.
- UAV Recon and Focused Sweep do not require the rangefinder.

## Payment Modes

TSC supports carried roubles, stash roubles, and hybrid payment behavior where configured. The phone displays the active price and balance source, and the server calculates authoritative stash prices. Client-sent prices are not trusted.

Back up profiles before testing payment-source modes.

## Dashboard

The TSC Dashboard is local by default:

- Public health route: `/tsc/health`
- Dashboard route: `/tsc/admin`
- Admin diagnostics route: `/tsc/admin/health`
- Config file: `config/tsc-config.json`
- Token file: `config/tsc-admin-token.txt`

Remote dashboard access is disabled by default. If you enable remote access, keep it on a trusted LAN/VPN only and require the admin token for writes. Do not port-forward the dashboard.

See `docs/dashboard.md`, `PRIVACY.md`, and `SECURITY.md`.

## Known Issues

- Phone inventory inspect model may still need polish.
- Mortar/artillery support is planned but not included.
- Phone-as-designator/rangefinder replacement is planned but not included.
- Remote third-person phone animation sync is planned but not included.
- Public beta: back up profiles before testing payment modes.

Stash rouble payment and non-host A-10 tracer visibility are not listed as known issues; both are considered implemented and must be regression-tested before public upload.

## Credits

Credits and notices are in `THIRD_PARTY_NOTICES.md` and `docs/credits.md`.

- SamSWAT for the original Fire Support.
- Arys for SamSWAT's Fire Support - Arys Reloaded.
- danauraborealis for Manimal Hacker Mod material used under the MIT license.
- Accurate Circular Radar / Tyrian Radar Standalone for adapted radar HUD material, if those assets/code remain.
- SPT and Project Fika as compatibility targets.

## License And Permissions

Tylevo's Tactical Services Control is released under Creative Commons Attribution-NonCommercial 4.0 International (CC BY-NC 4.0), matching the upstream Arys Reloaded source license. Arys clarified that the Forge/SPT Hub BY-NC 3.0 listing was due to historical site limitations before the Forge migration.

Upstream-derived Fire Support material is redistributed with permission and full attribution. Third-party components keep their own license terms.

## Optional Tip

If you enjoy the project and want to support future work, you can leave a voluntary tip on Ko-fi. This is optional and does not unlock features, early access, or support priority.

https://ko-fi.com/tylevo

This tip link is included with upstream permission for the public beta release. It is voluntary only and does not unlock features, early access, downloads, updates, or support priority.
