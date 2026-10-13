using System.Numerics;
using TeletextRecoveReese.Core;

internal static class CharacterSetRegressions
{
    private static byte Odd(int value) => (byte)(BitOperations.PopCount((uint)value) % 2 == 0 ? value | 128 : value);
    private static byte[] Row(int row, params byte[] text)
    {
        var packet = Enumerable.Repeat(Odd(32), 42).ToArray();
        int address = (row << 3) | 1;
        packet[0] = Hamming.Encode84(address & 15);
        packet[1] = Hamming.Encode84(address >> 4);
        if (row == 0)
            for (int offset = 2; offset < 10; offset++) packet[offset] = Hamming.Encode84(0);
        for (int index = 0; index < text.Length; index++) packet[index + 2] = Odd(text[index]);
        return packet;
    }

    private static byte[] Designation(int row, int primary, int secondary, int dc = 0)
    {
        var raw = Row(row);
        raw[2] = Hamming.Encode84(dc);
        for (int triplet = 0; triplet < 13; triplet++)
        {
            int value = triplet == 0 ? (primary << 7) | ((secondary & 15) << 14)
                : triplet == 1 ? secondary >> 4 : 0;
            int encoded = Hamming.Encode24_18(value);
            raw[3 + triplet * 3] = (byte)encoded;
            raw[4 + triplet * 3] = (byte)(encoded >> 8);
            raw[5 + triplet * 3] = (byte)(encoded >> 16);
        }
        return raw;
    }

    private static TeletextPage Read(params byte[][] packets)
    {
        var store = new PageStore();
        var assembler = new PageAssembler(store, decodeEnhancements: false);
        for (int index = 0; index < packets.Length; index++) assembler.Feed(packets[index], index);
        assembler.FinalizeAll();
        return store.AllInstances.Last().Page;
    }

    private static void Check(string name, bool condition)
    {
        if (!condition) throw new Exception($"FAILED: {name}");
        Console.WriteLine($"PASS: {name}");
    }

