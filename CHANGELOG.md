# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-08-22

### Added

- Initial UPM release: combined Player BuildReport + Addressables Build Layout analyzer.
- Editor window at **Window → Analysis → Full Build Size Report**.
- Pagination (10 / 20 / 50), CSV export, source filters, large-asset filter.
- Hierarchy reference finder (scenes / prefabs) and asset-level references from Addressables layout.
- Optional Addressables integration via `USE_ADDRESSABLES` (`com.unity.addressables` version define).
- Editor assembly definitions for core + optional Addressables module.

[1.0.0]: https://github.com/makarGames/Unity-Advanced-Build-Report/releases/tag/v1.0.0
