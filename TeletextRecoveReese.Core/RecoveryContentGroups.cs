using System.Numerics;

namespace TeletextRecoveReese.Core;

/// <summary>
/// Detects recurring contents, rather than turning every damaged reception into
/// a new page. A split needs repeated local signatures AND independently stable
/// differences outside those signatures. Otherwise recovery uses all copies.
/// </summary>
internal static class RecoveryContentGroups
{
    private const byte Unknown = 255;
    private const int BodyLength = 24 * 40;
    private const int SignatureLength = 8;
    private sealed record Copy(PageInstance Instance, byte[] Body);
    private sealed record Proposal(int Offset, List<List<Copy>> Seeds, int Support);

    public static List<List<PageInstance>> Split(
        IReadOnlyList<PageInstance> versions,
        CancellationToken cancellationToken)
    {
        var copies = versions.Select(instance =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new Copy(instance, ReadBody(instance));
        }).ToList();
        var groups = SplitCopies(copies, Math.Max(3, (copies.Count + 9) / 10), cancellationToken);
        return groups.Select(group => group.Select(copy => copy.Instance).OrderBy(v => v.VersionIndex).ToList())
            .OrderBy(group => group[0].VersionIndex).ToList();
    }

    private static List<List<Copy>> SplitCopies(List<Copy> copies, int minimumSupport, CancellationToken cancellationToken)
    {
        // Require at least three supporting receptions and 10% of the original address.
        // Neither a singleton nor a handful of outliers in a long capture can
        // establish another virtual page.
        if (copies.Count < minimumSupport * 2)
            return new() { copies };

        var proposals = new List<Proposal>();
        for (int offset = 0; offset < BodyLength; offset += SignatureLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var signatures = new Dictionary<ulong, List<Copy>>();
            foreach (var copy in copies)
            {
                if (!TrySignature(copy.Body, offset, out ulong signature)) continue;
                if (!signatures.TryGetValue(signature, out var members))
                    signatures[signature] = members = new();
                members.Add(copy);
            }
            // A noisy reception can miss this eight-byte signature and still
            // match the resulting model. Seed support is therefore lower than
            // the support required from the final, whole-content assignments.
            int seedSupport = Math.Max(3, (minimumSupport + 2) / 3);
            var seeds = signatures.Values.Where(group => group.Count >= seedSupport)
                .OrderByDescending(group => group.Count).ToList();
            if (seeds.Count >= 2)
                proposals.Add(new(offset, seeds, seeds.Skip(1).Sum(group => group.Count)));
        }

        // Test the best recurring signatures first. All candidate partitions
        // still have to pass independent validation; signature inequality alone
        // (like inequality of a calculated CRC) is never evidence of a new page.
        foreach (var proposal in proposals.OrderByDescending(p => p.Support).ThenBy(p => p.Offset).Take(12))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var models = proposal.Seeds.Select(seed => Consensus(seed, cancellationToken)).ToList();
            if (!DistinctModels(models, proposal.Offset)) continue;
            var partitions = models.Select(_ => new List<Copy>()).ToList();
            foreach (var copy in copies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int match = Match(copy.Body, models);
                if (match >= 0) partitions[match].Add(copy);
                // Ambiguous damaged copies cannot establish another page or
                // pollute a confirmed variant's consensus.
            }
            if (partitions.Any(group => group.Count < minimumSupport)) continue;
            // Most of the capture must support this explanation of its contents.
            if (partitions.Sum(group => group.Count) * 100 < copies.Count * 60) continue;
            // Reconfirm the differences using all assigned copies, not just
            // the clean copies that happened to share the proposing signature.
            if (!DistinctModels(partitions.Select(group => Consensus(group, cancellationToken)).ToList(), proposal.Offset))
                continue;

            var result = new List<List<Copy>>();
            foreach (var group in partitions)
                result.AddRange(SplitCopies(group, minimumSupport, cancellationToken));
            return result;
        }
        return new() { copies };
    }

    private static byte[] ReadBody(PageInstance instance)
    {
        var body = Enumerable.Repeat(Unknown, BodyLength).ToArray();
        for (int row = 1; row <= 24; row++)
        {
            if (instance.Page.RawRows[row] is not { Length: 42 } packet) continue;
            for (int column = 0; column < 40; column++)
                if (BitOperations.PopCount(packet[column + 2]) % 2 == 1)
                    body[(row - 1) * 40 + column] = (byte)(packet[column + 2] & 0x7F);
        }
        return body;
    }

    private static bool TrySignature(byte[] body, int offset, out ulong signature)
    {
        signature = 0;
        for (int index = 0; index < SignatureLength; index++)
        {
            byte value = body[offset + index];
            if (value == Unknown) return false;
            signature |= (ulong)value << (index * 8);
        }
        return true;
    }

    private static byte[] Consensus(List<Copy> copies, CancellationToken cancellationToken)
    {
        var model = Enumerable.Repeat(Unknown, BodyLength).ToArray();
        var counts = new int[128];
        for (int offset = 0; offset < BodyLength; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Array.Clear(counts);
            int valid = 0;
            foreach (var copy in copies)
            {
                byte value = copy.Body[offset];
                if (value == Unknown) continue;
                counts[value]++;
                valid++;
            }
            int winner = 0;
            for (int value = 1; value < counts.Length; value++)
                if (counts[value] > counts[winner]) winner = value;
            if (counts[winner] >= 3 && valid * 2 >= copies.Count && counts[winner] * 100 >= valid * 80)
                model[offset] = (byte)winner;
        }
        return model;
    }

    private static bool DistinctModels(List<byte[]> models, int signatureOffset)
    {
        for (int left = 0; left < models.Count; left++)
        for (int right = left + 1; right < models.Count; right++)
        {
            int compared = 0, different = 0;
            for (int offset = 0; offset < BodyLength; offset++)
            {
                if (offset >= signatureOffset && offset < signatureOffset + SignatureLength) continue;
                if (models[left][offset] == Unknown || models[right][offset] == Unknown) continue;
                compared++;
                if (models[left][offset] != models[right][offset])
                {
                    different++;
                }
            }
            // Validate across other bytes, not the eight bytes used to propose
            // this grouping. Repeated random errors rarely form stable coherent
            // alternatives across sixteen additional positions.
            if (compared < 80 || different < 16) return false;
        }
        return true;
    }

    private static int Match(byte[] body, List<byte[]> models)
    {
        int best = -1;
        double bestScore = 0, secondScore = 0;
        for (int modelIndex = 0; modelIndex < models.Count; modelIndex++)
        {
            int compared = 0, matched = 0;
            for (int offset = 0; offset < BodyLength; offset++)
            {
                byte expected = models[modelIndex][offset];
                if (expected == Unknown || body[offset] == Unknown) continue;
                // Shared spaces and static borders cannot outweigh the evidence
                // in the actual changing content.
                bool informative = models.Any(model => model[offset] != Unknown && model[offset] != expected);
                if (!informative) continue;
                compared++;
                if (body[offset] == expected) matched++;
            }
            double score = compared >= 8 ? (double)matched / compared : 0;
            if (score > bestScore)
            {
                secondScore = bestScore;
                bestScore = score;
                best = modelIndex;
            }
            else if (score > secondScore) secondScore = score;
        }
        return bestScore >= 0.70 && bestScore - secondScore >= 0.15 ? best : -1;
    }
}
