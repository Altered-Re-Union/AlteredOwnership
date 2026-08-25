using AlteredOwnership.Server.Infrastructure.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AlteredOwnership.Server.Tests.Infrastructure;

public class UnhandledExceptionLoggerTests
{
    private sealed class RecordingLogger : ILogger<UnhandledExceptionLogger>
    {
        public LogLevel? LastLevel;
        public Exception? LastException;
        public string? LastMessage;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            LastLevel = logLevel;
            LastException = exception;
            LastMessage = formatter(state, exception);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    [Fact]
    public async Task Logs_the_exception_at_error_level_and_leaves_it_to_ProblemDetails()
    {
        var recorder = new RecordingLogger();
        var handler = new UnhandledExceptionLogger(recorder);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/admin/rewards";
        var exception = new InvalidOperationException("boom");

        // Must return false: it only observes/logs, the built-in ProblemDetails
        // handler (registered via AddProblemDetails()) still has to write the response.
        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.False(handled);
        Assert.Equal(LogLevel.Error, recorder.LastLevel);
        Assert.Same(exception, recorder.LastException);
        Assert.Contains("/api/admin/rewards", recorder.LastMessage);
    }
}
