using System.Diagnostics.CodeAnalysis;

namespace Base58Encoding;
public static class ThrowHelper
{
    [DoesNotReturn]
    public static void ThrowNotExactLength()
    {
        throw new InvalidOperationException("Alphabet must be exactly 58 characters long.");
    }

    [DoesNotReturn]
    public static void ThrowInvalidCharacter(char character)
    {
        throw new ArgumentException($"Invalid Base58 character: '{character}'");
    }
}
