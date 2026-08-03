using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EventStore.Persistence.Migrations.Postgres;

public sealed class PostgresUniqueConstraintViolationDetector : IUniqueConstraintViolationDetector
{
    private const string UniqueViolationSqlState = "23505";

    public bool IsUniqueConstraintViolation(Exception exception, string columnName)
    {
        var inner = exception is DbUpdateException dbEx ? dbEx.InnerException : exception;
        return inner is PostgresException { SqlState: UniqueViolationSqlState } pgEx &&
            pgEx.Message.Contains(columnName, StringComparison.OrdinalIgnoreCase);
    }
}
