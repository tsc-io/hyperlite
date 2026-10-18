# Building

One solution, one restore, one build. `build.cmd` and `pack.cmd` are thin wrappers over
msbuild and `test.cmd` over `dotnet test`; there is no buildproj and no `make-*` script, and
nothing outside this repository is needed.

## Why msbuild and not dotnet build

`src\Hyperlite.Native` and `tests\Hyperlite.Native.Test` are C++ projects, and the dotnet cli
cannot build a `.vcxproj`. So the solution is built by msbuild, which handles both
languages. Everything else in the repository is plain SDK behaviour, and the parts of the
dotnet cli that do not build - `dotnet test --no-build`, `dotnet pack` metadata - are used
as they come.

## Where things are defined

| file | what it decides |
|---|---|
| `Version.props` | the version and copyright, one literal for the whole repository |
| `Directory.Build.props` | output layout, the framework set, shared package metadata |
| `Directory.Packages.props` | every package version, centrally |
| `Hyperlite.sln` | which projects exist and how they group |

`Directory.Build.props` is imported into every project, C# and C++ alike, before the
project body is read. Anything repeated in more than one project belongs there; anything
one project needs stays in that project.

### The framework set

`Directory.Build.props` holds three lists, because shipping and testing want different
things, and the explorer wants the desktop frameworks:

```
HyperliteLibraryTargetFrameworks   net48;net8.0;net10.0;netstandard2.0
HyperliteTargetFrameworks          net48;net7.0;net8.0;net10.0
HyperliteWindowsTargetFrameworks   net48;net8.0-windows;net10.0-windows
```

The third is the explorer's: the frameworks the libraries ship for, in the `-windows` form a
WPF application needs from net5 onwards.

The first is what the two libraries build and pack. The conditional code divides at net8.0
and nowhere else: `NET8_0_OR_GREATER` gives the flat tables their SIMD group probe, the
serializer's string map a non-randomized string hash, and `IsValueType` its uncached form,
and `NET5_0_OR_GREATER` adds `IReadOnlySet` to `FlatHashSet`. Nothing tells net8.0 and
net10.0 apart, so those two compile the same code and differ only in the framework they
declare. net48 earns its place because its conditional code is a measured win for its own
runtime, `Utf8`'s hand written ascii widening. netstandard2.0
earns its place because of who cannot use the others: without it a net472 project fails
outright with NU1202, and a net6.0 project restores only by falling back onto the net48
assembly with NU1701, quietly running a .NET Framework build on .NET 6.

The second is what the suites and the smoke test run on. A test leg is a runtime to
execute on, not an assembly to ship, and there is now exactly one leg per shipped
assembly:

| leg | resolves |
| --- | --- |
| net48 | the net48 assembly, and its hand written ascii decode |
| net7.0 | the netstandard2.0 assembly |
| net8.0 | the net8.0 assembly |
| net10.0 | the net10.0 assembly |

What no leg covers is an assembly running on a runtime newer than the one it targets - a
net9.0 consumer resolving the net8.0 assembly, say. That is ordinary .NET forward
compatibility rather than anything this code does.

**net7.0 is a compatibility leg, not a supported target.** It is the newest runtime that
cannot resolve the net8.0 assembly, so it resolves netstandard2.0 - the only way to
execute that assembly from here. Its interop differs: native callbacks go through
`Marshal.GetFunctionPointerForDelegate` rather than function pointers, and on CoreCLR
nothing else covers that combination. net48 covers delegate marshalling on .NET Framework
and the other two cover function pointers on CoreCLR, so without this leg the assembly
shipped to net472 and net5-net7 consumers would never be run. `CheckEolTargetFramework`
is off for it, because the warning is right about net7.0 as somewhere to ship and beside
the point for a leg chosen precisely for being old.

