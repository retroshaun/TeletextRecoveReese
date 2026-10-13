namespace TeletextRecoveReese.Core;

/// <summary>ETSI EN 300 706 Latin G0 national-option decoding.</summary>
public static class CharacterSets
{
    public const int Polish = 8;
    public const int SerbianCroatianSlovenian = 9;
    public const int Turkish = 10;
    public const int Romanian = 11;
    public const int Estonian = 12;
    public const int LatvianLithuanian = 13;

    public static bool IsNationalSubset(int value) => value is >= 0 and <= 6 or >= 8 and <= 13;

    // EN 300 706 table 36. IDs are UI choices, not transmitted designations.
    private static readonly char[][] AdditionalNationalSubsets =
    [
        ['#', 'ń', 'ą', 'Ƶ', 'Ś', 'Ł', 'ć', 'ó', 'ę', 'ż', 'ś', 'ł', 'ź'],
        ['#', 'Ë', 'Č', 'Ć', 'Ž', 'Đ', 'Š', 'ë', 'č', 'ć', 'ž', 'đ', 'š'],
        ['₺', 'ğ', 'İ', 'Ş', 'Ö', 'Ç', 'Ü', 'Ğ', 'ı', 'ş', 'ö', 'ç', 'ü'],
        ['#', '¤', 'Ţ', 'Â', 'Ş', 'Ă', 'Î', 'ı', 'ţ', 'â', 'ş', 'ă', 'î'],
        ['#', 'õ', 'Š', 'Ä', 'Ö', 'Ž', 'Ü', 'Õ', 'š', 'ä', 'ö', 'ž', 'ü'],
        ['#', '$', 'Š', 'ė', 'ę', 'Ž', 'č', 'ū', 'š', 'ą', 'ų', 'ž', 'į'],
    ];

    public static int? FromDesignation(int designation) => designation switch
    {
        0x08 => Polish,
        >= 0 and <= 6 => designation,
        0x09 or 0x11 or 0x21 => 1,
        0x0A or 0x12 => 2,
        0x0B or 0x13 => 3,
        0x0C or 0x14 or 0x44 => 4,
        0x0E or 0x26 => 6,
        0x10 or 0x40 => 0,
        0x15 => 5,
        0x16 or 0x36 => Turkish,
        0x1D => SerbianCroatianSlovenian,
        0x1F => Romanian,
        0x22 => Estonian,
        0x23 => LatvianLithuanian,
        _ => null,
    };
    private static readonly byte[] NationalPositions =
        [0x23, 0x24, 0x40, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F, 0x60, 0x7B, 0x7C, 0x7D, 0x7E];

    // C12-C14 values 0..6 in the legacy Western/Central Europe designation.
    private static readonly char[][] WesternNationalSubsets =
    [
        ['£', '$', '@', '←', '½', '→', '↑', '#', '—', '¼', '‖', '¾', '÷'],
        ['#', '$', '§', 'Ä', 'Ö', 'Ü', '^', '_', '°', 'ä', 'ö', 'ü', 'ß'],
        ['#', '¤', 'É', 'Ä', 'Ö', 'Å', 'Ü', '_', 'é', 'ä', 'ö', 'å', 'ü'],
        ['£', '$', 'é', '°', 'ç', '→', '↑', '#', 'ù', 'à', 'ò', 'è', 'ì'],
        ['é', 'ï', 'à', 'ë', 'ê', 'ù', 'î', '#', 'è', 'â', 'ô', 'û', 'ç'],
        ['ç', '$', '¡', 'á', 'é', 'í', 'ó', 'ú', '¿', 'ü', 'ñ', 'è', 'à'],
        ['#', 'ů', 'č', 'ť', 'ž', 'ý', 'í', 'ř', 'é', 'á', 'ě', 'ú', 'š'],
    ];

    public static char Decode(byte g0Code, int nationalOption = -1)
    {
        if (g0Code is < 0x20 or > 0x7F) return ' ';
        if (nationalOption is >= Polish and <= LatvianLithuanian)
        {
            int position = Array.IndexOf(NationalPositions, g0Code);
            if (position >= 0) return AdditionalNationalSubsets[nationalOption - Polish][position];
        }
        if (nationalOption is >= 0 and < 7)
        {
            int position = Array.IndexOf(NationalPositions, g0Code);
            if (position >= 0) return WesternNationalSubsets[nationalOption][position];
        }

        return g0Code switch
        {
            0x24 => '¤',
            0x7C => '¦',
            0x7F => '■',
            _ => (char)g0Code,
        };
    }
}
