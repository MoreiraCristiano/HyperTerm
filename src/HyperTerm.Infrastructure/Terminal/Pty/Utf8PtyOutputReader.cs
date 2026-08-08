using System.Text;

namespace HyperTerm.Infrastructure.Terminal;

internal static class Utf8PtyOutputReader
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    public static async Task ReadAsync(
        Stream stream,
        Action<string> outputReceived,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(outputReceived);

        byte[] buffer = new byte[64 * 1024];
        char[] characters = new char[Utf8.GetMaxCharCount(buffer.Length)];
        Decoder decoder = Utf8.GetDecoder();
        while (!cancellationToken.IsCancellationRequested)
        {
            int count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                int remaining = decoder.GetChars(
                    ReadOnlySpan<byte>.Empty,
                    characters,
                    flush: true);
                if (remaining > 0)
                {
                    outputReceived(new string(characters, 0, remaining));
                }

                return;
            }

            int characterCount = decoder.GetChars(
                buffer.AsSpan(0, count),
                characters,
                flush: false);
            if (characterCount > 0)
            {
                outputReceived(new string(characters, 0, characterCount));
            }
        }
    }
}
