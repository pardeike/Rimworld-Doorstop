using System.Collections.Concurrent;

namespace ILReloaderLib;

internal class Debouncer
{
	private readonly ConcurrentDictionary<string, Timer> changes = [];
	private readonly int debouncePeriod;
	private readonly Action<string> action;

	internal Debouncer(TimeSpan debouncePeriod, Action<string> action)
	{
		this.debouncePeriod = (int)debouncePeriod.TotalSeconds;
		this.action = action;
	}

	internal void Add(string filePath)
	{
		if (changes.TryGetValue(filePath, out var existingTimer))
		{
			_ = existingTimer.Change(debouncePeriod, Timeout.Infinite);
			return;
		}
		changes[filePath] = new Timer(_ => TimerCallback(filePath), null, debouncePeriod, Timeout.Infinite);
	}

	private void TimerCallback(string filePath)
	{
		_ = changes.TryRemove(filePath, out var timer);
		timer?.Dispose();
		action(filePath);
	}
}
