using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Base58Encoding.Benchmarks.Common;

namespace Base58Encoding.Benchmarks;

[SimpleJob(RuntimeMoniker.Net90)]
[SimpleJob(RuntimeMoniker.Net10_0)]
[DisassemblyDiagnoser(exportCombinedDisassemblyReport: true)]
[HideColumns("Job", "Error", "StdDev", "Median", "RatioSD")]
public class BoundsCheckComparisonBenchmark
{
    private const int Base = 58;
    private const int MaxStackallocByte = 512;
    private byte[] _testData = null!;
    private Base58 _base58 = null!;

    [Params(TestVectors.VectorType.BitcoinAddress, TestVectors.VectorType.SolanaAddress, TestVectors.VectorType.SolanaTx, TestVectors.VectorType.IPFSHash, TestVectors.VectorType.MoneroAddress, TestVectors.VectorType.FlickrTestData, TestVectors.VectorType.RippleTestData)]
    public TestVectors.VectorType VectorType { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _testData = TestVectors.GetVector(VectorType);
        _base58 = Base58.Bitcoin;
    }

    [Benchmark(Baseline = true)]
    public byte[] EncodeSafe()
    {
        var inputSpan = _testData.AsSpan();
        var size = (inputSpan.Length * 137 / 100) + 1;
        Span<byte> digits = size > MaxStackallocByte
            ? new byte[size]
            : stackalloc byte[size];

        int digitCount = 1;
        digits[0] = 0;

        foreach (byte b in inputSpan)
        {
            int carry = b;

            for (int i = 0; i < digitCount; i++)
            {
                carry += digits[i] << 8;
                carry = Math.DivRem(carry, Base, out int remainder);
                digits[i] = (byte)remainder;
            }

            while (carry > 0)
            {
                carry = Math.DivRem(carry, Base, out int remainder);
                digits[digitCount++] = (byte)remainder;
            }
        }

        // Create result array and copy digits
        var result = new byte[digitCount];
        digits.Slice(0, digitCount).CopyTo(result);
        return result;
    }

    [Benchmark]
    public byte[] EncodeUnsafe()
    {
        var inputSpan = _testData.AsSpan();
        var size = (inputSpan.Length * 137 / 100) + 1;
        Span<byte> digits = size > MaxStackallocByte
            ? new byte[size]
            : stackalloc byte[size];

        int digitCount = 1;
        digits[0] = 0;

        // Get reference once outside the loop
        ref byte digitsRef = ref MemoryMarshal.GetReference(digits);

        foreach (byte b in inputSpan)
        {
            int carry = b;

            for (int i = 0; i < digitCount; i++)
            {
                ref byte digit = ref Unsafe.Add(ref digitsRef, i);
                carry += digit << 8;
                carry = Math.DivRem(carry, Base, out int remainder);
                digit = (byte)remainder;
            }

            while (carry > 0)
            {
                carry = Math.DivRem(carry, Base, out int remainder);
                Unsafe.Add(ref digitsRef, digitCount) = (byte)remainder;
                digitCount++;
            }
        }

        // Create result array and copy digits
        var result = new byte[digitCount];
        digits.Slice(0, digitCount).CopyTo(result);
        return result;
    }

    [Benchmark]
    public unsafe byte[] EncodeFixed()
    {
        var inputSpan = _testData.AsSpan();
        var size = (inputSpan.Length * 137 / 100) + 1;
        Span<byte> digits = size > MaxStackallocByte
            ? new byte[size]
            : stackalloc byte[size];

        int digitCount = 1;
        digits[0] = 0;

        fixed (byte* digitsPtr = digits)
        {
            foreach (byte b in inputSpan)
            {
                int carry = b;

                for (int i = 0; i < digitCount; i++)
                {
                    byte* digitPtr = digitsPtr + i;
                    carry += (*digitPtr) << 8;
                    carry = Math.DivRem(carry, Base, out int remainder);
                    *digitPtr = (byte)remainder;
                }

                while (carry > 0)
                {
                    carry = Math.DivRem(carry, Base, out int remainder);
                    *(digitsPtr + digitCount) = (byte)remainder;
                    digitCount++;
                }
            }
        }

        // Create result array and copy digits
        var result = new byte[digitCount];
        digits.Slice(0, digitCount).CopyTo(result);
        return result;
    }
}