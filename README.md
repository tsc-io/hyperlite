Hyperlite
=========

An embedded, sorted, versioned cell store for .NET, with an object mapper on top.

Data is addressed by a four-part key — row, column family, column qualifier, timestamp —
kept in sorted order, and every write leaves a new revision rather than replacing the old
one. Scans read ranges of that key space: a row interval, a cell interval, one column
family, qualifiers matching a prefix or a regular expression, the last *n* revisions, a
timestamp window. On top of it sits a mapper that persists object graphs, follows
references, and reads them back with their identity intact.

Three assemblies and one native companion:

| | | |
|---|---|---|
| `Hyperlite` | the client library | cells, keys, scanners, mutators, namespaces, tables |
| `Hyperlite.Native` | its native half | sqlite, linked in statically, reached by p/invoke |
| `Hyperlite.Persistence` | the object mapper | entities, bindings, queries over the same api |
| `Hyperlite.Serialization` | the serializer alone | the format without the persistence layer |

`Hyperlite.Explorer` is a WPF front end for looking at a database. It is not a package but a
plain zip, `Hyperlite.Explorer.<version>.zip`, with one folder per framework.

SQLite is the storage engine, and the only one. Nothing runs as a server and nothing has to
be installed: a database is a file, opened in process.

### Getting started

        dotnet add package Hyperlite

or `Hyperlite.Persistence` for the object mapper, which brings `Hyperlite` along. It runs on
Windows x64 and on Linux x64 with glibc 2.35 or newer - Ubuntu 22.04, Debian 12 - and the
project has to build for x64 (`<PlatformTarget>x64</PlatformTarget>`), because the native half
does.

Cells - a table, a mutator to write, a scanner to read:

```csharp
using System;
using System.Text;
using Hyperlite;

using (var context = Context.Create("Provider=SQLite; Uri=file://example.db"))
using (var client = context.CreateClient())
{
    client.CreateNamespace("demo", CreateDispositions.CreateIfNotExist);
    using (var ns = client.OpenNamespace("demo"))
    {
        ns.CreateTable(
            "people",
            "<Schema><AccessGroup name=\"default\"><ColumnFamily><Name>info</Name></ColumnFamily></AccessGroup></Schema>",
            CreateDispositions.CreateIfNotExist);

        using (var table = ns.OpenTable("people"))
        {
            using (var mutator = table.CreateMutator())
            {
                mutator.Set(new Key("alice", "info", "email"), Encoding.UTF8.GetBytes("alice@example.org"));
                mutator.Set(new Key("bob", "info", "email"), Encoding.UTF8.GetBytes("bob@example.org"));
            }

            using (var scanner = table.CreateScanner(new ScanSpec().AddColumn("info:email")))
            {
                foreach (var cell in scanner)
                {
                    Console.WriteLine($"{cell.Key.Row} {Encoding.UTF8.GetString(cell.Value)}");
                }
            }
        }
    }
}
```

Objects - an entity class, persisted and found again by its id:

```csharp
using System;
using Hyperlite;
using Hyperlite.Persistence;
using Hyperlite.Persistence.Attributes;

EntityManagerFactory.DefaultConfiguration.RootNamespace = "demo";
using (var factory = EntityManagerFactory.CreateEntityManagerFactory("Provider=SQLite; Uri=file://example.db"))
{
    factory.RootNamespace.CreateTable(
        "customers",
        "<Schema><AccessGroup name=\"default\"><ColumnFamily><Name>c</Name></ColumnFamily></AccessGroup></Schema>",
        CreateDispositions.CreateIfNotExist);

    Guid id;
    using (var em = factory.CreateEntityManager())
    {
        id = em.Persist(new Customer { Name = "Contoso" }).Id;
    }

    using (var em = factory.CreateEntityManager())
    {
        Console.WriteLine(em.Find<Customer>(id).Name);
    }
}

[Entity("customers", ColumnFamily = "c")]
public class Customer
{
    [Id]
    public Guid Id { get; private set; }

    public string Name { get; set; }
}
```

