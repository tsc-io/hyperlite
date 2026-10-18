# NuGet packaging

Three packages, packed by the SDK's own pack target from the project files. There is no
`nuget` directory, no nuspec template and no `NuGet.exe`: the metadata lives in the
projects next to the code it describes, and `pack.cmd` runs `msbuild -t:Pack` over the
solution - msbuild rather than `dotnet pack`, for the reason `build.cmd` gives - and then
zips the explorer.

| package | contents |
|---|---|
| `Hyperlite` | `lib\{net48,net8.0,net10.0,netstandard2.0}`, `runtimes\win-x64\native`, `runtimes\linux-x64\native`, `build\<tfm>\Hyperlite.targets`, `buildTransitive\<tfm>\Hyperlite.targets` |
| `Hyperlite.Persistence` | `lib\{net48,net8.0,net10.0,netstandard2.0}`, dependency on `Hyperlite` |
| `Hyperlite.Serialization` | `lib\netstandard2.0` |

Each also carries the icon, the readme from `src\package` and an SBOM, see the last section.

## Running it

Build the whole framework matrix first, then pack once:

```
build.cmd
pack.cmd
```

`pack.cmd` passes `-p:NoBuild=true`, so the packages contain exactly the binaries that
were built and tested rather than a fresh compile of possibly-changed sources. It deletes
the previous packages first: nuget packs only when an input is newer than the package, and a
file that has gone is not newer than anything.

The Linux library comes from `build-linux.sh`, which `build.cmd` runs in WSL when there is a
distribution. Without it the package is Windows only, and `pack.cmd` warns; on GitHub, which
publishes, that is an error, and the build workflow's `linux` job provides the library.

Packing after building a single framework would produce a package claiming support for
that one framework alone - the id and the version do not vary by framework, so each pack
overwrites the last. `build.cmd` builds all four in one pass, which removes the trap
rather than documenting it.

## What the project files decide

Shared metadata - authors, company, the project and repository url, the license
expression, the icon and the readme - sits in `Directory.Build.props`, because all three
packages carry the same values. Per package values sit in the project: `PackageId`,
`Title`, `PackageDescription` and `PackageTags`, nothing else.

`IsPackable` is explicit on all three. Everything else - the explorer, the suites, the
benchmark - sets it false.

The explorer travels as a plain zip instead, `Hyperlite.Explorer.<version>.zip` beside the
packages, written by `pack.cmd` through the `PackZip` target: each framework published into
its own folder, so the zip holds exactly the explorer's files and no other tool's.

## Dependencies are derived, not written

`Hyperlite.Persistence` reaches Hyperlite through a `ProjectReference`, so pack writes the
dependency on `Hyperlite` itself, per target framework, at the version being built. Package
references come out the same way.

This is the part most worth having changed. The old packaging held the dependency by hand
in a nuspec template, filled in from a property that a plain buildproj did not import -
which is how a package once shipped with its dependency recorded as `.x64`, the id
missing and only the suffix left.

## The native companion

`Hyperlite.csproj` declares `Hyperlite.Native.win-x64.dll` once, as a `Content` item, and that
item both copies the file around the build and packs it to `runtimes\win-x64\native`. The
Linux library, built elsewhere, is a `None` item packed to `runtimes\linux-x64\native`. See
`build.md`.

