using System.Text;

namespace HyperTerm.UI.Controls;

internal sealed class TerminalOutputBuffer
{
    private const int MaxBufferedCharacters = 2 * 1024 * 1024;
    private const int MaxBatchCharacters = 128 * 1024;
    private readonly object gate = new();
    private readonly Queue<string> chunks = new();
    private int bufferedCharacters;
    private bool completed;

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
            while (!completed && bufferedCharacters >= MaxBufferedCharacters)
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
            if (chunks.Count == 0 || firstChunk.Length >= MaxBatchCharacters)
            {
                Monitor.PulseAll(gate);
                return firstChunk;
            }

            var batch = new StringBuilder(
                Math.Min(MaxBatchCharacters, firstChunk.Length + bufferedCharacters));
            batch.Append(firstChunk);
            while (chunks.Count > 0 && batch.Length < MaxBatchCharacters)
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
