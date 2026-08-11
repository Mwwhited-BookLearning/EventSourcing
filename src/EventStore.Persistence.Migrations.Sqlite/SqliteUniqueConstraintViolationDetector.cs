using EventStore.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Persistence.Migrations.Sqlite;

public sealed class SqliteUniqueConstraintViolationDetector : IUniqueConstraintViolationDetector
{
    public bool IsUniqueConstraintViolation(Exception exception, string columnName)
    {
        var inner = exception is DbUpdateException dbEx ? dbEx.InnerException : exception;
        return inner is SqliteException { SqliteErrorCode: 19 } sqliteEx &&
            sqliteEx.Message.Contains(columnName, StringComparison.OrdinalIgnoreCase);
    }
}
