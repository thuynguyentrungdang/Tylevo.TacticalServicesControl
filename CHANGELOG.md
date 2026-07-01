# Changelog

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
