namespace MeshcomWebDesk.Helpers;

/// <summary>
/// Converts amateur radio callsigns into NATO phonetic alphabet spelling.
/// Letters use the international NATO alphabet (identical in all languages).
/// Digits are spoken in the selected UI language.
/// SSID numbers (1-99) are spoken as a whole number, not digit by digit.
/// Example: "DH1FR-12" (de) → "Delta Hotel Eins Foxtrot Romeo Strich Zwölf"
/// </summary>
public static class PhoneticAlphabetHelper
{
    // NATO phonetic alphabet – international, language-independent
    private static readonly Dictionary<char, string> Nato = new()
    {
        { 'A', "Alfa"     }, { 'B', "Bravo"    }, { 'C', "Charlie"  },
        { 'D', "Delta"    }, { 'E', "Echo"      }, { 'F', "Foxtrot"  },
        { 'G', "Golf"     }, { 'H', "Hotel"     }, { 'I', "India"    },
        { 'J', "Juliett"  }, { 'K', "Kilo"      }, { 'L', "Lima"     },
        { 'M', "Mike"     }, { 'N', "November"  }, { 'O', "Oscar"    },
        { 'P', "Papa"     }, { 'Q', "Quebec"    }, { 'R', "Romeo"    },
        { 'S', "Sierra"   }, { 'T', "Tango"     }, { 'U', "Uniform"  },
        { 'V', "Victor"   }, { 'W', "Whiskey"   }, { 'X', "X-ray"    },
        { 'Y', "Yankee"   }, { 'Z', "Zulu"      },
    };

