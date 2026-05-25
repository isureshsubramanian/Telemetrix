using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Telemetrix.Internal;
using Telemetrix.Models;
using Telemetrix.Storage;

namespace Telemetrix.Diagnostics;

/// <summary>
/// Subscribes to the <c>Microsoft.EntityFrameworkCore</c> diagnostic source and turns every
/// database command into a synthesised SQL span, complete with bound parameter values and the
/// originating line of application code. The EF Core payload types are accessed by reflection
/// so Telemetrix never has to take a compile-time dependency on Entity Framework Core.
/// </summary>
internal sealed class EfCoreCommandObserver : IObserver<KeyValuePair<string, object?>>
{
    private const string ProviderName = "Microsoft.EntityFrameworkCore";
    private const string ExecutingEvent = "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuting";
    private const string ExecutedEvent = "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted";
    private const string ErrorEvent = "Microsoft.EntityFrameworkCore.Database.Command.CommandError";

    private readonly TelemetrixStore _store;
    private readonly TelemetrixOptions _options;
    private readonly ConcurrentDictionary<Guid, PendingCommand> _pending = new();

    public EfCoreCommandObserver(TelemetrixStore store, TelemetrixOptions options)
    {
        _store = store;
        _options = options;
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    public void OnNext(KeyValuePair<string, object?> value)
    {
        try
        {
            var payload = value.Value;
            if (payload is null)
            {
                return;
            }

            switch (value.Key)
            {
                case ExecutingEvent:
                    HandleExecuting(payload);
                    break;
                case ExecutedEvent:
                    HandleCompleted(payload, exception: null);
                    break;
                case ErrorEvent:
                    HandleCompleted(payload, exception: PayloadReader.Get(payload, "Exception") as Exception);
                    break;
            }
        }
        catch
        {
            // Diagnostic capture must never break the host application.
        }
    }

    private void HandleExecuting(object payload)
    {
        var commandId = AsGuid(PayloadReader.Get(payload, "CommandId"));
        if (commandId == Guid.Empty)
        {
            return;
        }

        // Guard against leaks if a 'CommandExecuted' event is somehow missed.
        if (_pending.Count > 2000)
        {
            _pending.Clear();
        }

        var activity = Activity.Current;
        var location = _options.CaptureCodeLocation ? StackTraceResolver.Resolve() : null;

        _pending[commandId] = new PendingCommand(
            activity?.TraceId.ToHexString(),
            activity?.SpanId.ToHexString(),
            DateTime.UtcNow,
            location);
    }

    private void HandleCompleted(object payload, Exception? exception)
    {
        if (PayloadReader.Get(payload, "Command") is not DbCommand command)
        {
            return;
        }

        var commandId = AsGuid(PayloadReader.Get(payload, "CommandId"));
        _pending.TryRemove(commandId, out var pending);

        var duration = AsTimeSpan(PayloadReader.Get(payload, "Duration"));
        var startUtc = pending?.StartUtc ?? DateTime.UtcNow - duration;
        var commandText = command.CommandText ?? string.Empty;
        var operation = SqlFormatter.Operation(commandText);
        var parameters = ReadParameters(command);

        string? database = null;
        string? commandType = null;
        try
        {
            database = command.Connection?.Database;
        }
        catch
        {
            // Connection may already be closed.
        }

        try
        {
            commandType = command.CommandType.ToString();
        }
        catch
        {
            // Some providers throw for non-default command types.
        }

        var sql = new SqlCommandInfo
        {
            CommandText = commandText,
            FormattedSql = SqlFormatter.Format(commandText),
            Provider = ProviderName,
            Database = database,
            CommandType = commandType,
            ExecuteMethod = PayloadReader.Get(payload, "ExecuteMethod")?.ToString(),
            DurationMs = duration.TotalMilliseconds,
            RowsAffected = ReadRowsAffected(payload),
            IsError = exception is not null,
            ErrorMessage = exception?.Message,
            Parameters = parameters,
            CodeLocation = pending?.CodeLocation,
        };

        var span = new SpanRecord
        {
            TraceId = pending?.TraceId ?? ActivityTraceId.CreateRandom().ToHexString(),
            SpanId = ActivitySpanId.CreateRandom().ToHexString(),
            ParentSpanId = pending?.ParentSpanId,
            Name = $"DB {operation}",
            Kind = SpanKind.Client,
            Source = SpanSource.Sql,
            SourceName = ProviderName,
            StartTimeUtc = startUtc,
            DurationMs = duration.TotalMilliseconds,
            Status = exception is null ? SpanStatus.Ok : SpanStatus.Error,
            StatusDescription = exception?.Message,
            Tags = BuildTags(sql, operation, parameters.Count, exception),
            Sql = sql,
        };

        _store.AddSpan(span);
    }

    private List<SqlParameterInfo> ReadParameters(DbCommand command)
    {
        var parameters = new List<SqlParameterInfo>();
        try
        {
            foreach (DbParameter parameter in command.Parameters)
            {
                var isNull = parameter.Value is null or DBNull;
                string? value;
                if (isNull)
                {
                    value = null;
                }
                else if (_options.CaptureSqlParameters)
                {
                    value = ValueFormatter.Format(parameter.Value);
                }
                else
                {
                    value = "(value hidden)";
                }

                string? dbType = null;
                try
                {
                    dbType = parameter.DbType.ToString();
                }
                catch
                {
                    // Provider-specific parameter without a CLR DbType mapping.
                }

                parameters.Add(new SqlParameterInfo(
                    string.IsNullOrEmpty(parameter.ParameterName)
                        ? $"@p{parameters.Count}"
                        : parameter.ParameterName,
                    value,
                    dbType,
                    parameter.Direction.ToString(),
                    isNull));
            }
        }
        catch
        {
            // Parameter collection is best-effort.
        }

        return parameters;
    }

    private static int ReadRowsAffected(object payload)
        => PayloadReader.Get(payload, "Result") is int rows ? rows : -1;

    private static List<TagItem> BuildTags(SqlCommandInfo sql, string operation, int parameterCount, Exception? exception)
    {
        var tags = new List<TagItem>
        {
            new("db.system", "sql"),
            new("db.operation", operation),
            new("db.statement", ValueFormatter.Format(sql.CommandText)),
            new("db.parameter_count", parameterCount.ToString(CultureInfo.InvariantCulture)),
        };

        if (sql.Database is not null)
        {
            tags.Add(new TagItem("db.name", sql.Database));
        }

        if (sql.CommandType is not null)
        {
            tags.Add(new TagItem("db.command_type", sql.CommandType));
        }

        if (sql.ExecuteMethod is not null)
        {
            tags.Add(new TagItem("db.execute_method", sql.ExecuteMethod));
        }

        if (sql.RowsAffected >= 0)
        {
            tags.Add(new TagItem("db.rows_affected", sql.RowsAffected.ToString(CultureInfo.InvariantCulture)));
        }

        if (sql.CodeLocation is { } location)
        {
            if (location.File is not null)
            {
                tags.Add(new TagItem("code.filepath", location.File));
            }

            if (location.Line > 0)
            {
                tags.Add(new TagItem("code.lineno", location.Line.ToString(CultureInfo.InvariantCulture)));
            }

            if (location.Member is not null)
            {
                tags.Add(new TagItem("code.function", location.Member));
            }
        }

        if (exception is not null)
        {
            tags.Add(new TagItem("error", "true"));
            tags.Add(new TagItem("exception.type", exception.GetType().FullName ?? exception.GetType().Name));
            tags.Add(new TagItem("exception.message", exception.Message));
        }

        return tags;
    }

    private static Guid AsGuid(object? value) => value is Guid guid ? guid : Guid.Empty;

    private static TimeSpan AsTimeSpan(object? value) => value is TimeSpan span ? span : TimeSpan.Zero;

    private sealed record PendingCommand(
        string? TraceId,
        string? ParentSpanId,
        DateTime StartUtc,
        CodeLocation? CodeLocation);
}

/// <summary>
/// Reads named properties off arbitrary diagnostic-event payloads via cached reflection.
/// Lets Telemetrix consume EF Core / ADO.NET event data without referencing those assemblies.
/// </summary>
internal static class PayloadReader
{
    private static readonly ConcurrentDictionary<(Type Type, string Property), PropertyInfo?> Cache = new();

    public static object? Get(object payload, string propertyName)
    {
        var property = Cache.GetOrAdd(
            (payload.GetType(), propertyName),
            static key => key.Type.GetProperty(key.Property, BindingFlags.Public | BindingFlags.Instance));

        return property is null ? null : SafeGet(property, payload);
    }

    private static object? SafeGet(PropertyInfo property, object payload)
    {
        try
        {
            return property.GetValue(payload);
        }
        catch
        {
            return null;
        }
    }
}
