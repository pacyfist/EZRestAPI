namespace EZRestAPI.Utils;

public static class StringExtensions
{
    private static readonly HashSet<string> ReservedKeywords =
    [
        "abstract",
        "as",
        "base",
        "bool",
        "break",
        "byte",
        "case",
        "catch",
        "char",
        "checked",
        "class",
        "const",
        "continue",
        "decimal",
        "default",
        "delegate",
        "do",
        "double",
        "else",
        "enum",
        "event",
        "explicit",
        "extern",
        "false",
        "finally",
        "fixed",
        "float",
        "for",
        "foreach",
        "goto",
        "if",
        "implicit",
        "in",
        "int",
        "interface",
        "internal",
        "is",
        "lock",
        "long",
        "namespace",
        "new",
        "null",
        "object",
        "operator",
        "out",
        "override",
        "params",
        "private",
        "protected",
        "public",
        "readonly",
        "ref",
        "return",
        "sbyte",
        "sealed",
        "short",
        "sizeof",
        "stackalloc",
        "static",
        "string",
        "struct",
        "switch",
        "this",
        "throw",
        "true",
        "try",
        "typeof",
        "uint",
        "ulong",
        "unchecked",
        "unsafe",
        "ushort",
        "using",
        "virtual",
        "void",
        "volatile",
        "while",
    ];

    /// <summary>
    /// Lowercases the first character; results that collide with a C#
    /// reserved keyword are escaped with '@' so they stay valid identifiers.
    /// </summary>
    public static string ToCamelCase(this string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var camel = char.ToLowerInvariant(value[0]) + value.Substring(1);

        return ReservedKeywords.Contains(camel) ? "@" + camel : camel;
    }

    public static string ToPascalCase(this string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value.Substring(1);
    }

    public static string ToCleanNamespace(this string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (value.StartsWith("global::"))
        {
            return value.Substring(8);
        }

        return value;
    }
}
