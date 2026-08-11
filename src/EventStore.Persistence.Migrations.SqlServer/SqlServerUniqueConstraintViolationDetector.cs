using EventStore.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EventStore.Persistence.Migrations.SqlServer;

public sealed class SqlServerUniqueConstraintViolationDetector : IUniqueConstraintViolationDetector
{
    public bool IsUniqueConstraintViolation(Exception exception, string columnName)
    {
        var inner = exception is DbUpdateException dbEx ? dbEx.InnerException : exception;
        return inner is SqlException sqlEx &&
            sqlEx.Errors.Cast<SqlError>().Any(e => e.Number is 2601 or 2627 &&
                e.Message.Contains(columnName, StringComparison.OrdinalIgnoreCase));
    }
}