`Hyperlite.targets` ships alongside it and copies the Windows companion next to the consumer's
output; on Linux it does nothing, there `runtimes\` is always honoured. On Windows it is not
redundant: `runtimes\` is not always honoured. A packages.config
project - still a real possibility on net48 - copies `lib\` and nothing else, a net48
`PackageReference` project without a `RuntimeIdentifier` - a class library, a test
project - does not copy `runtimes\` either, and Hyperlite's loader probes the directory
the assembly sits in. Without it such a consumer would fail at load time rather than at
build time.

It ships twice, as `build\<tfm>\` and as `buildTransitive\<tfm>\`, because each reaches
consumers the other does not. packages.config knows only `build\`. `PackageReference`
imports `build\` for a direct reference only, and Hyperlite.Persistence depends on
Hyperlite with build assets excluded - the SDK's default for a project reference - so a
project that references only Hyperlite.Persistence sees nothing but `buildTransitive\`.
Nothing imports both: `PackageReference` takes `buildTransitive\` in place of `build\`,
for a direct reference too.

The file has to be named after the **package**, not the assembly: nuget imports
`build\<tfm>\$(PackageId).targets` and nothing else. Those two names happen to coincide
now, but they did not before the rename - the package was `ht4n2.x64` and the file
`ht4n2.targets`, which nuget carried along and never imported. The project spells it
`$(PackageId).targets` on both sides so the two cannot come apart again.

## A thing to know

**The package version is `2026.10.0`, not `2026.10.0.0`.** NuGet normalises a four part
version whose last part is zero. `AssemblyVersion` and `FileVersion` keep all four parts;
only the package version is shortened, and `2026.10.0.0` and `2026.10.0` are the same
version to NuGet, so a consumer pinning either resolves the same package.

## Publishing

By `.github/workflows/release.yml`, on a tag `v<package version>` - `v2026.10.0` for
`2026.10.0.0` in `Version.props`. It runs `build.yml`, which builds, tests and packs on the
CI runner, checks that the packages carry the version the tag names, pushes them to nuget.org
and creates the GitHub release with the packages, the explorer zip and the native symbols - the
Windows pdb and the Linux `.debug`. It refuses a `Hyperlite` package without both native
libraries.

Pushing is not reversible, so the publishing job runs in the `nuget` environment, whose
required reviewers approve each release. No API key is stored: nuget.org's trusted publishing
exchanges the run's OIDC token for a key that lasts an hour. What the repository and nuget.org
need set up once is listed at the top of `release.yml`.

Packages from a local `pack.cmd` are for a local feed. Their embedded symbols link to no
source, because SourceLink needs the commit the packages were built from to be on GitHub.

## A software bill of materials in every package

Each package carries an SPDX 2.2 SBOM at `_manifest/spdx_2.2/manifest.spdx.json`, with its
SHA-256 beside it: every file in the package with its hash, and the packages it depends on. The
supplier is the copyright holder; the document namespace is the repository.

`Microsoft.Sbom.Tool` writes it, run by `Directory.Build.targets` on each package once
`pack.cmd` has packed it - unzip, generate, zip. The tool comes in `Microsoft.Sbom.Targets`,
fetched with a `PackageDownload` and called directly rather than through the package's own
targets:

- **Not a PackageReference.** Component detection reads the restore graph,
  `project.assets.json`, where a reference sits whatever its `PrivateAssets`: the tool named
  itself as a dependency of every package. A `PackageDownload` is recorded there too, under
  `downloadDependencies`, so detection reads a copy of the file with that section taken out -
  it only ever lists tooling.
- **Not its targets.** Under the full-framework msbuild their task passes an argument the tool
  rejects, so a package with an external document got no SBOM at all.

Two kinds of component no NuGet detector finds are named by SPDX documents of their own,
referenced as external documents and packed under `sbom/`, so that every reference resolves
inside the package:

- **sqlite**, compiled into the native companion from the archive in `deps/sqlite`:
  `src/Hyperlite/sqlite.spdx.json` - version, download location, its `blessing` licence. The
  native build rewrites it when it names another archive, see `UpdateSqliteSbom` in
  [build.md](build.md).
- **a package of this repository another depends on** - Hyperlite, for Hyperlite.Persistence -
  which inside the repository is a project reference: written at pack time by the
  `DescribeReferencedPackages` target, with name, version and purl.

The package version is stated in `Version.props` as nuget writes it, 2026.10.0 for 2026.10.0.0,
because the SBOM step finds the package file by that name.

**A package whose SBOM misses a dependency is not made.** After the tool runs, every dependency
the package's nuspec declares has to appear in its SBOM - as a package, or as the external
document of a package of this repository. If one does not, or the tool fails, the pack fails and
the package is deleted rather than left in `dist` without a complete SBOM.

The documents written for project references are dated when the package is made. `pack.cmd`
restores before it packs, because the dependencies a package declares - and its SBOM - come from
the restore result, and Visual Studio's own restore once rewrote Hyperlite's with none.