    public static void Run()
    {
        byte[] codes = [0x23, 0x24, 0x40, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F, 0x60, 0x7B, 0x7C, 0x7D, 0x7E];
        Check("all Polish national positions match ETSI table 36",
            new string(codes.Select(code => CharacterSets.Decode(code, CharacterSets.Polish)).ToArray()) == "#ńąƵŚŁćóężśłź");
        var additional = new[]
        {
            (Subset: CharacterSets.SerbianCroatianSlovenian, Designation: 0x1D, Text: "#ËČĆŽĐŠëčćžđš"),
            (Subset: CharacterSets.Turkish, Designation: 0x16, Text: "₺ğİŞÖÇÜĞışöçü"),
            (Subset: CharacterSets.Romanian, Designation: 0x1F, Text: "#¤ŢÂŞĂÎıţâşăî"),
            (Subset: CharacterSets.Estonian, Designation: 0x22, Text: "#õŠÄÖŽÜÕšäöžü"),
            (Subset: CharacterSets.LatvianLithuanian, Designation: 0x23, Text: "#$ŠėęŽčūšąųžį"),
        };
        foreach (var entry in additional)
        {
            Check($"Latin subset {entry.Subset} maps all national positions",
                new string(codes.Select(code => CharacterSets.Decode(code, entry.Subset)).ToArray()) == entry.Text);
            var header = Row(0);
            header[9] = Hamming.Encode84((entry.Designation & 7) << 1);
            var primaryPage = Read(header, Row(1, codes), Designation(28, entry.Designation, 127));
            Check($"Latin subset {entry.Subset} is recognized automatically as primary", primaryPage.RowText(1).StartsWith(entry.Text));
            var secondaryPage = Read(Row(0), Row(1, new byte[] { 0x1B }.Concat(codes).ToArray()), Designation(28, 0, entry.Designation));
            Check($"Latin subset {entry.Subset} is recognized automatically as secondary", secondaryPage.RowText(1).StartsWith(" " + entry.Text));
        }
        Check("all thirteen Latin choices are selectable", Enumerable.Range(0, 128).Count(CharacterSets.IsNationalSubset) == 13);
        Check("Latin aliases and non-Latin designations remain distinct",
            CharacterSets.FromDesignation(0x10) == 0 && CharacterSets.FromDesignation(0x12) == 2
            && CharacterSets.FromDesignation(0x15) == 5 && CharacterSets.FromDesignation(0x26) == 6
            && CharacterSets.FromDesignation(0x36) == CharacterSets.Turkish
            && CharacterSets.FromDesignation(0x20) is null && CharacterSets.FromDesignation(0x24) is null);
        var page = Read(Row(0), Row(1, 0x7D, 0x1B, 0x7D, 0x1B, 0x7D), Row(2, 0x7D), Designation(28, 0, 8));
        Check("late X/28 secondary designation re-decodes existing rows", page.RowText(1).StartsWith("¾ ł ¾"));
        Check("ESC secondary state resets at each row", page.Grid[0, 2].Character == '¾');
        Check("cell lookup uses the active subset for editing", page.NationalOptionAt(2, 1) == CharacterSets.Polish
            && page.NationalOptionAt(4, 1) == 0);
        var snapshots = page.RawRows.Select(raw => raw is null ? "" : Convert.ToHexString(raw)).ToList();
        PageAssembler.SetSecondaryNationalOptionOverride(page, -2);
        Check("manual disable stops ESC switching", page.Grid[2, 1].Character == '¾');
        PageAssembler.SetSecondaryNationalOptionOverride(page, 8);
        Check("manual Polish secondary override works", page.Grid[2, 1].Character == 'ł');
        PageAssembler.SetSecondaryNationalOptionOverride(page, null);
        Check("display overrides preserve raw bytes", snapshots.SequenceEqual(page.RawRows.Select(raw => raw is null ? "" : Convert.ToHexString(raw))));

        page = Read(Designation(29, 0, 8), Row(0), Row(1, 0x1B, 0x7D));
        Check("M/29 supplies the magazine secondary subset", page.Grid[1, 1].Character == 'ł');
        page = Read(Designation(29, 0, 8), Row(0), Row(1, 0x1B, 0x7D), Designation(28, 0, 1, 4));
        Check("page X/28/4 overrides magazine M/29/0", page.Grid[1, 1].Character == 'ü');
        PageAssembler.ApplyCharacterSetPacket(page, Designation(28, 0, 8));
        Check("X/28/0 takes priority over X/28/4", page.Grid[1, 1].Character == 'ł');
        PageAssembler.ApplyCharacterSetPacket(page, Designation(28, 0, 1, 4));
        Check("late lower-priority X/28/4 cannot replace X/28/0", page.Grid[1, 1].Character == 'ł');

        page = Read(Row(0), Row(1, 0x1B, 0x7D), Designation(28, 0, 127));
        Check("secondary designation 127 disables ESC", page.Grid[1, 1].Character == '¾');
        page = Read(Row(0), Row(1, 0x7D), Designation(28, 8, 127));
        Check("automatic Polish primary subset works", page.Grid[0, 1].Character == 'ł');
        var germanHeader = Row(0);
        germanHeader[9] = Hamming.Encode84(2);
        page = Read(germanHeader, Row(1, 0x7D), Designation(28, 8, 8));
        Check("primary honors header national bits within the designated group", page.Grid[0, 1].Character == 'ü');

        var bad = Designation(28, 0, 1);
        bad[3] ^= 3;
        Check("uncorrectable designation is rejected", !PageAssembler.ApplyCharacterSetPacket(page, bad));
        var corrected = Designation(28, 0, 8);
        corrected[3] ^= 1;
        Check("single-bit designation damage is corrected", PageAssembler.ApplyCharacterSetPacket(page, corrected)
            && page.SecondaryNationalOption == CharacterSets.Polish);
        page = Read(Row(0), Row(1, 0x11, 0x21, 0x1B, 0x5D, 0x21), Designation(28, 0, 8));
        Check("secondary switching changes blast-through text while preserving mosaics",
            page.Grid[1, 1].IsMosaic && page.Grid[3, 1].Character == 'Ł' && page.Grid[4, 1].IsMosaic);

        var source = new[] { Designation(29, 0, 8), Row(0), Row(1, 0x1B, 0x7D), Row(2), Row(3) };
        var output = RecoverySquasher.Build(source, new RecoverySquashOptions { RequireServiceHeader = false });
        page = Read(output.ToArray());
        Check("squash materializes magazine designation into a self-contained page", page.Grid[1, 1].Character == 'ł'
            && page.CharacterSetPackets.Count == 1 && !page.CharacterSetPackets[0].IsMagazineDefault);
        source = [Designation(29, 0, 8), Row(0), Row(1, 0x1B, 0x7D), Row(2), Row(3), Designation(28, 0, 1, 4)];
        output = RecoverySquasher.Build(source, new RecoverySquashOptions { RequireServiceHeader = false });
        page = Read(output.ToArray());
        Check("squash retains page override rather than exporting a conflicting magazine default",
            page.Grid[1, 1].Character == 'ü' && page.CharacterSetPackets.Single().DesignationCode == 4);
    }
}
