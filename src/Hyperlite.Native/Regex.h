// ht4c matched row keys, cell values and column qualifiers with RE2. RE2 is not
// one of the three dependencies Hyperlite.Native is allowed, so matching is delegated
// back to the managed side, which owns a real regex engine already.
//
// Hyperlite installs three function pointers through the ABI (compile, match,
// release) and this wrapper owns the resulting handles with RAII. Semantics match
// what the scanner needs: an unanchored search, the equivalent of
// RE2::PartialMatch and of Regex.IsMatch.
//
// The callback is only ever entered from the thread that called into the scanner,
// so no runtime thread attach is involved.

#pragma once

#include "Support.h"
#include "abi.h"

#include <string_view>

namespace hyperlite
{
/// Installs the managed matcher. Called once, from the ABI entry point.
void set_regex_callbacks(const abi::t_callbacks& cb);

/// True once set_regex_callbacks has run with a usable table.
bool has_regex_callbacks() noexcept;

/// A compiled pattern. Owns the managed side handle.
class regex final
{
public:
    regex() = default;

    /// Compiles pattern. Throws BAD_SCAN_SPEC when the managed engine rejects it,
    /// which is the code and condition ht4c reported for an unusable RE2.
    regex(std::string_view pattern, const char* what);

    ~regex();

    regex(const regex&) = delete;
    regex& operator=(const regex&) = delete;

    regex(regex&& other) noexcept : handle_(other.handle_), pattern_(std::move(other.pattern_))
    {
        other.handle_ = nullptr;
    }

    regex& operator=(regex&& other) noexcept;

    bool valid() const noexcept { return handle_ != nullptr; }

    /// Unanchored match over raw bytes. A non UTF-8 value is passed through as is,
    /// the managed side decodes it the same way ht4n did.
    bool matches(const char* text, std::size_t len) const;
    bool matches(std::string_view text) const { return matches(text.data(), text.size()); }

    const std::string& pattern() const noexcept { return pattern_; }

private:
    void* handle_ = nullptr;
    std::string pattern_;
};
} // namespace hyperlite
