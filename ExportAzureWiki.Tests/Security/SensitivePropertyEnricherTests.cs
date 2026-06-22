using ExportAzureWiki.Platform.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ExportAzureWiki.Tests.Security;

public sealed class SensitivePropertyEnricherTests
{
    private static (ILogger logger, List<LogEvent> sink) BuildLoggerWithSink()
    {
        var captured = new List<LogEvent>();
        var sink = new CollectingSink(captured);
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.With(new SensitivePropertyEnricher())
            .WriteTo.Sink(sink)
            .CreateLogger();
        return (logger, captured);
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("PersonalAccessToken")]
    [InlineData("Pat")]
    [InlineData("Token")]
    [InlineData("AccessToken")]
    [InlineData("RefreshToken")]
    [InlineData("ClientSecret")]
    [InlineData("ApiKey")]
    [InlineData("Authorization")]
    public void Sensitive_Property_Is_Masked(string propertyName)
    {
        var (logger, sink) = BuildLoggerWithSink();
        logger.Information("event with {" + propertyName + "}", "the-real-secret");

        var rendered = sink.Single().Properties[propertyName].ToString();
        rendered.Should().Contain("***");
        rendered.Should().NotContain("the-real-secret");
    }

    [Fact]
    public void Recognition_Is_Case_Insensitive()
    {
        var (logger, sink) = BuildLoggerWithSink();
        logger.Information("event with {password}", "the-real-secret");

        sink.Single().Properties["password"].ToString().Should().NotContain("the-real-secret");
    }

    [Fact]
    public void Non_Sensitive_Property_Is_Untouched()
    {
        var (logger, sink) = BuildLoggerWithSink();
        logger.Information("event for {Username}", "alice");

        sink.Single().Properties["Username"].ToString().Should().Contain("alice");
    }

    [Fact]
    public void Mixed_Event_Masks_Only_Sensitive_Properties()
    {
        var (logger, sink) = BuildLoggerWithSink();
        logger.Information(
            "login {Username} {Password} on {Server}",
            "alice", "hunter2", "db.contoso.com");

        var evt = sink.Single();
        evt.Properties["Username"].ToString().Should().Contain("alice");
        evt.Properties["Password"].ToString().Should().NotContain("hunter2");
        evt.Properties["Server"].ToString().Should().Contain("db.contoso.com");
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly List<LogEvent> _events;
        public CollectingSink(List<LogEvent> events) => _events = events;
        public void Emit(LogEvent logEvent) => _events.Add(logEvent);
    }
}
