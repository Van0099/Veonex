namespace Veonex.Core.Logging;

public readonly record struct LogEntry(
	DateTimeOffset Timestamp,
	LogLevel Level,
	string Category,
	string Message,
	Exception? Exception = null);