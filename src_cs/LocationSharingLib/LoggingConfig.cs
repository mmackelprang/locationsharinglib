using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace LocationSharingLib;

public static class LoggingConfig
{
    private static ILoggerFactory? _factory;

    public static ILoggerFactory CreateLoggerFactory()
    {
        if (_factory != null) return _factory;
        var logsDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logsDir);
        var logPath = Path.Combine(logsDir, "locationsharinglib-.log");
        var template = "[" + "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}" + "][{Level:u3}][{SourceContext}][{Message:lj}]" + "{NewLine}{Exception}";
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, outputTemplate: template, retainedFileCountLimit: 7)
            .CreateLogger();
        _factory = new SerilogLoggerFactory(Log.Logger, dispose: true);
        return _factory;
    }
}