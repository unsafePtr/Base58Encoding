namespace Base58Encoding.Tests;

// VectorMath.ExtractBase58Digits replaces four constant divisions per limb with lane-wise
// reciprocal multiplication. The reciprocals are the risk: a multiplier/shift pair that is off by
// one anywhere in the 0 .. 58^5 domain produces a wrong digit for a narrow band of inputs that
// random testing would rarely hit. So the fast test pins the kernel against the scalar formula on
// the values most likely to expose an off-by-one, and the explicit test proves the pairs outright.
public class DigitExtractionTests
{
    private const uint Base58Pow5 = 656356768U; // 58^5

    [Theory]
    [InlineData(9)]  // IntermediateSz32
    [InlineData(18)] // IntermediateSz64
    [InlineData(1)]  // scalar tail only
    [InlineData(3)]  // shorter than a 256-bit vector
    [InlineData(5)]  // one 256-bit vector plus a tail
    public void ExtractBase58Digits_MatchesScalarFormula_OnBoundaryValues(int limbCount)
    {
        foreach (ulong[] limbs in BoundaryLimbSets(limbCount))
        {
            byte[] expected = new byte[limbCount * 5];
            ScalarExtract(limbs, expected);

            byte[] actual = new byte[limbCount * 5];
            VectorMath.ExtractBase58Digits(limbs, actual);

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void ExtractBase58Digits_MatchesScalarFormula_OnRandomLimbs()
    {
        var rng = new Random(58);

        for (int limbCount = 1; limbCount <= 20; limbCount++)
        {
            for (int trial = 0; trial < 50; trial++)
            {
                ulong[] limbs = new ulong[limbCount];
                for (int i = 0; i < limbCount; i++)
                {
                    limbs[i] = (ulong)rng.NextInt64(Base58Pow5);
                }

                byte[] expected = new byte[limbCount * 5];
                ScalarExtract(limbs, expected);

                byte[] actual = new byte[limbCount * 5];
                VectorMath.ExtractBase58Digits(limbs, actual);

                Assert.Equal(expected, actual);
            }
        }
    }

    // Proves the four multiplier/shift pairs against the true quotient for every legal limb value.
    // Explicit because it is 2.6 billion comparisons; it is the check that makes the fast tests
    // above sampling rather than hoping.
    //   dotnet run --project src/Base58Encoding.Tests -c Release -- -explicit only -method "*Reciprocals*"
    [Fact(Explicit = true)]
    [Trait("Category", "Fuzz")]
    public void Reciprocals_AreExactOverTheWholeLimbDomain()
    {
        AssertExact(58U, 592409283UL, 35);
        AssertExact(3364U, 326846501UL, 40);
        AssertExact(195112U, 11270569UL, 41);
        AssertExact(11316496U, 795935355UL, 53);
    }

    private static void AssertExact(uint divisor, ulong multiplier, int shift)
    {
        for (uint v = 0; v < Base58Pow5; v++)
        {
            if ((uint)(v * multiplier >> shift) != v / divisor)
            {
                Assert.Fail($"reciprocal for /{divisor} (multiplier {multiplier}, shift {shift}) is wrong at v={v}");
            }
        }
    }

    // Values where a quotient rolls over are where an inexact reciprocal shows up first: each
    // multiple of a divisor, and the value just below it.
    private static IEnumerable<ulong[]> BoundaryLimbSets(int limbCount)
    {
        List<ulong> interesting = [0UL, 1UL, 57UL, 58UL, 59UL, Base58Pow5 - 1UL];

        foreach (uint divisor in (uint[])[58U, 3364U, 195112U, 11316496U])
        {
            for (uint k = 1; k <= 60; k++)
            {
                ulong multiple = (ulong)divisor * k;
                if (multiple >= Base58Pow5)
                {
                    break;
                }

                interesting.Add(multiple - 1UL);
                interesting.Add(multiple);
                interesting.Add(multiple + 1UL);
            }
        }

        // Walk the interesting values through every lane position, so a lane-indexing mistake in
        // the packed store cannot hide behind a uniform vector.
        for (int offset = 0; offset < interesting.Count; offset += limbCount)
        {
            ulong[] limbs = new ulong[limbCount];
            for (int i = 0; i < limbCount; i++)
            {
                limbs[i] = interesting[(offset + i) % interesting.Count];
            }

            yield return limbs;
        }
    }

    private static void ScalarExtract(ReadOnlySpan<ulong> intermediate, Span<byte> raw)
    {
        for (int i = 0; i < intermediate.Length; i++)
        {
            uint v = (uint)intermediate[i];
            raw[(5 * i) + 4] = (byte)(v % 58U);
            raw[(5 * i) + 3] = (byte)(v / 58U % 58U);
            raw[(5 * i) + 2] = (byte)(v / 3364U % 58U);
            raw[(5 * i) + 1] = (byte)(v / 195112U % 58U);
            raw[(5 * i) + 0] = (byte)(v / 11316496U);
        }
    }
}
