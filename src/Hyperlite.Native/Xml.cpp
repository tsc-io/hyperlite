#include "pch.h"
#include "Xml.h"

#include <charconv>

namespace hyperlite::xml
{
namespace
{
constexpr bool is_space(char c) noexcept
{
    return c == ' ' || c == '\t' || c == '\r' || c == '\n';
}

constexpr bool is_name_start(char c) noexcept
{
    return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_' || c == ':' ||
           static_cast<unsigned char>(c) >= 0x80;
}

constexpr bool is_name_char(char c) noexcept
{
    return is_name_start(c) || (c >= '0' && c <= '9') || c == '-' || c == '.';
}

char lower(char c) noexcept
{
    return (c >= 'A' && c <= 'Z') ? static_cast<char>(c - 'A' + 'a') : c;
}

bool iequals(std::string_view a, std::string_view b) noexcept
{
    if (a.size() != b.size())
    {
        return false;
    }
    for (std::size_t i = 0; i < a.size(); ++i)
    {
        if (lower(a[i]) != lower(b[i]))
        {
            return false;
        }
    }
    return true;
}

[[noreturn]] void fail(const std::string& what)
{
    HYPERLITE_THROW(error::SCHEMA_PARSE_ERROR, what);
}

/// Appends the UTF-8 encoding of a code point.
void append_utf8(std::string& out, std::uint32_t cp)
{
    if (cp < 0x80)
    {
        out.push_back(static_cast<char>(cp));
    }
    else if (cp < 0x800)
    {
        out.push_back(static_cast<char>(0xC0 | (cp >> 6)));
        out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
    }
    else if (cp < 0x10000)
    {
        out.push_back(static_cast<char>(0xE0 | (cp >> 12)));
        out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
        out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
    }
    else
    {
        out.push_back(static_cast<char>(0xF0 | (cp >> 18)));
        out.push_back(static_cast<char>(0x80 | ((cp >> 12) & 0x3F)));
        out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
        out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
    }
}

class reader
{
public:
    explicit reader(std::string_view text) : s_(text) {}

    std::unique_ptr<element> parse_document()
    {
        skip_prolog();

        if (!consume('<'))
        {
            fail("Expected an element at the start of the document");
        }
        auto root = parse_element();

        // trailing misc is allowed, anything else is not
        skip_misc();
        if (pos_ != s_.size())
        {
            fail(format("Unexpected content after the root element at offset %zu", pos_));
        }
        return root;
    }

private:
    /// Deeper than any schema nests, and shallow enough that the recursive descent - and the
    /// recursive teardown of the tree it builds - cannot exhaust the stack.
    static constexpr int k_max_depth = 64;

    std::string_view s_;
    std::size_t pos_ = 0;
    int depth_ = 1;

    bool eof() const noexcept { return pos_ >= s_.size(); }
    char peek(std::size_t ahead = 0) const noexcept
    {
        return pos_ + ahead < s_.size() ? s_[pos_ + ahead] : '\0';
    }

    bool starts_with(std::string_view lit) const noexcept
    {
        return s_.compare(pos_, lit.size(), lit) == 0;
    }

    bool consume(char c) noexcept
    {
        if (peek() == c)
        {
            ++pos_;
            return true;
        }
        return false;
    }

    void expect(char c)
    {
        if (!consume(c))
        {
            fail(format("Expected '%c' at offset %zu", c, pos_));
        }
    }

    void skip_space() noexcept
    {
        while (!eof() && is_space(s_[pos_]))
        {
            ++pos_;
        }
    }

    /// Comments, processing instructions and the XML declaration.
    bool skip_one_misc()
    {
        if (starts_with("<!--"))
        {
            const auto at = s_.find("-->", pos_ + 4);
            if (at == std::string_view::npos)
            {
                fail("Unterminated comment");
            }
            pos_ = at + 3;
            return true;
        }
        if (starts_with("<?"))
        {
            const auto at = s_.find("?>", pos_ + 2);
            if (at == std::string_view::npos)
            {
                fail("Unterminated processing instruction");
            }
            pos_ = at + 2;
            return true;
        }
        if (starts_with("<!DOCTYPE"))
        {
            // no internal subset is expected in a schema, so scan to the closing '>'
            const auto at = s_.find('>', pos_);
            if (at == std::string_view::npos)
            {
                fail("Unterminated doctype declaration");
            }
            pos_ = at + 1;
            return true;
        }
        return false;
    }

    void skip_misc()
    {
        for (;;)
        {
            skip_space();
            if (!skip_one_misc())
            {
                return;
            }
        }
    }

    void skip_prolog() { skip_misc(); }

