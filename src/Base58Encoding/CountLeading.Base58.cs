using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Base58Encoding;

public partial class Base58
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int CountLeadingZeros(ReadOnlySpan<byte> data)
    {
        if (data.Length < 64)
            return CountLeadingZerosScalar(data);

        int count = CountLeadingZerosSimd(data, out int processed);
        if (count < processed)
            return count;

        return count + CountLeadingZerosScalar(data.Slice(count));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        else if (Vector128.IsHardwareAccelerated && length >= Vector128<byte>.Count)
        {
            var zeroVector = Vector128<byte>.Zero;

            while (length >= Vector128<byte>.Count)
            {
                var vector = Vector128.LoadUnsafe(ref searchSpace, (nuint)count);
                var comparison = Vector128.Equals(vector, zeroVector);
                uint mask = comparison.ExtractMostSignificantBits();

                if (mask != ushort.MaxValue)
                {
                    processed = count + Vector128<byte>.Count;
                    return count + BitOperations.TrailingZeroCount(~mask);
                }

                count += Vector128<byte>.Count;
                length -= Vector128<byte>.Count;
            }
        }

        processed = count;
        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        int count = 0;
        ref char searchSpace = ref MemoryMarshal.GetReference(text);

        int length = text.Length;

        while (length >= 4 && count + 3 < text.Length)
        {
            ulong fourChars = Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<char, byte>(ref Unsafe.Add(ref searchSpace, count)));

            ulong targetPattern = ((ulong)target) | (((ulong)target) << 16) | (((ulong)target) << 32) | (((ulong)target) << 48);

            if (fourChars != targetPattern)
            {
                if (Unsafe.Add(ref searchSpace, count) != target) return count;
                if (Unsafe.Add(ref searchSpace, count + 1) != target) return count + 1;
                if (Unsafe.Add(ref searchSpace, count + 2) != target) return count + 2;
                return count + 3;
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
