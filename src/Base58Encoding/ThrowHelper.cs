using System.Diagnostics.CodeAnalysis;

namespace Base58Encoding;

internal static class ThrowHelper
{
    [DoesNotReturn]
    public static void ThrowInvalidCharacter(char character)
    {
        throw new ArgumentException($"Invalid Base58 character: '{character}'");
    }
}
