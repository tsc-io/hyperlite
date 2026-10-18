@echo off
setlocal enableextensions

rem Builds everything: the native companion, the three libraries, the explorer, the tests
rem and the benchmark, for every framework each project targets.
rem
rem   build.cmd            Release
rem   build.cmd Debug      Debug
rem
rem msbuild rather than dotnet build, and that is not a preference: the solution contains
rem C++ projects, which the dotnet cli cannot build. Everything else - restore, the
rem framework matrix, packing - is plain SDK behaviour.

set configuration=%1
if "%configuration%" == "" set configuration=Release

rem The marker test.cmd and pack.cmd require. Removed before anything is built and written
rem only once everything has, so a failed or interrupted build can never be tested or packed
rem as though it had succeeded - dist would still hold the previous build's binaries, and
rem dotnet test --no-build would report green on code that is no longer the source.
set marker=%~dp0build\%configuration%\last-build.ok
if exist "%marker%" del "%marker%"

call "%~dp0find-msbuild.cmd"
if errorlevel 1 exit /b 1

"%msbuild%" "%~dp0Hyperlite.sln" -t:Restore -p:Configuration=%configuration% -p:Platform=x64 -v:minimal -nologo
if errorlevel 1 goto :failed

"%msbuild%" "%~dp0Hyperlite.sln" -p:Configuration=%configuration% -p:Platform=x64 -m -v:minimal -nologo
if errorlevel 1 goto :failed

rem The Linux library, for pack.cmd to carry as well, when there is a WSL distribution to
rem build it in - see build-linux.sh. Without one the package is Windows only, which pack.cmd
rem warns about. Not on GitHub, where the build workflow's linux job builds it.
call :wsl
if errorlevel 1 goto :skiplinux
wsl.exe --cd "%~dp0." -e bash ./build-linux.sh build %configuration% || goto :failed
:skiplinux

if not exist "%~dp0build\%configuration%" mkdir "%~dp0build\%configuration%"
echo %date% %time%> "%marker%"

echo.
echo BUILD OK ^(%configuration%^) - output in dist\%configuration%
exit /b 0

:failed
echo.
echo BUILD FAILED ^(%configuration%^)
exit /b 1

:wsl
if "%GITHUB_ACTIONS%" == "true" exit /b 1
rem || rather than errorlevel: wsl.exe fails with -1, which "if errorlevel 1" does not see
wsl.exe -e true >nul 2>&1 || exit /b 1
exit /b 0
