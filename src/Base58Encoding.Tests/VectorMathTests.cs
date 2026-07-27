namespace Base58Encoding.Tests;

// Direct tests for the two vector kernels in VectorMath.
//
// The Bitcoin fast paths only ever call these with four lengths — TensorMultiplyAdd with 8 and 17,
// TensorDot with 9 and 18 — so going through Encode/Decode leaves most of the branch matrix
// unexercised: no tail of length 3, no 0/1/3-iteration vector loops, and on AVX2 hardware the
// `else if (Vector128 ...)` arm is unreachable entirely (it needs 2 <= len < 4). These tests sweep
// length 0..20 against a plain scalar reference so every combination is covered.
//
// Each test also runs on the Vector128 and scalar paths, because VectorInstructionSetTests re-runs
// the whole suite in child processes with DOTNET_EnableAVX2=0 and DOTNET_EnableHWIntrinsic=0.
public class VectorMathTests
{
    // Both kernels require every operand to fit in 32 bits — see MultiplyWidening32. This is the
    // exclusive upper bound the fast paths guarantee.
    private const long OperandLimit = 1L << 32;

    private static ulong DotReference(ReadOnlySpan<ulong> x, ReadOnlySpan<ulong> y)
    {
        ulong sum = 0UL;
        for (int i = 0; i < x.Length; i++)
        {
            sum += x[i] * y[i]; // wraps mod 2^64, exactly like the kernel is documented to
        }

        return sum;
    }

    private static void MultiplyAddReference(ReadOnlySpan<ulong> row, ulong scale, Span<ulong> acc)
    {
        for (int i = 0; i < row.Length; i++)
        {
            acc[i] += row[i] * scale;
        }
    }

    private static ulong[] RandomOperands(Random rng, int length)
    {
        var values = new ulong[length];
        for (int i = 0; i < length; i++)
        {
            values[i] = (ulong)rng.NextInt64(0, OperandLimit);
        }

        return values;
    }

    // Sweeps every length that changes the branch structure: 0/1 (scalar only), 2/3 (the narrow arm),
    // 4..20 (vector loop with every possible tail 0..3).
    [Fact]
    public void TensorDot_MatchesScalarReference_ForEveryLength()
    {
        var rng = new Random(58);

        for (int length = 0; length <= 20; length++)
        {
            for (int iteration = 0; iteration < 200; iteration++)
            {
                ulong[] x = RandomOperands(rng, length);
                ulong[] y = RandomOperands(rng, length);

                Assert.Equal(DotReference(x, y), VectorMath.TensorDot(x, y));
            }
        }
    }

    [Fact]
    public void TensorMultiplyAdd_MatchesScalarReference_ForEveryLength()
    {
        var rng = new Random(85);

        for (int length = 0; length <= 20; length++)
        {
            for (int iteration = 0; iteration < 200; iteration++)
            {
                ulong[] row = RandomOperands(rng, length);
                ulong scale = (ulong)rng.NextInt64(0, OperandLimit);

                // Seed the accumulator with non-zero values: the kernel must ADD into it, not overwrite.
                ulong[] seed = RandomOperands(rng, length);
                ulong[] expected = (ulong[])seed.Clone();
                ulong[] actual = (ulong[])seed.Clone();

                MultiplyAddReference(row, scale, expected);
                VectorMath.TensorMultiplyAdd(row, scale, actual);

                Assert.Equal(expected, actual);
            }
        }
    }

    // The accumulator is only ever read and added to, never multiplied, so it is free to exceed 32 bits
    // — production relies on that, since limbs grow well past 2^32 before the reduce pass. The sweep
    // above only seeds it with values under 2^32, so this is the one place that covers it.
    [Fact]
    public void TensorMultiplyAdd_AccumulatorMayExceed32Bits()
    {
        var row = new ulong[9];
        var acc = new ulong[9];
        var expected = new ulong[9];
        Array.Fill(row, 656356767UL);        // 58^5 - 1, the largest table-side operand
        Array.Fill(acc, ulong.MaxValue - 7); // accumulator already far above 2^32
        Array.Fill(expected, ulong.MaxValue - 7);

        MultiplyAddReference(row, uint.MaxValue, expected);
        VectorMath.TensorMultiplyAdd(row, uint.MaxValue, acc);

        Assert.Equal(expected, acc);
    }

    // Boundary operands, which random sampling over [0, 2^32) would essentially never hit. Includes the
    // exact worst case the decode kernel can produce: max intermediate limb x max DecodeTable64 entry.
    // The uint.MaxValue row also covers 2^64 wrap-around — one term is 18446744065119617025, so from
    // length 2 upward the sum overflows, and the vector paths (which accumulate into 2 or 4 partials and
    // reduce at the end, so in a different order than scalar) must still agree bit-for-bit.
    [Theory]
    [InlineData(0UL, 0UL)]
    [InlineData(0UL, 4294967295UL)]
    [InlineData(1UL, 1UL)]
    [InlineData(4294967295UL, 4294967295UL)]   // both at the 32-bit ceiling; wraps from length 2 up
    [InlineData(656356767UL, 4264082837UL)]    // production worst case
    [InlineData(656356767UL, 656356767UL)]
    public void Kernels_HandleBoundaryOperands(ulong a, ulong b)
    {
        foreach (int length in new[] { 1, 2, 3, 4, 8, 9, 17, 18, 20 })
        {
            var x = new ulong[length];
            var y = new ulong[length];
            Array.Fill(x, a);
            Array.Fill(y, b);

            Assert.Equal(DotReference(x, y), VectorMath.TensorDot(x, y));

            var expected = new ulong[length];
            var actual = new ulong[length];
            MultiplyAddReference(x, b, expected);
            VectorMath.TensorMultiplyAdd(x, b, actual);
            Assert.Equal(expected, actual);
        }
    }

    // The precondition MultiplyWidening32 depends on, asserted at its source for both operand sides.
    // If a future table or a wider intermediate base ever broke this, the vector paths would silently
    // truncate — so check it against the shipped tables rather than trusting the comment.
    [Fact]
    public void MultiplyWidening32Precondition_HoldsForAllProductionOperands()
    {
        // Table side.
        foreach ((string name, ulong[] table) in new (string, ulong[])[]
        {
            (nameof(Base58BitcoinTables.EncodeTable32RowMajor), Base58BitcoinTables.EncodeTable32RowMajor),
            (nameof(Base58BitcoinTables.EncodeTable64RowMajor), Base58BitcoinTables.EncodeTable64RowMajor),
            (nameof(Base58BitcoinTables.DecodeTable32), Base58BitcoinTables.DecodeTable32),
            (nameof(Base58BitcoinTables.DecodeTable64), Base58BitcoinTables.DecodeTable64),
        })
        {
            for (int i = 0; i < table.Length; i++)
            {
                Assert.True(table[i] < (ulong)OperandLimit,
                    $"{name}[{i}] = {table[i]} exceeds 2^32; MultiplyWidening32 would truncate it.");
            }
        }

        // Limb side: an intermediate limb is at most 58^5 - 1, which is below 2^32.
        ulong maxLimb = (57UL * 11316496UL) + (57UL * 195112UL) + (57UL * 3364UL) + (57UL * 58UL) + 57UL;
        Assert.Equal(Base58BitcoinTables.R1Div - 1UL, maxLimb);
        Assert.True(maxLimb < (ulong)OperandLimit);
    }
}
