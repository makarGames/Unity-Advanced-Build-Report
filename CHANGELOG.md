# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.3] - 2026-08-22

### Fixed

- Restored full MonoImporter / AssemblyDefinitionImporter blocks in .meta so Editor scripts compile and menu item appears.

## [1.0.2] - 2026-08-22

### Fixed

- Cross-assembly Addressables call: main Editor assembly can no longer reference Addressables collector type directly. Uses registration hook instead.

## [1.0.1] - 2026-08-22

### Fixed

- Invalid `.meta` GUIDs (not 32 hex chars) caused Unity to ignore Editor scripts вЂ” empty folders and missing menu item.
- Added missing `.meta` for README / CHANGELOG / LICENSE / documentation image.

## [1.0.0] - 2026-08-22

### Added

- Initial UPM release: combined Player BuildReport + Addressables Build Layout analyzer.
- Editor window at **Window в†’ Analysis в†’ Full Build Size Report**.
- Pagination (10 / 20 / 50), CSV export, source filters, large-asset filter.
- Hierarchy reference finder (scenes / prefabs) and asset-level references from Addressables layout.
- Optional Addressables integration via `USE_ADDRESSABLES` (`com.unity.addressables` version define).
- Editor assembly definitions for core + optional Addressables module.

[1.0.0]: https://github.com/makarGames/Unity-Advanced-Build-Report/releases/tag/v1.0.0
