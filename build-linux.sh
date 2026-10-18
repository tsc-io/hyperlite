#!/usr/bin/env bash
#
# The Linux half of build.cmd and test.cmd: builds libHyperlite.Native.linux-x64.so with gcc
# and runs the native test and the suites over it, on net8.0 and net10.0.
#
#   ./build-linux.sh                  build and test, Release
#   ./build-linux.sh build [Debug]    the library and the native test only
#   ./build-linux.sh test [Debug]     build, then every test
#
# Needs gcc, cmake, rsync and the .NET 10 SDK with the 8.0 runtime; on Ubuntu 22.04, glibc
# 2.35, the oldest the library is built for:
#
#   sudo apt install build-essential cmake rsync
#   wget https://dot.net/v1/dotnet-install.sh && bash dotnet-install.sh --channel 10.0 &&
#       bash dotnet-install.sh --channel 8.0 --runtime dotnet
#
# From a Windows checkout under /mnt - which is how build.cmd and test.cmd call it through
# wsl.exe - the sources are first copied to ~/.cache/hyperlite: building on the Windows
# file system is slow and its locks are not Linux locks, which the native test looks at. The
# library and its .debug are copied back into the checkout's dist, for pack.cmd.

set -euo pipefail

command=${1:-test}
configuration=${2:-Release}
case "$command" in
    build|test) ;;
    *) echo "usage: $0 [build|test] [Release|Debug]" >&2; exit 2 ;;
esac

checkout=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
if [[ "$checkout" == /mnt/* ]]; then
    repo=$HOME/.cache/hyperlite
    mkdir -p "$repo"
    # build and dist are the copy's own; the unpacked sqlite is Windows' - cmake unpacks its
    # own into the build tree
    rsync -a --delete \
        --exclude=/.git/ --exclude=/.vs/ --exclude=/build/ --exclude=/dist/ \
        --exclude=bin/ --exclude=obj/ --exclude=TestResults/ \
        --exclude=/deps/sqlite/sqlite3.c --exclude=/deps/sqlite/sqlite3.h \
        --exclude='*.db' --exclude='*.db-journal' --exclude='*.db-wal' --exclude='*.db-shm' \
        "$checkout/" "$repo/"
else
    repo=$checkout
fi

if [[ -x $HOME/.dotnet/dotnet ]]; then
    export DOTNET_ROOT=$HOME/.dotnet
    export PATH=$DOTNET_ROOT:$PATH
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

native=dist/$configuration/runtimes/linux-x64/native
scratch=$repo/build/$configuration/linux-scratch

# sed rather than head, which stops reading and leaves ldd writing into a closed pipe
echo "=== native, gcc $(gcc -dumpfullversion), $(ldd --version | sed -n 1p) ==="
cmake -S "$repo/src/Hyperlite.Native" -B "$repo/build/linux-x64/$configuration" \
    -DCMAKE_BUILD_TYPE="$configuration" >/dev/null
cmake --build "$repo/build/linux-x64/$configuration" --parallel

# the newest glibc symbol it needs is the oldest glibc it runs on
glibc=$(objdump -T "$repo/$native/libHyperlite.Native.linux-x64.so" |
    grep -o 'GLIBC_[0-9.]*' | sort -uV | tail -n 1)
echo "needs $glibc"
if [[ $(printf '%s\n' "$glibc" GLIBC_2.35 | sort -V | tail -n 1) != GLIBC_2.35 ]]; then
    echo "libHyperlite.Native.linux-x64.so needs $glibc, newer than the 2.35 it is built for" >&2
    exit 1
fi

if [[ "$repo" != "$checkout" ]]; then
    mkdir -p "$checkout/$native"
    cp "$repo/$native/libHyperlite.Native.linux-x64.so" "$repo/$native/libHyperlite.Native.linux-x64.so.debug" \
        "$checkout/$native/"
fi

if [[ "$command" == build ]]; then
    echo "BUILD OK ($configuration) - $native"
    exit 0
fi

failures=0

echo
echo "=== native dispatch table ==="
mkdir -p "$scratch"
(cd "$scratch" && "$repo/$native/Hyperlite.Native.Test") || failures=$((failures + 1))

frameworks="net8.0 net10.0"

echo
echo "=== managed chain, smoke ==="
for framework in $frameworks; do
    echo "--- smoke $framework"
    dotnet build "$repo/tests/Hyperlite.Smoke/Hyperlite.Smoke.csproj" -c "$configuration" -f "$framework" \
        -v:minimal -nologo || { failures=$((failures + 1)); continue; }
    mkdir -p "$scratch/$framework"
    (cd "$scratch/$framework" && dotnet "$repo/dist/$configuration/$framework/Hyperlite.Smoke.dll") ||
        failures=$((failures + 1))
done

echo
echo "=== suites ==="
for suite in Hyperlite.Test Hyperlite.Persistence.Test Hyperlite.Serialization.Test; do
    for framework in $frameworks; do
        echo "--- $suite $framework"
        export HYPERLITE_TEST_SCRATCH=$scratch/$framework/$suite
        dotnet test "$repo/tests/$suite/$suite.csproj" -c "$configuration" -f "$framework" --nologo \
            --results-directory "$repo/build/$configuration/$framework/TestResults" \
            --logger "trx;LogFileName=$suite.linux.$framework.trx" || failures=$((failures + 1))
    done
done

echo
if [[ $failures == 0 ]]; then
    echo "TESTS OK ($configuration, linux-x64)"
    exit 0
fi
echo "TESTS FAILED ($configuration, linux-x64) - $failures step(s)"
exit 1
