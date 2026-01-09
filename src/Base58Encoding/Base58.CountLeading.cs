using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Base58Encoding;

public partial class Base58
{
    internal static int CountLeadingZeros(ReadOnlySpan<byte> data)
    {
        if (data.Length < 32)
            return CountLeadingZerosScalar(data);

        int count = CountLeadingZerosSimd(data, out int processed);
        if (count < processed)
            return count;

        return count + CountLeadingZerosScalar(data.Slice(count));
    }

    internal static int CountLeadingZerosSimd(ReadOnlySpan<byte> data, out int processed)
    {
        int count = 0;
        int length = data.Length;
        ref byte searchSpace = ref MemoryMarshal.GetReference(data);

        if (Vector256.IsHardwareAccelerated && length >= Vector256<byte>.Count)
        {
            var zeroVector = Vector256<byte>.Zero;

            while (length >= Vector256<byte>.Count)
            {
                var vector = Vector256.LoadUnsafe(ref searchSpace, (nuint)count);
                var comparison = Vector256.Equals(vector, zeroVector);
                uint mask = comparison.ExtractMostSignificantBits();

                if (mask != uint.MaxValue)
                {
                    processed = count + Vector256<byte>.Count;
                    return count + BitOperations.TrailingZeroCount(~mask);
                }

                count += Vector256<byte>.Count;
                length -= Vector256<byte>.Count;
            }
        }

        processed = count;
        return count;
    }

    internal static int CountLeadingZerosScalar(ReadOnlySpan<byte> data)
    {
        int count = 0;
        int length = data.Length;
        ref byte searchSpace = ref MemoryMarshal.GetReference(data);

        while (length >= sizeof(ulong))
        {
            ulong value = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref searchSpace, count));
            if (value != 0)
            {
                if (BitConverter.IsLittleEndian)
                {
                    count += BitOperations.TrailingZeroCount(value) / 8;
                }
                else
                {
                    count += BitOperations.LeadingZeroCount(value) / 8;
                }
                return count;
            }
            count += sizeof(ulong);
            length -= sizeof(ulong);
        }

        while (count < data.Length && data[count] == 0)
        {
            count++;
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int CountLeadingCharacters(ReadOnlySpan<char> text, char target)
    {
        int mismatchIndex = text.IndexOfAnyExcept(target);

        return mismatchIndex == -1 ? text.Length : mismatchIndex;
    }
}
