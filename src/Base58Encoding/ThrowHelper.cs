using System.Diagnostics.CodeAnalysis;

namespace Base58Encoding;

internal static class ThrowHelper
{
    [DoesNotReturn]
    public static void ThrowInvalidCharacter(char character)
    {
        throw new ArgumentException($"Invalid Base58 character: '{character}'");
    }

    [DoesNotReturn]
    public static void ThrowDestinationTooSmall(string paramName)
    {
        throw new ArgumentException("Destination buffer is too small.", paramName);
    }

    [DoesNotReturn]
    public static void ThrowNegativeLength(string paramName)
    {
        throw new ArgumentOutOfRangeException(paramName, "Length must be non-negative.");
    }
}
