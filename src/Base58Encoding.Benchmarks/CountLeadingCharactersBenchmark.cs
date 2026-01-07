using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Base58Encoding.Benchmarks;

[SimpleJob(RuntimeMoniker.Net90)]
[SimpleJob(RuntimeMoniker.Net10_0)]
[HideColumns("Job", "Error", "StdDev", "Median", "RatioSD")]
[MemoryDiagnoser]
public class CountLeadingCharactersBenchmark
{
    private static readonly string[] TestStrings = new[]
    {
        "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa", // 0 leading zeros (standard P2PKH)
        "111111111111111111114oLvT2", // Many leading zeros
        "1BoatSLRHtKNngkdXEeobR76b53LETtpyT", // 0 leading zeros (standard)

        "11111111111111111111111111111112", // 31 leading zeros (system program)
        "1111111QLbz7JHiBTspS962RLKV8GndWFwiEaqKM", // 1 leading zero
        "4TMFNY21gmHLT3HpPZDiXY6kQkGPpJsJRfisJ9T7rXdV", // Typical Solana address (0 leading zeros)
        "So11111111111111111111111111111111111111112", // Wrapped SOL (0 leading zeros)

        "QmYwAPJzv5CZsnA625s3Xf2nemtYgPpHdWEz79ojWnPbdG", // No leading zeros (typical)
        "11111118YbNzKyoNPP4TJLYwpB2B3NCvpNXJbRNcszU", // Many leading zeros
        "3aDawfe6rm6QnFhJGppyyHVxXfEhBGYPMD8nzfLKNJqq",
        "4vJ9JU1bJJE96FWSJKvHsmmFADCg4gpZQff4P3bkLKi", // 0 leading zeros
        "11BxRVsYgGSUZRSMaNcNjSzwMBYXqZQvBQr3BQznBFmpV" // 2 leading zeros
    };

    [Benchmark(Description = "Simple Loop")]
    public int SimpleLoop()
    {
        int totalCount = 0;
        foreach (var str in TestStrings)
        {
            totalCount += CountLeadingCharactersSimple(str, '1');
        }
        return totalCount;
    }

    [Benchmark(Baseline = true, Description = "Optimized sequential")]
    public int Optimized()
    {
        int totalCount = 0;
        foreach (var str in TestStrings)
        {
            totalCount += CountLeadingCharacters(str, '1');
        }
        return totalCount;
    }

    [Benchmark(Description = "IndexOfAnyExcept")]
    public int IndexOfAnyExcept()
    {
        int totalCount = 0;
        foreach (var str in TestStrings)
        {
            totalCount += CountLeadingCharactersExcept(str, '1');
        }

        return totalCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CountLeadingCharactersExcept(ReadOnlySpan<char> text, char target)
    {
        int mismatchIndex = text.IndexOfAnyExcept(target);

        return mismatchIndex == -1 ? text.Length : mismatchIndex;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountLeadingCharactersSimple(ReadOnlySpan<char> text, char target)
    {
        int count = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != target)
                break;
            count++;
        }
        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int CountLeadingCharacters(ReadOnlySpan<char> text, char target)
    {
        int count = 0;
        int length = text.Length;
        ref char searchSpace = ref MemoryMarshal.GetReference(text);
        ulong targetPattern = ((ulong)target) | (((ulong)target) << 16) | (((ulong)target) << 32) | (((ulong)target) << 48);

        while (length >= 4)
        {
            ulong fourChars = Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<char, byte>(ref Unsafe.Add(ref searchSpace, count)));

            if (fourChars != targetPattern)
            {
                if (BitConverter.IsLittleEndian)
                {
                    if ((fourChars & 0xFFFF) != target) return count;
                    if (((fourChars >> 16) & 0xFFFF) != target) return count + 1;
                    if (((fourChars >> 32) & 0xFFFF) != target) return count + 2;
                    return count + 3;
                }
                else
                {
                    if (((fourChars >> 48) & 0xFFFF) != target) return count;
                    if (((fourChars >> 32) & 0xFFFF) != target) return count + 1;
                    if (((fourChars >> 16) & 0xFFFF) != target) return count + 2;
                    return count + 3;
                }
            }

            count += 4;
            length -= 4;
        }

        while (count < text.Length && Unsafe.Add(ref searchSpace, count) == target)
        {
            count++;
        }

        return count;
    }
}