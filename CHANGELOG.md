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
- `ISink`, `AuditfileSchema` with JSON round-trip (an unknown `formatVersion`, column type or source is refused).
- Packages `XmlAuditfile` (namespace `XmlAuditfile`) and `XmlAuditfile.Memory` with `MemorySink`. Types used every day (`TextLanguage`, `ColumnType`) are in the root namespace; the rest of the definitions in `XmlAuditfile.Definitions`.
- Typed row accessors: `GetString`, `GetDecimal`, `GetInteger`, `GetDate`, `GetDateTime`, `GetTime`, `GetBoolean`, `TryGetValue`, also with a `SchemaColumn` looked up once.
- Report counts per severity that include diagnostics left out of a full list (`HasErrors`, `HasFatal`, `IsTruncated`); errors take the place of infos and warnings.
- Cancellation in the scan phase; `Scan` and `Open` take a `CancellationToken`.
- XAF-S006 (missing required field) in unverified mode; XAF-X004 for a part of a split file in another encoding than the first part.
- Native AOT compatible on net10.0; no package dependencies.
- Views from auditfile-spec (`AuditfileVersion.Views`, `ViewDefinition.Run`, `MemorySink.Run`): trial balance, ledger entries and more, with parameters and diagnostics XAF-Q001/Q002.
- Views: joins with `alternatives` (`ViewJoin.Alternatives`) and the `pseudonym` operation (`ViewOperator.Pseudonym`,
  `ViewValue.Prefix`); the views `ledger` and `ledger_pseudonymized`.