That mapping is worth re-checking after any change to either list - compare the hash of
`Hyperlite.dll` under each `dist\<configuration>\<tfm>\<suite>\` against the ones under
`dist\<configuration>\<tfm>\`, and every shipped assembly should appear at least once.

The framework list in `test.cmd` has to match `HyperliteTargetFrameworks` and nothing
enforces it. A framework built but not listed in the script is simply never run, silently.

**`test.cmd` and `pack.cmd` refuse to run on a failed build.** Both work on whatever is in
`dist`, so after a build that failed they would test or pack the previous build's binaries -
a green test run of code that is no longer the source. `build.cmd` removes
`build\<configuration>\last-build.ok` before it starts and writes it only once everything has
built, and both scripts stop with an error when it is missing. Building from Visual Studio does
not write it; run `build.cmd`.

The benchmark takes net48, net8.0 and net10.0: the same cases on several runtimes is how a
difference gets attributed to the runtime rather than the library, net48 is the leg that
can be compared against the predecessor, which ran nowhere else, and net10.0 is the newest
shipping framework.

This replaced a `DotnetFramework` property that selected one framework per build, with the
build repeated per framework and the output of the last one winning if the property was
forgotten. Now one build produces the whole matrix.

### Output layout

```
dist\<configuration>\<tfm>\                      every deliverable for that framework
dist\<configuration>\<tfm>\<suite>\              the test assemblies
dist\<configuration>\*.nupkg                    the nuget packages
dist\<configuration>\runtimes\win-x64\native\   the native companion
dist\<configuration>\runtimes\linux-x64\native\ the Linux one, from build-linux.sh
build\<project>\                                intermediate output
build\<configuration>\<tfm>\TestResults\         test results
build\<configuration>\scratch\                   working directory for the native test,
                                                 the smoke harness and, under <tfm>\<suite>\,
                                                 the suites
