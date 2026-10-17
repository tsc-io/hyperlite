#include "pch.h"
#include "Regex.h"

namespace hyperlite
{
namespace
{
abi::t_callbacks g_cb{};
} // namespace

void set_regex_callbacks(const abi::t_callbacks& cb)
{
    g_cb = cb;
}

bool has_regex_callbacks() noexcept
{
    return g_cb.re_compile && g_cb.re_match && g_cb.re_release;
}

regex::regex(std::string_view pattern, const char* what) : pattern_(pattern)
{
    if (!has_regex_callbacks())
    {
        HYPERLITE_THROW(error::NOT_IMPLEMENTED,
                        "No regex engine installed, Hyperlite did not set the callbacks");
    }

    handle_ = g_cb.re_compile(pattern.data(), static_cast<std::int32_t>(pattern.size()));
    if (!handle_)
    {
        HYPERLITE_THROWF(error::BAD_SCAN_SPEC, "Can't convert %s '%.*s' to regexp",
                         what ? what : "pattern", static_cast<int>(pattern.size()), pattern.data());
    }
}

regex::~regex()
{
    if (handle_ && g_cb.re_release)
    {
        g_cb.re_release(handle_);
        handle_ = nullptr;
    }
}

regex& regex::operator=(regex&& other) noexcept
{
    if (this != &other)
    {
        if (handle_ && g_cb.re_release)
        {
            g_cb.re_release(handle_);
        }
        handle_ = other.handle_;
        pattern_ = std::move(other.pattern_);
        other.handle_ = nullptr;
    }
    return *this;
}

bool regex::matches(const char* text, std::size_t len) const
{
    if (!handle_)
    {
        return false;
    }
    // a null text is matched as empty, which is how RE2::PartialMatch behaved on
    // an empty StringPiece
    const auto matched = g_cb.re_match(handle_, text ? text : "", static_cast<std::int32_t>(len));
    if (matched == -1)
    {
        // RE2 matched in linear time and could not run away; .NET backtracks and can.
        // Reporting no match would return a wrong result without saying so.
        HYPERLITE_THROWF(error::REQUEST_TIMEOUT, "Regexp '%s' timed out", pattern_.c_str());
    }
    if (matched < 0)
    {
        HYPERLITE_THROWF(error::EXTERNAL, "Regexp '%s' failed to match", pattern_.c_str());
    }
    return matched != 0;
}
} // namespace hyperlite
