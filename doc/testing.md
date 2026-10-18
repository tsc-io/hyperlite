# The test suites

What is where, and how the suites are run.

## The suites

| suite | what it covers | needs a database |
|---|---|---|
| `Hyperlite.Native.Test` | the native dispatch table, in C++ | yes |
| `Hyperlite.Test` | the client: contexts, namespaces, tables, keys, mutators, scanners | yes |
| `Hyperlite.Persistence.Test` | the object mapper, and the serializer under it | yes |
| `Hyperlite.Serialization.Test` | the serializer alone | no |

The last two share their serialization fixtures. `Hyperlite.Serialization.Test` compiles the
same source files against `Hyperlite.Serialization` instead of `Hyperlite.Persistence`, the
way the two libraries share their sources — so a serialization fixture is listed by hand in
`Hyperlite.Serialization.Test.csproj` and a new one has to be added there too. Nothing
enforces that; a fixture left out simply never runs against the standalone assembly.

`Hyperlite.Smoke` is not a suite. It is an executable that exercises the api once on each
framework, so that a packaging or loader problem is caught before the suites are reached.

Run everything with `build.cmd` then `test.cmd`. Nothing in `test.cmd` builds — it passes
`--no-build`, because `dotnet test` would otherwise try to build the solution's C++
projects, which it cannot do.

On Linux, `build-linux.sh test` runs the native test, the smoke harness and the three suites
on `net8.0` and `net10.0`, building as it goes - there are no C++ projects in the way there.
`test.cmd` runs it in WSL when there is a distribution, and the build workflow's `linux` job
on every pull request. The few tests that need the operating system - a hard link, a byte
range lock on the database file from outside sqlite - do it each platform's way: on Linux
sqlite's locks are POSIX record locks, which never conflict within one process, so the lock
is an open file description lock, and the native test reads `/proc/locks`.
