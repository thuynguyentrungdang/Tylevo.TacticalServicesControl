# Fika

Fika is a soft dependency. The TSC Fika plugin ships in every package but only activates when `com.fika.core` is loaded; on single-player installs it logs an informational message and stays idle.

Install the same TSC version on:

- Host.
- Headless host, if used.
- Every client.

## Behavior

- Host config is authoritative while joined.
- Client local config does not override host settings during a joined raid.
- Dashboard changes on the host sync to clients.
- Disconnect clears synced overrides.
- Damage remains host-authoritative.

## Regression Checklist

- Host/client same version.
- A-10 Strafe.
- A-10 Double Pass.
- A-10 tracers visible to non-host.
- UH-60 Extraction.
- Priority Exfil.
- UAV Recon.
- Focused Sweep.
- Host dashboard config sync.
- Stash payment charges the correct player.
- Disconnect cleanup.
