using System;
using System.Collections.Generic;
using System.Threading;

namespace Veonex.Core.Logging;

public static class Logger
{
	private const int MaxHistoryEntries = 4096;
	private const int MaxPendingEntries = 4096;

	private static readonly object Sync = new();

	private static readonly List<LogEntry> History = [];
	private static readonly Queue<LogEntry> Pending = [];

	private static int _minimumLevel = (int)LogLevel.Trace;

	public static LogLevel MinimumLevel
	{
		get => (LogLevel)Volatile.Read(ref _minimumLevel);
		set => Volatile.Write(ref _minimumLevel, (int)value);
	}

	public static int HistoryCount
	{
		get
		{
			lock (Sync)
				return History.Count;
		}
	}

	public static void Trace(string category, string message)
		=> Log(LogLevel.Trace, category, message);

	public static void Debug(string category, string message)
		=> Log(LogLevel.Debug, category, message);

	public static void Info(string category, string message)
		=> Log(LogLevel.Info, category, message);

	public static void Warning(
		string category,
		string message,
		Exception? exception = null)
		=> Log(LogLevel.Warning, category, message, exception);

	public static void Error(
		string category,
		string message,
		Exception? exception = null)
		=> Log(LogLevel.Error, category, message, exception);

	public static void Critical(
		string category,
		string message,
		Exception? exception = null)
		=> Log(LogLevel.Critical, category, message, exception);

	public static void Log(
		LogLevel level,
		string category,
		string message,
		Exception? exception = null)
	{
		if (level < MinimumLevel || level == LogLevel.None)
			return;

		ArgumentException.ThrowIfNullOrWhiteSpace(category);
		ArgumentNullException.ThrowIfNull(message);

		LogEntry entry = new(
			DateTimeOffset.Now,
			level,
			category,
			message,
			exception);

		lock (Sync)
		{
			History.Add(entry);

			if (History.Count > MaxHistoryEntries)
				History.RemoveAt(0);

			if (Pending.Count >= MaxPendingEntries)
				Pending.Dequeue();

			Pending.Enqueue(entry);
		}
	}

	public static LogEntry[] GetHistory()
	{
		lock (Sync)
			return History.ToArray();
	}

	public static LogEntry[] DrainPending(
		int maxEntries = int.MaxValue)
	{
		if (maxEntries <= 0)
			return [];

		lock (Sync)
		{
			int count = Math.Min(maxEntries, Pending.Count);
			LogEntry[] entries = new LogEntry[count];

			for (int i = 0; i < count; i++)
				entries[i] = Pending.Dequeue();

			return entries;
		}
	}
}