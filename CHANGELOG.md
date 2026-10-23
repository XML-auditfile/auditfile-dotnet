# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Streaming reader for XAF 3.2: version resolution (forced, registry, alias, resolver), scan and load phases, XSD validation with the built-in XSD.
- Table mapping from the generated manifest, typed values, ids, parents, ordinals and source parts.
- Version constants for every registry entry: `Financial`, `CashRegister`, `TaxiOnboardComputer`, `TripRegistration`, `Payroll`.
- Stream-only sources: `AuditfileSource.FromStream`, `FromStreams`, `FromFactory`, `FromFactories`. The reader never touches the file system.
- Split audit files, dialect corrections, key and reference checks, XAF business rules.
- `ISink`, `IAuditfileSource`, `AuditfileSchema` with JSON round-trip.
- `MemorySink` in the separate package `XmlAuditfile.Auditfile.Memory` (namespace `XmlAuditfile.Auditfile.Memory`).
- Native AOT compatible on net10.0; no package dependencies.
- Views from auditfile-spec (`AuditfileVersion.Views`, `ViewDefinition.Run`, `MemorySink.Run`): trial balance, ledger entries and more, with parameters and diagnostics XAF-Q001/Q002. The ReadToMemory sample prints them.
