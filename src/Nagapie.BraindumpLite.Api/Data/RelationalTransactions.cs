using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Nagapie.BraindumpLite.Api.Data;

internal static class RelationalTransactions
{
    internal static async Task<IDbContextTransaction> BeginWriteAsync(NagapieDbContext database, string userId, CancellationToken cancellationToken)
    {
        var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            if (database.Database.IsSqlServer())
            {
                // Serialize one user's writes (including reset/import), while row versions still allow independent edits.
                var resource = "Nagapie:data:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId)));
                await database.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @result < 0 THROW 51000, 'Could not acquire the user data lock.', 1;", cancellationToken);
            }
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}
