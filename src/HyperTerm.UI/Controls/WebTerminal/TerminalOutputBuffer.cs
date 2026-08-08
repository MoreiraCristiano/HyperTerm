using System.Text;

namespace HyperTerm.UI.Controls;

internal sealed class TerminalOutputBuffer
{
    private const int DefaultMaxBufferedCharacters = 2 * 1024 * 1024;
    private const int DefaultMaxBatchCharacters = 128 * 1024;
    private readonly object gate = new();
    private readonly Queue<string> chunks = new();
    private readonly int maximumBufferedCharacters;
    private readonly int maximumBatchCharacters;
    private int bufferedCharacters;
    private bool completed;

    public TerminalOutputBuffer()
        : this(DefaultMaxBufferedCharacters, DefaultMaxBatchCharacters)
    {
    }

    internal TerminalOutputBuffer(
        int maximumBufferedCharacters,
        int maximumBatchCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBufferedCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBatchCharacters);
        this.maximumBufferedCharacters = maximumBufferedCharacters;
        this.maximumBatchCharacters = maximumBatchCharacters;
    }

    public bool HasData
    {
        get
        {
            lock (gate)
            {
                return chunks.Count > 0;
            }
        }
    }

    public bool Enqueue(string output)
    {
        if (output.Length == 0)
        {
            return false;
        }

        lock (gate)
        {
            while (!completed && bufferedCharacters > 0 &&
                   bufferedCharacters + output.Length > maximumBufferedCharacters)
            {
                Monitor.Wait(gate);
            }

            if (completed)
            {
                return false;
            }

            chunks.Enqueue(output);
            bufferedCharacters += output.Length;
            return true;
        }
    }

    public string? TryDrainBatch()
    {
        lock (gate)
        {
            if (chunks.Count == 0)
            {
                return null;
            }

            string firstChunk = chunks.Dequeue();
            bufferedCharacters -= firstChunk.Length;
            if (chunks.Count == 0 || firstChunk.Length >= maximumBatchCharacters)
            {
                Monitor.PulseAll(gate);
                return firstChunk;
            }

            var batch = new StringBuilder(
                Math.Min(maximumBatchCharacters, firstChunk.Length + bufferedCharacters));
            batch.Append(firstChunk);
            while (chunks.Count > 0 &&
                   batch.Length + chunks.Peek().Length <= maximumBatchCharacters)
            {
                string chunk = chunks.Dequeue();
                bufferedCharacters -= chunk.Length;
                batch.Append(chunk);
            }

            Monitor.PulseAll(gate);
            return batch.ToString();
        }
    }

    public void Complete()
    {
        lock (gate)
        {
            completed = true;
            Monitor.PulseAll(gate);
        }
    }
}
