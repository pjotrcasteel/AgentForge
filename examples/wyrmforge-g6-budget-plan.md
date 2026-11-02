# WyrmForge G6 — Lighting Engine 1.0 plan (budgeting example)

## G6.1 — Validate G5 lighting baseline

Dependencies: G5 accepted and merged.
Inspect the current lighting contracts, material maps, emitter configurations and the approved G1–G5 behavior.
Do not replace unrelated game systems. Capture the current failing and passing lighting cases.
Acceptance: current town, neutral-grey test arena and one Wet Forest hunt still render on mobile.
Tests: add targeted regression tests for existing light directions and occluder elevation.

## G6.2 — Correct impact-shadow behavior

Depends on: G6.1.
Fix the transient long shadow cast from a projectile impact towards the player.
Calculate direction from the actual impact emitter and scene geometry; preserve the world-space shadow contract.
Tests: multiple Fireball approach vectors and static point-light directions.
Acceptance: shadow directions remain consistent on all supported rendering paths.

## G6.3 — Validate and document mobile performance

Depends on: G6.2.
Profile dynamic shadow count, material-map lookups, frame times and memory cost on a representative mobile device.
Retain fallback quality settings; do not silently disable shadows in the real hunt.
Acceptance: supply repeatable test evidence and measured device metrics.
Mandatory visual approval: stop before marking G6 accepted; present genuine in-game screenshots or footage.
