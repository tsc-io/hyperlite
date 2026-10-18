@echo off
setlocal enableextensions

rem Runs everything, in the order that makes a failure easiest to place: the native
rem dispatch table first, then the managed chain over it, then the suites. A break in the
rem first says the storage engine; a break only in the last says the api surface.
rem
rem   test.cmd             Release
rem   test.cmd Debug       Debug
rem
rem Assumes build.cmd has run - nothing here builds. dotnet test is given --no-build for
rem that reason and not only for speed: it would otherwise try to build the solution's
rem C++ projects, which the dotnet cli cannot do.
rem
rem The framework list below has to match HyperliteTargetFrameworks in
rem Directory.Build.props. Nothing enforces that: a framework built but not listed here is
rem simply never run, silently.

set configuration=%1
if "%configuration%" == "" set configuration=Release

rem Refuse to test what the last build.cmd did not produce. It removes this marker before it
rem starts and writes it only when everything built, so after a failed build dist still holds
rem the previous binaries, and a green run over them would report on code that is no longer
rem the source.
if not exist "%~dp0build\%configuration%\last-build.ok" (
    echo The last build.cmd %configuration% did not succeed, or has not run - dist\%configuration%
    echo holds older binaries than the source. Run build.cmd %configuration% first.
    exit /b 1
)

set dist=%~dp0dist\%configuration%
set failures=0

rem Everything below runs from a scratch directory under build, not from dist. The
rem executables write databases relative to the working directory, and dist holds build
rem output - a chain.db among the binaries is not build output. Windows and the managed
rem loader both probe the executable's own directory, so an absolute path is enough and
rem nothing has to be copied.
set scratch=%~dp0build\%configuration%\scratch
if not exist "%scratch%" mkdir "%scratch%"

echo === native dispatch table ===
if not exist "%dist%\runtimes\win-x64\native\Hyperlite.Native.Test.exe" (
    echo Hyperlite.Native.Test.exe not found - run build.cmd %configuration% first.
    exit /b 1
)
pushd "%scratch%"
"%dist%\runtimes\win-x64\native\Hyperlite.Native.Test.exe"
if errorlevel 1 set /a failures+=1
popd

echo.
set frameworks=net48 net7.0 net8.0 net10.0

echo === managed chain, smoke ===
for %%f in (%frameworks%) do call :smoke %%f

echo.
echo === suites ===
for %%f in (%frameworks%) do call :suite tests\Hyperlite.Test\Hyperlite.Test.csproj %%f
for %%f in (%frameworks%) do call :suite tests\Hyperlite.Persistence.Test\Hyperlite.Persistence.Test.csproj %%f
for %%f in (%frameworks%) do call :suite tests\Hyperlite.Serialization.Test\Hyperlite.Serialization.Test.csproj %%f

rem The same again on Linux, net8.0 and net10.0, when there is a WSL distribution to run them
rem in - see build-linux.sh
call :wsl
if not errorlevel 1 (
    echo.
    echo === linux-x64 ===
    wsl.exe --cd "%~dp0." -e bash ./build-linux.sh test %configuration% || set /a failures+=1
)

echo.
if %failures% == 0 (
    echo TESTS OK ^(%configuration%^)
    exit /b 0
)
echo TESTS FAILED ^(%configuration%^) - %failures% step^(s^)
exit /b 1

:smoke
echo --- smoke %1
if not exist "%scratch%\%1" mkdir "%scratch%\%1"
pushd "%scratch%\%1"
if exist "%dist%\%1\Hyperlite.Smoke.exe" (
    "%dist%\%1\Hyperlite.Smoke.exe"
) else (
    dotnet "%dist%\%1\Hyperlite.Smoke.dll"
)
if errorlevel 1 set /a failures+=1
popd
exit /b 0

:suite
echo --- %~n1 %2
rem --results-directory keeps the trx out of the source tree; without it dotnet test
rem writes TestResults next to the project file. The logger is what puts anything in
rem there at all - the directory is created either way, results or not.
rem HYPERLITE_TEST_SCRATCH is where a suite runs, as the smoke harness does: the TestBase of
rem the two suites that write databases switches to it, so those stay out of dist.
set HYPERLITE_TEST_SCRATCH=%scratch%\%2\%~n1
dotnet test "%~dp0%1" -c %configuration% --no-build -f %2 --nologo ^
    --results-directory "%~dp0build\%configuration%\%2\TestResults" ^
    --logger "trx;LogFileName=%~n1.%2.trx"
if errorlevel 1 set /a failures+=1
exit /b 0

:wsl
if "%GITHUB_ACTIONS%" == "true" exit /b 1
rem || rather than errorlevel: wsl.exe fails with -1, which "if errorlevel 1" does not see
wsl.exe -e true >nul 2>&1 || exit /b 1
exit /b 0
