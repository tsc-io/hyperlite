@echo off

rem Locates Visual Studio 2026 and returns its directory in %vsroot% and its msbuild in
rem %msbuild%: where vswhere says it is, otherwise the first edition found in the default
rem places, the Build Tools last.
rem
rem Its own script because build.cmd, pack.cmd and format.cmd all need it, and a copy in
rem each is a copy that goes stale when a new Visual Studio arrives.

set vsroot=
set msbuild=

rem vswhere finds an installation anywhere, the list below only where the installer puts it
rem by default: a release first, a preview only when there is none. The ',' and ')' of the
rem version range are escaped for the for /f command line.
rem
rem The C++ workload is asked for, the toolset is not: the v143 the C++ projects build with
rem is an individual component, one for each of its versions, or comes from a Visual Studio
rem 2022 beside this one - and msbuild names it when it is missing (MSB8020).
set vswhere=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe
if exist "%vswhere%" (
    for %%p in ("" "-prerelease") do (
        for /f "usebackq delims=" %%i in (`"%vswhere%" %%~p -version [18.0^,19.0^) -products * -requires Microsoft.Component.MSBuild Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do (
            if not defined vsroot if exist "%%i\MSBuild\Current\bin\msbuild.exe" set vsroot=%%i
        )
    )
)

for %%v in (
    "C:\Program Files\Microsoft Visual Studio\18\Professional"
    "C:\Program Files\Microsoft Visual Studio\18\Enterprise"
    "C:\Program Files\Microsoft Visual Studio\18\Community"
    "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools"
) do (
    rem 'if not defined' and not 'if "%vsroot%" == ""': %vsroot% inside a for body is
    rem expanded once, when the block is parsed, so it would read as empty on every
    rem iteration and the last installation found would win instead of the first.
    if not defined vsroot if exist "%%~v\MSBuild\Current\bin\msbuild.exe" set vsroot=%%~v
)

if not defined vsroot (
    rem Visual Studio 2026 only: the msbuild of Visual Studio 2022 does not support net10.0
    echo No msbuild found. Visual Studio 2026 with the C++ workload is required.
    exit /b 1
)

set msbuild=%vsroot%\MSBuild\Current\bin\msbuild.exe
exit /b 0
