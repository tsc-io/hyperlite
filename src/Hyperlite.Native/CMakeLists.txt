# The Linux build of Hyperlite.Native, with gcc: libHyperlite.Native.linux-x64.so and the
# native test beside it, in dist/<config>/runtimes/linux-x64/native. Hyperlite.Native.vcxproj
# is the Windows build and stays the reference: the sources and the sqlite configuration are
# read out of it, so that the two cannot drift apart.
#
#   cmake -S src/Hyperlite.Native -B build/linux-x64 -DCMAKE_BUILD_TYPE=Release
#   cmake --build build/linux-x64 --parallel
#
# build-linux.sh at the repository root does that and runs the tests.

cmake_minimum_required(VERSION 3.22)
project(Hyperlite.Native LANGUAGES C CXX)

if(NOT CMAKE_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE Release)
endif()

get_filename_component(REPO_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../.." ABSOLUTE)
set(HYPERLITE_RID linux-x64)
set(NATIVE_OUTPUT_DIR "${REPO_ROOT}/dist/${CMAKE_BUILD_TYPE}/runtimes/${HYPERLITE_RID}/native")

#
# sqlite: the newest amalgamation in deps/sqlite, configured as the vcxproj configures it
#
file(READ "${CMAKE_CURRENT_SOURCE_DIR}/Hyperlite.Native.vcxproj" VCXPROJ)
string(REGEX MATCH "<SqliteDefines>([^<]*)</SqliteDefines>" _ "${VCXPROJ}")
if(NOT CMAKE_MATCH_1)
    message(FATAL_ERROR "No SqliteDefines in Hyperlite.Native.vcxproj")
endif()
# a ; separated list in msbuild, which is what a list is in cmake too
set(SQLITE_DEFINES "${CMAKE_MATCH_1}")

file(GLOB SQLITE_ARCHIVES "${REPO_ROOT}/deps/sqlite/sqlite-amalgamation-*.zip")
set(SQLITE_ARCHIVE "")
set(SQLITE_VERSION 0)
foreach(archive IN LISTS SQLITE_ARCHIVES)
    string(REGEX MATCH "-([0-9]+)\\.zip$" _ "${archive}")
    if(CMAKE_MATCH_1 GREATER SQLITE_VERSION)
        set(SQLITE_VERSION "${CMAKE_MATCH_1}")
        set(SQLITE_ARCHIVE "${archive}")
    endif()
endforeach()
if(NOT SQLITE_ARCHIVE)
    message(FATAL_ERROR "No sqlite-amalgamation-*.zip in ${REPO_ROOT}/deps/sqlite")
endif()

# into the build tree rather than deps/sqlite, so a checkout shared with Windows keeps its
# own unpacked copy; again only when the version changes
set(SQLITE_DIR "${CMAKE_BINARY_DIR}/sqlite-${SQLITE_VERSION}")
if(NOT EXISTS "${SQLITE_DIR}/sqlite3.c")
    message(STATUS "Unpacking sqlite ${SQLITE_VERSION}")
    file(REMOVE_RECURSE "${CMAKE_BINARY_DIR}/sqlite-unpack")
    file(ARCHIVE_EXTRACT INPUT "${SQLITE_ARCHIVE}" DESTINATION "${CMAKE_BINARY_DIR}/sqlite-unpack")
    file(GLOB_RECURSE SQLITE_SOURCES
        "${CMAKE_BINARY_DIR}/sqlite-unpack/*/sqlite3.c" "${CMAKE_BINARY_DIR}/sqlite-unpack/*/sqlite3.h")
    list(LENGTH SQLITE_SOURCES count)
    if(NOT count EQUAL 2)
        message(FATAL_ERROR "Expected sqlite3.c and sqlite3.h in the amalgamation, found ${count}")
    endif()
    file(COPY ${SQLITE_SOURCES} DESTINATION "${SQLITE_DIR}")
    file(REMOVE_RECURSE "${CMAKE_BINARY_DIR}/sqlite-unpack")
endif()

#
# the library
#
string(REGEX MATCHALL "<ClCompile Include=\"([^\"]+)\"" COMPILES "${VCXPROJ}")
set(SOURCES "")
foreach(item IN LISTS COMPILES)
    string(REGEX REPLACE "<ClCompile Include=\"([^\"]+)\"" "\\1" file "${item}")
    list(APPEND SOURCES "${CMAKE_CURRENT_SOURCE_DIR}/${file}")
endforeach()

add_library(Hyperlite.Native SHARED ${SOURCES})
set_target_properties(Hyperlite.Native PROPERTIES
    OUTPUT_NAME "Hyperlite.Native.${HYPERLITE_RID}"
    LIBRARY_OUTPUT_DIRECTORY "${NATIVE_OUTPUT_DIR}"
    CXX_STANDARD 20
    CXX_STANDARD_REQUIRED ON
    CXX_EXTENSIONS OFF
    C_VISIBILITY_PRESET hidden
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN ON
    POSITION_INDEPENDENT_CODE ON)
target_include_directories(Hyperlite.Native PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}" "${SQLITE_DIR}")
target_compile_definitions(Hyperlite.Native PRIVATE ${SQLITE_DEFINES})
target_compile_options(Hyperlite.Native PRIVATE
    -g
    # the build machine's paths stay out of the binary and its debug information
    "-ffile-prefix-map=${REPO_ROOT}=/_"
    "-ffile-prefix-map=${CMAKE_BINARY_DIR}=/_/build"
    $<$<COMPILE_LANGUAGE:CXX>:-Wall -Wextra>)
# sqlite is built as it comes, without warnings, as in the vcxproj
set_source_files_properties("${CMAKE_CURRENT_SOURCE_DIR}/sqlite3-build.c" PROPERTIES COMPILE_OPTIONS -w)
target_link_options(Hyperlite.Native PRIVATE
    # nothing to install beside it but glibc: the C++ runtime is linked in, and hidden
    -static-libstdc++
    -static-libgcc
    -Wl,--exclude-libs,ALL
    -Wl,--no-undefined
    -Wl,--build-id
    -Wl,-z,relro
    -Wl,-z,now)
find_package(Threads REQUIRED)
target_link_libraries(Hyperlite.Native PRIVATE Threads::Threads m)

# the debug information goes into a .debug of its own, which the .so points at
add_custom_command(TARGET Hyperlite.Native POST_BUILD
    COMMAND ${CMAKE_OBJCOPY} --only-keep-debug "$<TARGET_FILE:Hyperlite.Native>" "$<TARGET_FILE:Hyperlite.Native>.debug"
    COMMAND ${CMAKE_STRIP} --strip-debug --strip-unneeded "$<TARGET_FILE:Hyperlite.Native>"
    COMMAND ${CMAKE_OBJCOPY} "--add-gnu-debuglink=$<TARGET_FILE_NAME:Hyperlite.Native>.debug" "$<TARGET_FILE:Hyperlite.Native>"
    WORKING_DIRECTORY "${NATIVE_OUTPUT_DIR}"
    VERBATIM)

#
# the native test, beside the library it loads
#
set(TEST_DIR "${REPO_ROOT}/tests/Hyperlite.Native.Test")
add_executable(Hyperlite.Native.Test "${TEST_DIR}/smoke.cpp" "${TEST_DIR}/bench.cpp" "${TEST_DIR}/platform.cpp")
set_target_properties(Hyperlite.Native.Test PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${NATIVE_OUTPUT_DIR}"
    CXX_STANDARD 20
    CXX_STANDARD_REQUIRED ON
    CXX_EXTENSIONS OFF)
target_include_directories(Hyperlite.Native.Test PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}")
target_compile_options(Hyperlite.Native.Test PRIVATE -Wall -Wextra)
target_link_libraries(Hyperlite.Native.Test PRIVATE ${CMAKE_DL_LIBS})
add_dependencies(Hyperlite.Native.Test Hyperlite.Native)
