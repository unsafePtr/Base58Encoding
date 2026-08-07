namespace Base58Encoding;

public static partial class Base58
{
    /// <summary>
    /// Returns a safe upper bound for the number of encoded Base58 characters
    /// produced from an input of the given byte length.
    /// </summary>
    /// <param name="byteCount">Length of the input data in bytes.</param>
    /// <returns>Maximum number of characters/bytes written by <c>Encode</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="byteCount"/> is negative, or so large that the encoded
    /// length would exceed <see cref="int.MaxValue"/>.
    /// </exception>
    /// <remarks>
    /// 137 = ceil(100 * log(256)/log(58)); sufficient for every length because 58^137 &gt; 2^800,
    /// and 136 is not. Do not shrink it or the <c>+ 1</c>, which covers the leading digit.
    /// </remarks>
    public static int GetMaxEncodedLength(int byteCount)
    {
        if (byteCount < 0)
        {
            ThrowHelper.ThrowNegativeLength(nameof(byteCount));
        }

        if (byteCount == 0)
        {
            return 0;
        }

        long maxLength = (long)byteCount * 137 / 100 + 1;

        if (maxLength > int.MaxValue)
        {
            ThrowHelper.ThrowInputTooLarge(nameof(byteCount));
        }

        return (int)maxLength;
    }

    /// <summary>
    /// Returns a typical upper bound for the number of decoded bytes produced
    /// from a Base58 input of the given length. Suitable for sizing destination
    /// buffers for the common case where the input has no leading '1' characters.
    /// </summary>
    /// <param name="encodedLength">Length of the encoded input (chars or ASCII bytes).</param>
    /// <returns>Typical maximum number of bytes written by <c>Decode</c>.</returns>
    /// <remarks>
    /// Base58 expansion is asymmetric: ordinary content decodes at about 0.733 bytes
    /// per character, but each leading '1' decodes 1:1 to a zero byte. This method
    /// returns the typical-case bound (<c>encodedLength * 733 / 1000 + 1</c>).
    /// For inputs containing leading '1' characters the actual decoded length can
    /// exceed this bound — up to <paramref name="encodedLength"/> in the degenerate
    /// all-'1's case. If you need a safe bound for arbitrary inputs, size the
    /// destination at <paramref name="encodedLength"/>. If you already have the
    /// input and want a tight bound, count leading '1's (<c>L</c>) and use
    /// <c>L + (encodedLength - L) * 733 / 1000 + 1</c>.
    /// </remarks>
    public static int GetTypicalDecodedLength(int encodedLength)
    {
        if (encodedLength < 0)
        {
            ThrowHelper.ThrowNegativeLength(nameof(encodedLength));
        }

        if (encodedLength == 0)
        {
            return 0;
        }

        return (int)((long)encodedLength * 733 / 1000 + 1);
    }
}