build\sqlite-amalgamation\                      scratch, while the amalgamation unpacks
```

`dist` holds build output and nothing else. Test results and the databases a run leaves
behind both used to land there - the databases because `test.cmd` ran the executables with
a framework's output directory as the working directory, so a `chain.db` appeared among
the binaries. The executables now run from a scratch directory under `build`, by absolute
path: Windows and the managed loader both probe the executable's own directory, so nothing
has to be copied for that to work. The two suites that write databases, which `dotnet test`
starts in their output directory, move to the same scratch directory themselves: `test.cmd`
names it in `HYPERLITE_TEST_SCRATCH`, and their `TestBase` switches to it before the first
test. `Hyperlite.Serialization.Test` writes no files. Run from Visual Studio, where the
variable is not set, the two still write beside the binaries.

One directory per framework rather than one per project, so everything built for a
framework sits together and reading the output does not require knowing which project
produced what.

The suites are the exception and keep a directory each, and not for tidiness. All three
emit `testhost.dll.config`, a fixed filename carrying their app settings, and
`Hyperlite.Test`'s differs from the other two - it connects through the legacy `Ht4n.`
prefixed key on purpose, as the only live coverage of that alias. Sharing a directory
would leave whichever suite built last holding the file, and the loser would run green
against the wrong connection string rather than fail.

That subdirectory needs a segment *after* the framework, so the suites place the framework
themselves and switch off `AppendTargetFrameworkToOutputPath`. **That property also
governs `IntermediateOutputPath`**, so each of them puts the framework back there by hand.
Switching it off repo-wide instead puts every framework of a project into one `obj`
directory, where the reference assemblies under `obj\ref` overwrite each other and the
build fails with CS0006 on whichever project loses the race.

The native companion lands in the shape a package expects, `runtimes\<rid>\native`, so
packing it is a copy rather than a rearrangement.

### The sqlite amalgamation

`deps/sqlite` holds the zip sqlite.org publishes -
`sqlite-amalgamation-<version>.zip` - and not the extracted source. 3 MB rather than 10,
and the version is in the file name rather than something to infer from a diff.

Three targets in `Hyperlite.Native.vcxproj` handle it.

`SelectSqliteAmalgamation` reads the version out of every
`sqlite-amalgamation-<version>.zip` present, keeps the highest and deletes the rest, so
**upgrading is dropping the new zip in and building** - the old one is pruned for you, and
`UpdateSqliteSbom`, below, rewrites the SBOM for the new version. Commit both.

`SelectSqliteAmalgamation` has no `Inputs` or `Outputs` and so runs every build. That is not
incidental: a target with them is skipped whole when they say it is up to date, and an earlier
version of this put the archive check inside that gate, where a second zip was ignored in
silence until something else forced an unpack. The comparison is numeric - sqlite writes
3.53.1 as 3530100 - which needs an inline task, because msbuild cannot take the maximum of an
item list.

`UnpackSqliteAmalgamation` extracts `sqlite3.c` and `sqlite3.h` into `deps/sqlite` when
they are missing, older than the zip, or came from a different version. That last one is
why its `Outputs` include a stamp named after the version rather than relying on dates: a
zip downloaded from sqlite.org can carry a date *older* than the source already extracted
from the previous one, and then nothing looks stale and the build quietly keeps compiling
the old amalgamation. Both extracted files are gitignored.

`UpdateSqliteSbom` rewrites `sqlite.spdx.json` when it names another archive than the one
in use, taking the version and the release year - the download location's directory - from
the unpacked `sqlite3.h`, and failing if that disagrees with the archive's name. It warns
when it does, since the rewritten file is source; one that already names the archive is
left alone, so a clean clone rewrites nothing.

The archive nests its contents under a version-named directory and the include path
expects them flat, hence a copy up one level, and the extracted files are touched
afterwards - they carry the dates the archive recorded, older than the zip itself, so
without that the unpack would look stale on every build.

`shell.c` and `sqlite3ext.h` are in the archive and are not extracted: the first is
sqlite's command line tool, the second is for loadable extensions, and the amalgamation is
linked statically with neither.

## How the native companion travels

`Hyperlite.csproj` declares it once, as a **`Content`** item with `CopyToOutputDirectory`, and
that single item does three jobs:

1. copies it next to `Hyperlite.dll`
2. copies it next to every project that references Hyperlite - `Hyperlite.Persistence`, the
   client and the persistence suites, the smoke harness, the explorer, the benchmark - because
   `Content` flows through a `ProjectReference` and `None` does not
3. carries it into the package under `runtimes\<rid>\native`

Hyperlite's loader probes the directory the assembly sits in, so (2) is what makes every test
and tool work with nothing said in its own project file.

The C runtime is linked in (`/MT`), so the companion depends on nothing beyond Windows itself
and a consumer needs no Visual C++ redistributable. Nothing crosses the boundary to be freed
by another runtime: native blocks go back through `mem_free`.

`Directory.Build.props` names the companion after the machine building - the dll on Windows,
`libHyperlite.Native.linux-x64.so` on Linux - and `Hyperlite.csproj` packs the Linux one beside
the dll when it is in `dist`. Packing without it is a warning locally and an error on GitHub.

Build order comes from a `ProjectReference` to the vcxproj with `ReferenceOutputAssembly`
false, on Windows only - on Linux `build-linux.sh` runs cmake before anything managed is
built. Three pieces of metadata on it are not optional:

- `SkipGetTargetFrameworkProperties` - a vcxproj has no framework to negotiate and being
  asked for one is an error
- `SetPlatform=Platform=x64` - Hyperlite builds as AnyCPU with `PlatformTarget` x64, the
  vcxproj has an x64 configuration only
- `UndefineProperties=TargetFramework` - without it each of the four inner builds asks for
  the vcxproj under a different `TargetFramework`, msbuild treats those as four different
  projects, and four `cl.exe` race to write one pdb: `fatal error C1041`

## Linux

`src\Hyperlite.Native\CMakeLists.txt` builds `libHyperlite.Native.linux-x64.so` with gcc and
the native test beside it. The vcxproj stays the reference: the sources and `SqliteDefines` are
read out of it, so the two builds cannot configure sqlite differently - its configuration is
part of the on-disk format. The amalgamation unpacks into the cmake build tree, not
`deps\sqlite`, so a checkout shared with Windows keeps the two apart.

Like the dll, the library needs nothing installed: libstdc++ and libgcc are linked in
statically and their symbols hidden, and the only export is the entry point, by name -
Linux has no ordinals. Built on Ubuntu 22.04 it needs glibc 2.35, and `build-linux.sh` fails
if a symbol asks for anything newer. The debug information goes into
`libHyperlite.Native.linux-x64.so.debug`, which the release carries beside the pdb.

`build-linux.sh` builds and, with `test`, runs the native test, the smoke harness and the
three suites on net8.0 and net10.0. From a Windows checkout - `build.cmd` and `test.cmd` call
it through `wsl.exe` when a distribution is installed - it copies the sources to
`~/.cache/hyperlite` first and the library back afterwards: the Windows file system is slow
from WSL, and its locks are not the POSIX locks the native test looks for. On GitHub the
`linux` job of the build workflow runs it on ubuntu-22.04, and the Windows job packs what it
built.

## A quirk worth knowing

**The output directories are keyed on the project's directory name, not
`$(MSBuildProjectName)`.** WPF compiles markup in a second pass, through a temporary
project it generates next to the real one - `Hyperlite.Explorer_a1b2c3d4_wpftmp.csproj` - and
that project re-reads `Directory.Build.props`. Keyed on the project name it would get
intermediate directories of its own, with no restore output in them, and markup
compilation would fail on every reference that came from a package.

## The version

`Version.props` holds one literal. From it come `AssemblyVersion` and `FileVersion`, the
package version, and - for the native half - `VersionComma` and `CopyrightEscaped`, which
the `GenerateVersionHeader` target in `Hyperlite.Native.vcxproj` writes into a generated
`Hyperlite.version.h` that the `.rc` includes. So the version resource in the native dll and
the managed assembly attributes cannot disagree.

A month written with a leading zero loses it in the assembly version - 2026.09 arrives as
2026.9 - because that field is a number. `VersionComma` goes through `System.Version` for
the same reason: `rc.exe` would read a leading zero as octal.

**The assembly version is the year alone**, `2026.0.0.0` for `2026.10.0.0`. The assemblies
are strong named, and on .NET Framework the assembly version is part of a strong name: moved
with every monthly release, each update would need a binding redirect in every application
that takes a new Hyperlite without rebuilding what references it - an application that
updates Hyperlite under an unchanged Hyperlite.Persistence, say. The file version and the
package version carry the release; the trace line `EntityManagerFactory` writes and the
benchmark header report the file version for that reason.

## Signing

One key, `src\Hyperlite.snk`, named once in `Directory.Build.props`. The four shipping
assemblies - `Hyperlite`, `Hyperlite.Persistence`, `Hyperlite.Serialization` and
`Hyperlite.Explorer` - opt in with `SignAssembly`. Nothing else is signed.

It replaced the predecessors' keys: `ht4n2.snk`, `ht4o.snk` and `ht4n.snk`, two of them
duplicated under `tests\`, each named by an obsolete `AssemblyKeyFile` attribute in an
`AssemblyInfo.cs` with warning 1699 suppressed around it. Those attributes are gone.

Two deliberate changes came with it:

**Fully signed, not delay signed.** `ht4n2` was delay signed to line its identity up with
ht4n's, and neither ht4n nor that identity is in this repository. A delay signed assembly
also needs verification skipped on every machine that loads it, which this one does not.

**The test assemblies are not signed at all.** Nothing references them and there is no
`InternalsVisibleTo` anywhere here, so their strong names bought nothing.

Renaming the assemblies changed their identity regardless, which is why this was the
moment to do it: a consumer binding to the old strong name has to be rebuilt anyway.
`sn -vf` verifies any of the four.
