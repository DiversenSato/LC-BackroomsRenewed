# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.1.6] - 2026-11-12

First release of **Backrooms Renewed Fixed**, a fork of VELD-Dev's Backrooms Renewed by DiversenSato.

### Changed

- Changed the plugin GUID to `DiversenSato.BackroomsRenewedFixed`. Configs from the original mod are not carried over, and this mod won't load if the original is installed.
- Teleports that happen before the backrooms finish generating now wait for generation instead of dropping the player into the void.

### Fixed

- Fixed a bug that would generate too many walls and could trap the player in a box.
- Fixed a bug that would teleport the player outside the bounds of a cell.
- Fixed players sometimes being teleported on top of or below the backrooms.
- Fixed the backrooms navmesh never being built (without the legacy navmesh option), which made every teleport land in the middle of a cell.
- Fixed the ship teleporter: players sent to the backrooms are no longer pulled back to the ship 3 seconds later, and the chance is only rolled once instead of once per player in the lobby.
- Exit doors and cells are now cleaned up properly when the backrooms despawn.

## [0.1.5] - 2026-06-30

### Changed

- Improved teleportation to backrooms (PR [#3](https://github.com/VELD-Dev/LC-BackroomsRenewed/pull/3) by Entity378)

### Fixed

- Fixed a bug teleporting distant clients in the void
- Fixed lights not flickering (at least confirmed for host)
- Fixed a bug causing backrooms to be generated only for host

## [0.1.4] - 2026-06-10

### Removed

- Removed momentum cancellation as it was causing issues with teleportation

### Fixed

- Fixed a bug preventing from teleporting into the backrooms
- Fixed backrooms generation (PR [#1](https://github.com/VELD-Dev/LC-BackroomsRenewed/pull/1) by Entity378)
- Fixed configs mismatch (PR [#1](https://github.com/VELD-Dev/LC-BackroomsRenewed/pull/1) by Entity378)

## [0.1.3] - 2026-06-09

### Changed

- Removed any momentum the player could have had when teleporting to the backrooms.

### Fixed

- Fixed a bug causing backrooms to occasionally generate inside of a map's terrain.

## [0.1.2] - 2026-06-08

### Fixed

- Fixed a bug occurring when a player tried to join that would prevent from joining.

## [0.1.1] - 2026-06-08

### Fixed

- Fixed a bug occurring when a player tried to join that would prevent from joining.
- Fixed a game crash due to networking objects not synchronizing properly
- Fixed a bug that made some ceiling lights to be rendered black even if there was actually light.

## 0.1.0 - 2026-06-07

### Added

- Implemented customizable procedural backrooms generation with 4 algorithms.
- Implemented basic ways to get TP'd to the backrooms
- Added many settings to push to another level custom gameplay experience. Some settings are synchronizable and can be modified only by host during the game.
- Added bases for cells variants and Backrooms themes/levels (I'll decide later if we only keep 1 level of the backrooms at once or if we generate several backroom levels)
- Added ambience effects (networked twinkling lights) for more immersion
- Added ambient noise (slight background TV static)

[unreleased]: https://github.com/DiversenSato/LC-BackroomsRenewed/compare/v0.1.6...HEAD
[0.1.6]: https://github.com/DiversenSato/LC-BackroomsRenewed/releases/tag/v0.1.6
[0.1.5]: https://github.com/VELD-Dev/LC-BackroomsRenewed/releases/tag/v0.1.5
[0.1.4]: https://github.com/VELD-Dev/LC-BackroomsRenewed/releases/tag/v0.1.4
[0.1.3]: https://github.com/VELD-Dev/LC-BackroomsRenewed/releases/tag/v0.1.3
[0.1.2]: https://github.com/VELD-Dev/LC-BackroomsRenewed/releases/tag/v0.1.2
[0.1.1]: https://github.com/VELD-Dev/LC-BackroomsRenewed/releases/tag/v0.1.1
