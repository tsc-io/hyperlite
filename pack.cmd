@echo off
setlocal enableextensions

rem Packs the three nuget packages into dist\<configuration>, beside the framework
rem directories they were built from, and the explorer as a plain zip beside them -
rem Hyperlite.Explorer.<version>.zip, one folder per framework.
rem
rem   pack.cmd             Release
rem   pack.cmd Debug       Debug, for a local feed only - do not publish it
rem
rem Assumes build.cmd has run: -p:NoBuild=true packs what is in dist rather than building
rem again, so the packages contain exactly the binaries that were tested.
rem
rem One package per library, each carrying every framework it was built for. Build the
rem whole matrix before packing - packing after building a single framework would produce
rem a package claiming support for that one alone.

set configuration=%1
if "%configuration%" == "" set configuration=Release

rem Refuse to pack what the last build.cmd did not produce. It removes this marker before it
rem starts and writes it only when everything built, so after a failed build dist still holds
rem the previous binaries, and the packages would ship code that is no longer the source.
if not exist "%~dp0build\%configuration%\last-build.ok" (
    echo The last build.cmd %configuration% did not succeed, or has not run - dist\%configuration%
    echo holds older binaries than the source. Run build.cmd %configuration% first.
    exit /b 1
)

call "%~dp0find-msbuild.cmd"
if errorlevel 1 exit /b 1

rem Restore again before packing. A package's dependencies, and its SBOM, come from the
rem restore result, and something else can rewrite that between build.cmd and here - Visual
rem Studio restores on its own when a props file changes, and once wrote Hyperlite's with no
rem dependencies at all, which packed a Hyperlite that did not declare System.Buffers. Restoring
rem the same project files gives the same result, so the binaries built still match it.
"%msbuild%" "%~dp0Hyperlite.sln" -t:Restore -p:Configuration=%configuration% -p:Platform=x64 -v:minimal -nologo
if errorlevel 1 goto :failed

"%msbuild%" "%~dp0Hyperlite.sln" -t:Pack -p:Configuration=%configuration% -p:Platform=x64 -p:NoBuild=true -v:minimal -nologo
if errorlevel 1 goto :failed

rem No -p:Platform here: on the project alone it would move the intermediate directory away
rem from the one the solution build wrote, and the publish inside PackZip would find nothing.
"%msbuild%" "%~dp0src\Hyperlite.Explorer\Hyperlite.Explorer.csproj" -t:PackZip -p:Configuration=%configuration% -v:minimal -nologo
if errorlevel 1 goto :failed

echo.
echo PACK OK ^(%configuration%^)
dir /b "%~dp0dist\%configuration%\*.nupkg" "%~dp0dist\%configuration%\*.zip"
exit /b 0

:failed
echo.
echo PACK FAILED ^(%configuration%^)
exit /b 1
