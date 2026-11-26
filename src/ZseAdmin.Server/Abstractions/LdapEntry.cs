using System.Text;

namespace ZseAdmin.Server.Abstractions;

/// <summary>
/// Pozycja wyniku wyszukiwania LDAP zdekodowana z <c>SearchResultEntry.Attributes</c>.
/// Pakiet <c>System.DirectoryServices.Protocols</c> zwraca wartości jako bajty, dlatego każdy
/// atrybut ma osobną, jawną konwersję — testowalną bez kontrolera domeny.
/// </summary>
public sealed record LdapEntry(
    string DistinguishedName,
    IReadOnlyDictionary<string, IReadOnlyList<byte[]>> Attributes)
{
    /// <summary>Pierwszy tekstowy atrybut (UTF-8), bez pustych wartości.</summary>
    public string? Text(string attribute)
    {
        if (!Attributes.TryGetValue(attribute, out var values))
        {
            return null;
        }

        foreach (var value in values)
        {
            var text = DecodeString(value);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    /// <summary>Wszystkie niepuste wartości atrybutu jako tekst (np. <c>memberOf</c>).</summary>
    public IReadOnlyList<string> Texts(string attribute)
    {
        if (!Attributes.TryGetValue(attribute, out var values))
        {
            return Array.Empty<string>();
        }

        return values
            .Select(DecodeString)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList();
    }

    private static string? DecodeString(byte[]? value)
    {
        if (value is null || value.Length == 0)
        {
            return null;
        }

        // Wartości tekstowe w AD są UTF-8; końcowy NUL bywa doklejony przez niektóre atrybuty.
        return Encoding.UTF8.GetString(value).TrimEnd('\0');
    }
}
