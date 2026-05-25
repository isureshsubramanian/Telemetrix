namespace Telemetrix.Models;

/// <summary>
/// A captured database command, including its parameter values and the line of
/// application code that issued it. Populated for spans whose <see cref="SpanRecord.Source"/>
/// is <see cref="SpanSource.Sql"/>.
/// </summary>
public sealed class SqlCommandInfo
{
    /// <summary>The raw command text exactly as sent to the database.</summary>
    public required string CommandText { get; init; }

    /// <summary>The command text re-indented for readability in the inspector.</summary>
    public string? FormattedSql { get; init; }

    /// <summary>The diagnostic source that produced the command (for example <c>Microsoft.EntityFrameworkCore</c>).</summary>
    public required string Provider { get; init; }

    /// <summary>The database / data source the command ran against, when known.</summary>
    public string? Database { get; init; }

    /// <summary><c>Text</c> or <c>StoredProcedure</c>.</summary>
    public string? CommandType { get; init; }

    /// <summary>The ADO.NET execution method, for example <c>ExecuteReader</c> or <c>ExecuteNonQuery</c>.</summary>
    public string? ExecuteMethod { get; init; }

    /// <summary>How long the command took, in milliseconds.</summary>
    public double DurationMs { get; init; }

    /// <summary>Rows affected for non-query commands; <c>-1</c> when not reported.</summary>
    public int RowsAffected { get; init; } = -1;

    /// <summary><see langword="true"/> when the command threw.</summary>
    public bool IsError { get; init; }

    /// <summary>The exception message when <see cref="IsError"/> is set.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>The bound parameters, in declaration order.</summary>
    public IReadOnlyList<SqlParameterInfo> Parameters { get; init; } = [];

    /// <summary>The first application stack frame that issued the command.</summary>
    public CodeLocation? CodeLocation { get; init; }
}

/// <summary>A single bound database parameter.</summary>
/// <param name="Name">The parameter name including any provider prefix.</param>
/// <param name="Value">The bound value, formatted for display.</param>
/// <param name="DbType">The declared <see cref="System.Data.DbType"/>.</param>
/// <param name="Direction">Input, Output, InputOutput or ReturnValue.</param>
/// <param name="IsNull"><see langword="true"/> when the value is <c>NULL</c>/<c>DBNull</c>.</param>
public sealed record SqlParameterInfo(
    string Name,
    string? Value,
    string? DbType,
    string Direction,
    bool IsNull);

/// <summary>Points at the application source line that triggered a captured operation.</summary>
/// <param name="File">Absolute source file path, when a PDB is available.</param>
/// <param name="Line">1-based line number, or <c>0</c> when unknown.</param>
/// <param name="Member">The declaring type and method name.</param>
/// <param name="Namespace">The declaring namespace, used to dim framework frames in the UI.</param>
public sealed record CodeLocation(
    string? File,
    int Line,
    string? Member,
    string? Namespace)
{
    /// <summary>Just the file name (no directory), convenient for compact display.</summary>
    public string? FileName => File is null ? null : System.IO.Path.GetFileName(File);
}
