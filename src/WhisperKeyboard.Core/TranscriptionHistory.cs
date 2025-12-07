namespace WhisperKeyboard.Core;

public class TranscriptionHistory
{
    private const int MaxHistoryItems = 10;
    private readonly Queue<HistoryEntry> _entries = new();
    private readonly object _lock = new();

    public void AddEntry(TranscriptionResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Text))
            return;

        lock (_lock)
        {
            // Remove oldest if at capacity
            while (_entries.Count >= MaxHistoryItems)
            {
                _entries.Dequeue();
            }

            _entries.Enqueue(new HistoryEntry
            {
                FullText = result.Text,
                Timestamp = DateTime.Now
            });
        }
    }

    public List<HistoryEntry> GetEntries()
    {
        lock (_lock)
        {
            // Return newest first
            return _entries.Reverse().ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }
}