    // Single digits spoken in the respective UI language
    private static readonly Dictionary<string, string[]> Digits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["de"] = ["Null", "Eins", "Zwei", "Drei", "Vier", "Fünf", "Sechs", "Sieben", "Acht", "Neun"],
        ["en"] = ["Zero", "One",  "Two",  "Three","Four", "Five", "Six",   "Seven",  "Eight","Nine"],
        ["it"] = ["Zero", "Uno",  "Due",  "Tre",  "Quattro","Cinque","Sei","Sette",  "Otto", "Nove"],
        ["es"] = ["Cero", "Uno",  "Dos",  "Tres", "Cuatro","Cinco","Seis","Siete",  "Ocho", "Nueve"],
    };

    // Numbers 1–99 spoken as a whole word per language (index = number)
    // Index 0 unused; 1–19 explicit; 20–99 composed
    private static readonly Dictionary<string, string[]> Teens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["de"] = ["", "Eins","Zwei","Drei","Vier","Fünf","Sechs","Sieben","Acht","Neun",
                  "Zehn","Elf","Zwölf","Dreizehn","Vierzehn","Fünfzehn","Sechzehn","Siebzehn","Achtzehn","Neunzehn"],
        ["en"] = ["", "One","Two","Three","Four","Five","Six","Seven","Eight","Nine",
                  "Ten","Eleven","Twelve","Thirteen","Fourteen","Fifteen","Sixteen","Seventeen","Eighteen","Nineteen"],
        ["it"] = ["", "Uno","Due","Tre","Quattro","Cinque","Sei","Sette","Otto","Nove",
                  "Dieci","Undici","Dodici","Tredici","Quattordici","Quindici","Sedici","Diciassette","Diciotto","Diciannove"],
        ["es"] = ["", "Uno","Dos","Tres","Cuatro","Cinco","Seis","Siete","Ocho","Nueve",
                  "Diez","Once","Doce","Trece","Catorce","Quince","Dieciséis","Diecisiete","Dieciocho","Diecinueve"],
    };

    private static readonly Dictionary<string, string[]> Tens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["de"] = ["", "", "Zwanzig","Dreißig","Vierzig","Fünfzig","Sechzig","Siebzig","Achtzig","Neunzig"],
        ["en"] = ["", "", "Twenty","Thirty","Forty","Fifty","Sixty","Seventy","Eighty","Ninety"],
        ["it"] = ["", "", "Venti","Trenta","Quaranta","Cinquanta","Sessanta","Settanta","Ottanta","Novanta"],
        ["es"] = ["", "", "Veinte","Treinta","Cuarenta","Cincuenta","Sesenta","Setenta","Ochenta","Noventa"],
    };

    // Compound tens connector per language (e.g. "einundzwanzig" = "ein" + "und" + "zwanzig")
    private static readonly Dictionary<string, string> TensConnector = new(StringComparer.OrdinalIgnoreCase)
    {
        ["de"] = "und",   // dreiundzwanzig
        ["en"] = "-",     // twenty-three
        ["it"] = "",      // ventitré (no connector, handled below)
        ["es"] = " y ",   // veintitrés (special) / treinta y uno
    };

    // SSID separator word per language
    private static readonly Dictionary<string, string> Stroke = new(StringComparer.OrdinalIgnoreCase)
    {
        ["de"] = "Strich",
        ["en"] = "Stroke",
        ["it"] = "Tratto",
        ["es"] = "Guión",
    };

    /// <summary>
    /// Spells a callsign phonetically.
    /// Letters → NATO alphabet, digits → language-specific words, '-' → stroke word.
    /// SSID numbers (the part after '-') are spoken as a whole number (1–99).
    /// </summary>
    /// <param name="callsign">Callsign, e.g. "DH1FR" or "OE1KBC-12".</param>
    /// <param name="lang">Language code: "de", "en", "it" or "es". Defaults to "de".</param>
    /// <returns>Phonetic spelling as a space-separated string.</returns>
    public static string Spell(string callsign, string lang = "de")
    {
        if (string.IsNullOrWhiteSpace(callsign)) return string.Empty;

        var upper   = callsign.ToUpperInvariant();
        var dashIdx = upper.IndexOf('-');

        // Split into base callsign and optional SSID part
        var baseCall = dashIdx >= 0 ? upper[..dashIdx]    : upper;
        var ssid     = dashIdx >= 0 ? upper[(dashIdx+1)..] : null;

        var digits = Digits.TryGetValue(lang, out var d) ? d : Digits["de"];
        var stroke = Stroke.TryGetValue(lang, out var s) ? s : Stroke["de"];

        var parts = new List<string>();

        // Spell base callsign character by character
        foreach (var ch in baseCall)
        {
            if (Nato.TryGetValue(ch, out var word))
                parts.Add(word);
            else if (ch >= '0' && ch <= '9')
                parts.Add(digits[ch - '0']);
            // other characters silently skipped
        }

        // Spell SSID: if it is a number 1–99 → say as whole number, else digit by digit
        if (ssid != null)
        {
            parts.Add(stroke);
            if (int.TryParse(ssid, out var ssidNum) && ssidNum >= 1 && ssidNum <= 99)
                parts.Add(SpellNumber(ssidNum, lang));
            else
                foreach (var ch in ssid)
                    if (ch >= '0' && ch <= '9') parts.Add(digits[ch - '0']);
        }

        return string.Join(" ", parts);
    }

    /// <summary>Speaks a number 1–99 as a whole word in the given language.</summary>
    private static string SpellNumber(int n, string lang)
    {
        var teens = Teens.TryGetValue(lang, out var t) ? t : Teens["de"];
        var tens  = Tens.TryGetValue(lang,  out var x) ? x : Tens["de"];

        if (n <= 19) return teens[n];

        var tenPart  = tens[n / 10];
        var unitPart = n % 10 == 0 ? null : teens[n % 10];

        if (unitPart is null) return tenPart;

        // Language-specific composition
        return lang.ToLowerInvariant() switch
        {
            "de" => $"{unitPart}und{tenPart}",   // einundzwanzig
            "en" => $"{tenPart}-{unitPart}",      // twenty-one
            "it" => ComposeItalian(n, tenPart, unitPart),
            "es" => ComposeSpanish(n, tenPart, unitPart),
            _    => $"{tenPart} {unitPart}"
        };
    }

    // Italian: venti+uno = ventuno (drop trailing vowel of tens before vowel)
    private static string ComposeItalian(int n, string tenPart, string unitPart)
    {
        // 21–29 use "venti" prefix with vowel-elision rule
        if (n / 10 == 2)
        {
            var vowels = new[] { 'A','E','I','O','U' };
            var prefix = vowels.Contains(char.ToUpper(unitPart[0])) ? "Vent" : "Venti";
            return prefix + unitPart.ToLower();
        }
        return tenPart + unitPart.ToLower();
    }

    // Spanish: 21–29 use "veinti" prefix; 30+ use "y"
    private static string ComposeSpanish(int n, string tenPart, string unitPart)
    {
        if (n / 10 == 2) return "Veinti" + unitPart.ToLower();
        return $"{tenPart} y {unitPart.ToLower()}";
    }
}