    std::string parse_name()
    {
        if (!is_name_start(peek()))
        {
            fail(format("Expected an element or attribute name at offset %zu", pos_));
        }
        const std::size_t from = pos_;
        while (!eof() && is_name_char(s_[pos_]))
        {
            ++pos_;
        }
        return std::string(s_.substr(from, pos_ - from));
    }

    /// Decodes the predefined entities and numeric character references.
    void append_reference(std::string& out)
    {
        const auto semi = s_.find(';', pos_);
        if (semi == std::string_view::npos)
        {
            fail("Unterminated entity reference");
        }
        const std::string_view body = s_.substr(pos_, semi - pos_);
        pos_ = semi + 1;

        if (body == "lt")
        {
            out.push_back('<');
            return;
        }
        if (body == "gt")
        {
            out.push_back('>');
            return;
        }
        if (body == "amp")
        {
            out.push_back('&');
            return;
        }
        if (body == "quot")
        {
            out.push_back('"');
            return;
        }
        if (body == "apos")
        {
            out.push_back('\'');
            return;
        }

        if (!body.empty() && body[0] == '#')
        {
            std::uint32_t cp = 0;
            const bool hex = body.size() > 1 && (body[1] == 'x' || body[1] == 'X');
            const std::string_view digits = body.substr(hex ? 2 : 1);
            if (digits.empty())
            {
                fail("Malformed character reference");
            }
            const auto* first = digits.data();
            const auto* last = digits.data() + digits.size();
            const auto res = std::from_chars(first, last, cp, hex ? 16 : 10);
            if (res.ec != std::errc() || res.ptr != last)
            {
                fail("Malformed character reference");
            }
            // only what XML allows as a character: &#0; put a NUL into a name, which was then
            // stored as an empty one and left the table unopenable, and a surrogate or a code
            // point past U+10FFFF came out as bytes that are not UTF-8
            const bool is_char = cp == 0x9 || cp == 0xA || cp == 0xD ||
                                 (cp >= 0x20 && cp <= 0xD7FF) || (cp >= 0xE000 && cp <= 0xFFFD) ||
                                 (cp >= 0x10000 && cp <= 0x10FFFF);
            if (!is_char)
            {
                fail(format("Character reference to U+%04X, which XML does not allow", cp));
            }
            append_utf8(out, cp);
            return;
        }

        fail(format("Unsupported entity reference '&%.*s;'", static_cast<int>(body.size()),
                    body.data()));
    }

    std::string parse_attribute_value()
    {
        const char quote = peek();
        if (quote != '"' && quote != '\'')
        {
            fail(format("Expected a quoted attribute value at offset %zu", pos_));
        }
        ++pos_;

        std::string out;
        for (;;)
        {
            if (eof())
            {
                fail("Unterminated attribute value");
            }
            const char c = s_[pos_];
            if (c == quote)
            {
                ++pos_;
                return out;
            }
            if (c == '&')
            {
                ++pos_;
                append_reference(out);
                continue;
            }
            // a literal newline or tab in an attribute normalises to a space
            out.push_back(c == '\n' || c == '\r' || c == '\t' ? ' ' : c);
            ++pos_;
        }
    }

    /// Called with pos_ just past the opening '<'.
    std::unique_ptr<element> parse_element()
    {
        auto node = std::make_unique<element>();
        node->name = parse_name();

        for (;;)
        {
            skip_space();
            if (consume('/'))
            {
                expect('>');
                return node; // self closing
            }
            if (consume('>'))
            {
                break;
            }
            attribute attr;
            attr.name = parse_name();
            skip_space();
            expect('=');
            skip_space();
            attr.value = parse_attribute_value();
            node->attributes.push_back(std::move(attr));
        }

        parse_content(*node);
        return node;
    }

    void parse_content(element& node)
    {
        std::string text;

        for (;;)
        {
            if (eof())
            {
                fail(format("Unterminated element '%s'", node.name.c_str()));
            }

            if (s_[pos_] == '<')
            {
                if (starts_with("</"))
                {
                    pos_ += 2;
                    const std::string close = parse_name();
                    skip_space();
                    expect('>');
                    if (!iequals(close, node.name))
                    {
                        fail(format("Closing tag '%s' does not match '%s'", close.c_str(),
                                    node.name.c_str()));
                    }
                    node.text = trim(text);
                    return;
                }
                if (starts_with("<![CDATA["))
                {
                    const auto at = s_.find("]]>", pos_ + 9);
                    if (at == std::string_view::npos)
                    {
                        fail("Unterminated CDATA section");
                    }
                    text.append(s_.substr(pos_ + 9, at - (pos_ + 9)));
                    pos_ = at + 3;
                    continue;
                }
                if (skip_one_misc())
                {
                    continue;
                }
                ++pos_; // past '<'
                if (++depth_ > k_max_depth)
                {
                    fail(format("Elements nested deeper than %d", k_max_depth));
                }
                node.children.push_back(parse_element());
                --depth_;
                continue;
            }

            if (s_[pos_] == '&')
            {
                ++pos_;
                append_reference(text);
                continue;
            }

            text.push_back(s_[pos_]);
            ++pos_;
        }
    }

