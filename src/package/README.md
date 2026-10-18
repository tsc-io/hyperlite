# Hyperlite

An embedded, sorted, versioned cell store for .NET, on SQLite. Windows x64, and Linux x64
with glibc 2.35 or newer.

- `Hyperlite` - the store: tables, keys, mutators, scanners
- `Hyperlite.Persistence` - an object mapper on top
- `Hyperlite.Serialization` - its serializer alone; not together with `Hyperlite.Persistence`, which contains it

The consuming project builds for x64, `<PlatformTarget>x64</PlatformTarget>`.

Reading creates only declared and allowed types, and calls no serialization callbacks,
unless `Encoder.Configuration` says otherwise.

Getting started: https://github.com/tsc-io/hyperlite#getting-started

Source, documentation and the MIT license: https://github.com/tsc-io/hyperlite