The id is assigned on persist. An entity manager writes an entity once: under the default
`Behaviors.CreateLazy` a later `Persist` of one it has fetched or written already is skipped,
and one it does not know gets a new id. A change to an entity goes in with `Merge`, or with
`Persist(entity, Behaviors.CreateNew)` - for that entity: one it refers to that this manager
has written already is not written again.

The same entity without attributes - mapped in code, and stored under a type code instead of its
type name. More to set up, but the more compact and faster form, for types written and read
often:

```csharp
using System;
using Hyperlite;
using Hyperlite.Persistence;
using Hyperlite.Persistence.Bindings;
using Hyperlite.Persistence.Serialization;

// once, before the first entity manager, and the same wherever the data is read
var binding = EntityManagerFactory.DefaultConfiguration.Binding;
binding.RegisterTableBinding(typeof(Customer), new CustomerTable());
binding.RegisterColumnBinding(typeof(Customer), new ColumnBinding("c"));
binding.RegisterKeyBinding(typeof(Customer), new KeyBinding<Customer>(c => c.Id, generateKey: true));

Encoder.Register(1, typeof(Customer));

EntityManagerFactory.DefaultConfiguration.RootNamespace = "demo";
using (var factory = EntityManagerFactory.CreateEntityManagerFactory("Provider=SQLite; Uri=file://example.db"))
{
    factory.RootNamespace.CreateTable(
        "customers",
        "<Schema><AccessGroup name=\"default\"><ColumnFamily><Name>c</Name></ColumnFamily></AccessGroup></Schema>",
        CreateDispositions.CreateIfNotExist);

    string id;
    using (var em = factory.CreateEntityManager())
    {
        id = em.Persist(new Customer { Name = "Contoso" }).Id;
    }

    using (var em = factory.CreateEntityManager())
    {
        Console.WriteLine(em.Find<Customer>(id).Name);
    }
}

public class Customer
{
    public string Id { get; private set; }

    public string Name { get; set; }
}

public sealed class CustomerTable : ITableBinding
{
    // null is the factory's root namespace
    public string Namespace => null;

    public string TableName => "customers";
}
```

- The id is the row key: the key binding generates it on persist and sets it again on read, and
  it is not written into the value. It is a string here - the binding for a `Guid` id is the
  attribute's.
- The stored value drops from 64 bytes to 21: the code takes the place of the type's
  assembly-qualified name, and reading it needs no name resolution.
- A type code is part of the format: never reuse one for another type, and register it wherever
  the data is read. `doc\serialization.md` has the rest, a serializer of your own among it.

The suites under `tests` show the rest of the api.

### Frameworks

| | |
|---|---|
| the libraries ship for | `net48`, `net8.0`, `net10.0`, `netstandard2.0`; `Hyperlite.Serialization` for `netstandard2.0` alone |
| the suites run on | `net48`, `net7.0`, `net8.0`, `net10.0` |

Windows x64 and Linux x64, because the native half is; Linux from .NET 8 on. `netstandard2.0` is there so that consumers on
`net461` through `net472`, and on `net5.0` through `net7.0`, resolve a real assembly rather
than failing to resolve one or silently taking the `net48` build.

### Requirements

- Visual Studio 2026, or its Build Tools, with the C++ workload and, for `format.cmd`, the
  C++ Clang tools. The C++ projects ask for the v143 toolset: the individual component
  "MSVC v143 - VS 2022 C++ x64/x86 build tools", or a Visual Studio 2022 beside it. Another
  toolset works when set in the environment before the scripts run, `set PlatformToolset=v145`.
  Visual Studio 2022 does not do: its msbuild does not support `net10.0`, and its
  clang-format is older than `.clang-format` needs.
- The .NET 10 SDK, which `global.json` asks for, the .NET 8 and .NET 7 runtimes the test
  legs run on, and the .NET Framework 4.8 developer pack.
- For the Linux library, a WSL distribution - Ubuntu 22.04, whose glibc is the oldest it is
  built for - with gcc, cmake, rsync, the .NET 10 SDK and the .NET 8 runtime; see
  `build-linux.sh`. Without one the scripts build and test for Windows only, and the package
  `pack.cmd` makes is Windows only too, with a warning.

Nothing else. sqlite is vendored under `deps` as its amalgamation archive, which the build
unpacks, so there is no sibling repository to clone and nothing to restore for the native
half.

