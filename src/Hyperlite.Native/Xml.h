// Minimal non validating XML reader, enough for the table schema documents and
// nothing more. It exists so that Hyperlite.Native does not have to link expat.
//
// Supported: elements, attributes (single or double quoted), self closing tags,
// text content, the five predefined entities plus numeric character references,
// comments, CDATA sections, the XML declaration and processing instructions.
// Not supported, because a schema never contains them: DTDs, entity
// declarations, namespaces as anything other than part of the literal name.
//
// The reader builds a small tree rather than firing callbacks. Schema documents
// are a few kilobytes at most, and a tree is far easier to translate the ht4w
// parse logic onto, which walks children by name.

#pragma once

#include "Support.h"

#include <memory>
#include <string>
#include <string_view>
#include <vector>

namespace hyperlite::xml
{
struct attribute
{
    std::string name;
    std::string value;
};

class element final
{
public:
    std::string name;
    std::vector<attribute> attributes;
    /// Concatenated text content of this element, entity decoded, with leading and
    /// trailing whitespace trimmed. ht4w's XmlParser handed content over the same
    /// way, so <MaxVersions> 5 </MaxVersions> parses.
    std::string text;
    std::vector<std::unique_ptr<element>> children;

    /// Case insensitive attribute lookup, matching the strcasecmp ht4w used.
    const std::string* find_attribute(std::string_view n) const noexcept;
    /// First child with that name, case insensitive, or nullptr.
    const element* find_child(std::string_view n) const noexcept;
};

/// Parses a whole document and returns its root element.
/// Throws hyperlite::exception with error::SCHEMA_PARSE_ERROR on malformed input,
/// which is the code ht4w reported for the same condition.
std::unique_ptr<element> parse(std::string_view text);

/// Escapes the five characters that may not appear literally in element text or
/// in a double quoted attribute value.
std::string escape(std::string_view text);

/// Accepts "true"/"false", "yes"/"no", "1"/"0", case insensitive, as ht4w did.
bool content_to_bool(std::string_view element_name, std::string_view content);
std::int16_t content_to_i16(std::string_view element_name, std::string_view content);
std::int32_t content_to_i32(std::string_view element_name, std::string_view content);
std::int64_t content_to_i64(std::string_view element_name, std::string_view content);
/// Verifies the content is one of the allowed spellings, returns it lowercased.
std::string content_to_text(std::string_view element_name, std::string_view content,
                            const std::vector<std::string_view>& allowed);
} // namespace hyperlite::xml
