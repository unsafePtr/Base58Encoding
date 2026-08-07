using System.Diagnostics;
using System.Numerics;
using System.Text;

namespace Base58Encoding.Tests;

// Long-running differential fuzz. Explicit so it never runs in the normal suite. Two tests: a
// general one over all input lengths, and one focused on the Bitcoin 32/64-byte fast paths. Run:
//   dotnet run --project src/Base58Encoding.Tests -c Release -- -explicit only
// Override the duration (seconds) with the FUZZ_SECONDS environment variable. Add a -method filter
// to run just one (e.g. -method "*Bitcoin_32And64*").
//
// Ground truth is a BigInteger oracle (the literal definition of Base58), so the fuzz validates our
// code without trusting any third party. We also cross-check our encoder against SimpleBase's, but
// not SimpleBase.Decode(ours): its 5.6.2 decoder drops the most-significant byte on some lengths
// (ssg/SimpleBase#83, fixed in 5.6.3 — which we cannot take yet, see Directory.Packages.props).
public class SimpleBaseFuzzTests
{
    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    private readonly ITestOutputHelper _output;

    public SimpleBaseFuzzTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Explicit = true)]
    [Trait("Category", "Fuzz")]
    public void EncodeDecode_MatchesOracleAndSimpleBase_UnderRandomInput()
    {
        int seconds = int.TryParse(Environment.GetEnvironmentVariable("FUZZ_SECONDS"), out int s) && s > 0 ? s : 600;
        var rng = new Random(20260722);
        var sw = Stopwatch.StartNew();
        long iterations = 0;
        int maxLen = 0;

        while (sw.Elapsed.TotalSeconds < seconds)
        {
            byte[] data = NextInput(rng);
            maxLen = Math.Max(maxLen, data.Length);

            string ours = Base58.Bitcoin.Encode(data);

            string oracle = OracleEncode(data);
            if (!string.Equals(ours, oracle, StringComparison.Ordinal))
            {
                Assert.Fail($"Encode vs oracle mismatch @ iter {iterations}: input={Convert.ToHexString(data)}\n  ours  ={ours}\n  oracle={oracle}");
            }

            string theirs = SimpleBase.Base58.Bitcoin.Encode(data);
            if (!string.Equals(ours, theirs, StringComparison.Ordinal))
            {
                Assert.Fail($"Encode vs SimpleBase mismatch @ iter {iterations}: input={Convert.ToHexString(data)}\n  ours  ={ours}\n  theirs={theirs}");
            }

            byte[] roundTrip = Base58.Bitcoin.Decode(ours);
            if (!roundTrip.AsSpan().SequenceEqual(data))
            {
                Assert.Fail($"Round-trip mismatch @ iter {iterations}: input={Convert.ToHexString(data)} decoded={Convert.ToHexString(roundTrip)}");
            }

            byte[] oursFromTheirs = Base58.Bitcoin.Decode(theirs);
            if (!oursFromTheirs.AsSpan().SequenceEqual(data))
            {
                Assert.Fail($"Cross-decode (ours <- SimpleBase) mismatch @ iter {iterations}: input={Convert.ToHexString(data)} decoded={Convert.ToHexString(oursFromTheirs)}");
            }

            iterations++;
        }

        _output.WriteLine($"Fuzz OK: {iterations:N0} iterations in {sw.Elapsed.TotalSeconds:F0}s, max input {maxLen} bytes, zero mismatches.");
    }

    // Focused fuzz on the Bitcoin 32- and 64-byte fast paths (DecodeBitcoin{32,64}Fast and the
    // SIMD encode). Only 32/64-byte inputs; the MSB is kept non-zero most of the time so the encoding
    // lands in the fast-path length window (43-44 / 87-88 chars) and Decode takes the fast path.
    // Exercises the string and byte-span overloads of both Encode and Decode against the oracle.
    // Run this one alone with: -explicit only -method "*Bitcoin_32And64*"
    [Fact(Explicit = true)]
    [Trait("Category", "Fuzz")]
    public void Bitcoin_32And64_FastPaths_MatchOracle_UnderRandomInput()
    {
        int seconds = int.TryParse(Environment.GetEnvironmentVariable("FUZZ_SECONDS"), out int s) && s > 0 ? s : 600;
        var rng = new Random(58585858);
        var sw = Stopwatch.StartNew();
        long iterations = 0;

        Span<byte> encoded = stackalloc byte[128];
        Span<byte> decoded = stackalloc byte[64];

        while (sw.Elapsed.TotalSeconds < seconds)
        {
            int len = (iterations & 1) == 0 ? 32 : 64;
            byte[] data = new byte[len];
            rng.NextBytes(data);

            if (rng.Next(8) == 0)
            {
                // Exercise the fast encode path's leading-zero ('1'-prefix) handling.
                Array.Clear(data, 0, rng.Next(1, len + 1));
            }
            else if (data[0] == 0)
            {
                // Keep it in the fast-path length window so Decode hits DecodeBitcoin{32,64}Fast.
                data[0] = 1;
            }

            string ours = Base58.Bitcoin.Encode(data);

            string oracle = OracleEncode(data);
            if (!string.Equals(ours, oracle, StringComparison.Ordinal))
            {
                Assert.Fail($"[{len}B] Encode vs oracle mismatch @ iter {iterations}: input={Convert.ToHexString(data)}\n  ours  ={ours}\n  oracle={oracle}");
            }

            string theirs = SimpleBase.Base58.Bitcoin.Encode(data);
            if (!string.Equals(ours, theirs, StringComparison.Ordinal))
            {
                Assert.Fail($"[{len}B] Encode vs SimpleBase mismatch @ iter {iterations}: input={Convert.ToHexString(data)}\n  ours  ={ours}\n  theirs={theirs}");
            }

            // Byte-span encode overload must match the string encode.
            int encodedLength = Base58.Bitcoin.Encode(data, encoded);
            if (!encoded[..encodedLength].SequenceEqual(Encoding.ASCII.GetBytes(ours)))
            {
                Assert.Fail($"[{len}B] Encode-to-bytes mismatch @ iter {iterations}: input={Convert.ToHexString(data)}");
            }

            // Decode from chars (round-trip through the fast decode path).
            byte[] roundTrip = Base58.Bitcoin.Decode(ours);
            if (!roundTrip.AsSpan().SequenceEqual(data))
            {
                Assert.Fail($"[{len}B] Decode(string) round-trip mismatch @ iter {iterations}: input={Convert.ToHexString(data)} decoded={Convert.ToHexString(roundTrip)}");
            }

            // Decode from ASCII bytes into a destination span (byte-input fast path).
            int decodedLength = Base58.Bitcoin.Decode(encoded[..encodedLength], decoded);
            if (!decoded[..decodedLength].SequenceEqual(data))
            {
                Assert.Fail($"[{len}B] Decode(bytes) round-trip mismatch @ iter {iterations}: input={Convert.ToHexString(data)} decoded={Convert.ToHexString(decoded[..decodedLength].ToArray())}");
            }

            iterations++;
        }

        _output.WriteLine($"Bitcoin 32/64 fast-path fuzz OK: {iterations:N0} iterations in {sw.Elapsed.TotalSeconds:F0}s, zero mismatches.");
    }

    // Length distribution biased toward the 32/64-byte fast paths and short inputs; 1-in-4 inputs
    // get a run of leading zero bytes to exercise the '1'-prefix / leading-zero handling.
    private static byte[] NextInput(Random rng)
    {
        int len = rng.Next(100) switch
        {
            < 15 => 32,
            < 30 => 64,
            < 45 => rng.Next(1, 8),
            _ => rng.Next(1, 200),
        };

        byte[] data = new byte[len];
        rng.NextBytes(data);

        if (rng.Next(4) == 0)
        {
            Array.Clear(data, 0, rng.Next(1, len + 1));
        }

        return data;
    }

    // The literal definition of Base58: big-endian integer, repeated divide-by-58, one '1' per leading zero.
    private static string OracleEncode(byte[] data)
    {
        int zeros = 0;
        while (zeros < data.Length && data[zeros] == 0)
        {
            zeros++;
        }

        var num = new BigInteger(data, isUnsigned: true, isBigEndian: true);
        var sb = new StringBuilder();
        while (num > 0)
        {
            num = BigInteger.DivRem(num, 58, out BigInteger rem);
            sb.Insert(0, Alphabet[(int)rem]);
        }

        for (int i = 0; i < zeros; i++)
        {
            sb.Insert(0, '1');
        }

        return sb.ToString();
    }
}