    static std::string trim(const std::string& in)
    {
        std::size_t from = 0;
        std::size_t to = in.size();
        while (from < to && is_space(in[from]))
        {
            ++from;
        }
        while (to > from && is_space(in[to - 1]))
        {
            --to;
        }
        return in.substr(from, to - from);
    }
};
} // namespace

const std::string* element::find_attribute(std::string_view n) const noexcept
{
    for (const auto& a : attributes)
    {
        if (iequals(a.name, n))
        {
            return &a.value;
        }
    }
    return nullptr;
}

const element* element::find_child(std::string_view n) const noexcept
{
    for (const auto& c : children)
    {
        if (iequals(c->name, n))
        {
            return c.get();
        }
    }
    return nullptr;
}

std::unique_ptr<element> parse(std::string_view text)
{
    reader r(text);
    return r.parse_document();
}

std::string escape(std::string_view text)
{
    std::string out;
    out.reserve(text.size());
    for (const char c : text)
    {
        switch (c)
        {
        case '<':
            out += "&lt;";
            break;
        case '>':
            out += "&gt;";
            break;
        case '&':
            out += "&amp;";
            break;
        case '"':
            out += "&quot;";
            break;
        case '\'':
            out += "&apos;";
            break;
        // as references: written raw, a tab or line break in an attribute reads back as a space,
        // and the schema stored would no longer be the schema given
        case '\t':
            out += "&#x9;";
            break;
        case '\n':
            out += "&#xA;";
            break;
        case '\r':
            out += "&#xD;";
            break;
        default:
            out.push_back(c);
            break;
        }
    }
    return out;
}

bool content_to_bool(std::string_view element_name, std::string_view content)
{
    if (iequals(content, "true") || iequals(content, "yes") || content == "1")
    {
        return true;
    }
    if (iequals(content, "false") || iequals(content, "no") || content == "0")
    {
        return false;
    }
    HYPERLITE_THROWF(error::SCHEMA_PARSE_ERROR, "Invalid boolean value (%.*s) in %.*s element",
                     static_cast<int>(content.size()), content.data(),
                     static_cast<int>(element_name.size()), element_name.data());
}

namespace
{
std::int64_t to_integer(std::string_view element_name, std::string_view content, std::int64_t lo,
                        std::int64_t hi)
{
    std::string_view v = content;
    while (!v.empty() && is_space(v.front()))
    {
        v.remove_prefix(1);
    }
    while (!v.empty() && is_space(v.back()))
    {
        v.remove_suffix(1);
    }

    std::int64_t out = 0;
    const auto* first = v.data();
    const auto* last = v.data() + v.size();
    const auto res = std::from_chars(first, last, out);
    if (res.ec != std::errc() || res.ptr != last || out < lo || out > hi)
    {
        HYPERLITE_THROWF(error::SCHEMA_PARSE_ERROR, "Invalid integer value (%.*s) in %.*s element",
                         static_cast<int>(content.size()), content.data(),
                         static_cast<int>(element_name.size()), element_name.data());
    }
    return out;
}
} // namespace

std::int16_t content_to_i16(std::string_view element_name, std::string_view content)
{
    return static_cast<std::int16_t>(to_integer(element_name, content, INT16_MIN, INT16_MAX));
}

std::int32_t content_to_i32(std::string_view element_name, std::string_view content)
{
    return static_cast<std::int32_t>(to_integer(element_name, content, INT32_MIN, INT32_MAX));
}

std::int64_t content_to_i64(std::string_view element_name, std::string_view content)
{
    return to_integer(element_name, content, INT64_MIN, INT64_MAX);
}

std::string content_to_text(std::string_view element_name, std::string_view content,
                            const std::vector<std::string_view>& allowed)
{
    for (const auto& candidate : allowed)
    {
        if (iequals(content, candidate))
        {
            return std::string(candidate);
        }
    }
    HYPERLITE_THROWF(error::SCHEMA_PARSE_ERROR, "Invalid value (%.*s) in %.*s element",
                     static_cast<int>(content.size()), content.data(),
                     static_cast<int>(element_name.size()), element_name.data());
}
} // namespace hyperlite::xml
