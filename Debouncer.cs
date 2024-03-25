using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Doorstop
{
	internal class Debouncer
	{
		private readonly ConcurrentDictionary<string, Timer> changes = [];
		private readonly TimeSpan debouncePeriod;
		private readonly Action<string> action;

		internal Debouncer(TimeSpan debouncePeriod, Action<string> action)
		{
			this.debouncePeriod = debouncePeriod;
			this.action = action;
		}

		internal void Add(string filePath)
		{
			if (changes.TryGetValue(filePath, out var existingTimer))
			{
				existingTimer.Change(debouncePeriod, Timeout.InfiniteTimeSpan);
				return;
			}
			changes[filePath] = new Timer(_ => TimerCallback(filePath), null, debouncePeriod, Timeout.InfiniteTimeSpan);
		}

		private void TimerCallback(string filePath)
		{
			changes.TryRemove(filePath, out var timer);
			timer.Dispose();
			action(filePath);
		}
	}
}