### Build, test, pack

        build.cmd            all projects, all frameworks, Release; the Linux library in WSL
        test.cmd             the native test, the smoke harness, then the three suites; in WSL
                             the same on net8.0 and net10.0
        pack.cmd             the three packages and the explorer zip, into dist\<configuration>
        build-linux.sh       the Linux half alone, in WSL or on Linux

Each takes `Debug` as an optional argument. Or open `Hyperlite.sln`.

`build.cmd` drives msbuild rather than `dotnet build`, and that is not a preference: the
solution contains C++ projects, which the dotnet cli cannot build. Everything else is
ordinary SDK behaviour — one restore, the framework matrix from one property, `dotnet pack`
metadata in the project files.

`test.cmd` passes `--no-build` for the same reason, so `build.cmd` has to have run first -
and to have succeeded: it writes `build\<configuration>\last-build.ok` only once everything
has built, and `test.cmd` and `pack.cmd` refuse to run without it.

### Layout

        src\                 the shipping libraries, the native half, the explorer
        tests\               the suites, the smoke harness, the native test
        bench\               the benchmark
        deps\                vendored sqlite
        doc\                 build, packaging, testing and serialization notes
        build\               intermediate output
        dist\                build output and packages

`build` and `dist` hold everything the build writes, apart from the sqlite sources unpacked
into `deps\sqlite` and the generated `src\Hyperlite.Native\Hyperlite.version.h`. All of it
is ignored by git, and deleting `build` or `dist` is safe.

### Tests

| | | |
|---|---|---|
| the native dispatch table | `tests\Hyperlite.Native.Test` | C++, no managed code involved |
| the managed chain | `tests\Hyperlite.Smoke` | an executable, no test host |
| the client suite | `tests\Hyperlite.Test` | contexts, namespaces, tables, keys, mutators, scanners |
| the persistence suite | `tests\Hyperlite.Persistence.Test` | entities, bindings, queries, the serializer |
| the serializer alone | `tests\Hyperlite.Serialization.Test` | the same serialization fixtures, against the standalone assembly |

They run in that order because it is the order in which a failure is easiest to place: a
break in the first says the storage engine, a break only in the last says the api surface.

The serialized form is pinned byte for byte. `TestFormatCompatibility` holds a corpus of
266 values and a recorded hex form for each of the portable ones, so any change to the
serializer that moves the format fails there and nowhere else. See `doc\testing.md` for how
the suites are laid out.

### Documentation

| | |
|---|---|
| `doc\build.md` | how the build fits together |
| `doc\nuget.md` | what the packages contain and why |
| `doc\testing.md` | the suites and how they are run |
| `doc\serialization.md` | the serialized form, changes to a class, type codes, serializers of your own |

`CHANGELOG.md` and `SECURITY.md` are at the root. The code style is set by `.editorconfig`
and `.clang-format`, and `format.cmd` applies it - `format.cmd check` changes nothing and fails
on anything off.

### Security

The serializer records the type of what it writes, and reading creates the type the bytes name.
So that bytes from an untrusted source cannot instantiate types you did not intend, two settings
on `Encoder.Configuration` are safe by default:

- **`StrictTypes`, on.** Reading creates only what the application declared - the type asked
  for, entity types and bound types, and what their members, elements and type arguments
  declare - the framework's value and collection types, and what was allowed. Anything else is
  refused with a `SerializationException` before any of its code runs. A value stored behind a
  member typed `object`, an interface or a base class needs its type allowed:

        Encoder.Configuration.AllowType(typeof(Discount));

- **`EnableSerializationCallbacks`, off.** Methods marked `[OnSerializing]`, `[OnSerialized]`,
  `[OnDeserializing]` or `[OnDeserialized]`, and `IDeserializationCallback`, are not called, so
  reading runs nothing of a type beyond its constructor and setters. A type that restores
  something in a callback needs them:

        Encoder.Configuration.EnableSerializationCallbacks = true;

Whatever the settings, input nested too deeply for the stack, or stating more items than it
holds, is refused with a `SerializationException` before the damage. Neither setting changes the
format. `SECURITY.md` has the details and what is known to be left.

### Licence

MIT. See `LICENSE.md`.
