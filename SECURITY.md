# Security

## Reporting a vulnerability

Please report it privately, through GitHub's **Report a vulnerability** on the repository's
Security tab, not as a public issue. Reports are answered there.

Fixes go into the latest release only.

## Untrusted data

The serializer records the type of what it writes, and reading creates the type the bytes name.
Two settings on `Encoder.Configuration` keep that from being turned against the application, and
both are safe by default:

- **`StrictTypes`, on by default.** Reading creates only declared types - the type asked for,
  entity types and types with a registered binding, and what their members, elements and type
  arguments declare - the framework's value and collection types, and types added with
  `AllowType` or `AllowAssembly`. Anything else is refused before any of its code runs; generic
  arguments, array elements and `Type` values are checked as well. Switched off, reading creates
  whatever type the bytes name, and only data the application wrote itself should be read.
- **`EnableSerializationCallbacks`, off by default.** The methods a type marks `[OnSerializing]`,
  `[OnSerialized]`, `[OnDeserializing]` or `[OnDeserialized]`, and `IDeserializationCallback`, are
  not called, so reading runs nothing of a type beyond its constructor and its setters - and with
  strict types, of declared and allowed types only.

A way to make strict reading create a type that was neither declared nor allowed, or to make
reading call a callback while they are off, is a vulnerability.

Always on, whatever the settings:

- **Nesting depth.** Input nested too deeply for the stack is refused with a
  `SerializationException`; it used to overflow the stack, which ends the process. Values nested
  too deeply to write are refused the same way.
- **Counts.** A count of items, array lengths, string lengths included, larger than the bytes
  left in the input is refused before anything is allocated - every item takes at least one byte.
  This needs the length of the input: it holds for a byte array and for a seekable stream.
- **Type names.** Under strict types a stored name resolves only against assemblies the process
  has already loaded - it never loads one, whose code would run before anything could be refused.
  A name that does not resolve is not cached, so distinct names cannot grow memory.
- **Member names.** A name `Resolver.PropertyNameResolver` maps is kept for the one stream it
  came in, not recorded on the type.
- **Regular expressions in a scan.** A row, value or qualifier pattern is matched by .NET's
  backtracking engine, so one match may take two seconds at most - or what the process sets as
  `REGEX_DEFAULT_MATCH_TIMEOUT` - after which the scan fails with a `TimeoutException`. A
  pattern taken from an untrusted source can still cost that much per cell; bound the scan.

Known limit - read untrusted data with it in mind:

- **A stream that cannot seek** has no length to check counts against. Read untrusted data from a
  byte array or a seekable stream.
