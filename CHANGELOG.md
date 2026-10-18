# Changelog

## 2026.10.0 - unreleased

The first release.

- `Hyperlite` - an embedded, sorted, versioned cell store on SQLite: namespaces, tables,
  four-part keys, mutators and scanners, with the native half as one library that needs
  nothing installed.
- `Hyperlite.Persistence` - an object mapper on top: entities, bindings, references followed
  and read back with their identity intact. An entity manager is thread safe.
- `Hyperlite.Serialization` - the serializer on its own.
- Safe by default: `Encoder.Configuration.StrictTypes` is on - reading creates only declared
  and allowed types - and `EnableSerializationCallbacks` is off.
- An SPDX 2.2 SBOM in every package, sqlite included.
- `Hyperlite.Explorer`, the database browser, as a zip beside the packages.
- `net48`, `net8.0`, `net10.0` and `netstandard2.0`, Windows x64; Linux x64 from .NET 8 on,
  glibc 2.35 or newer; `Hyperlite.Serialization` for `netstandard2.0`.
