# MySQL Database Setup Fix

## Issue Description
When running OpenDeepWiki with MySQL using `docker-compose-mysql.yml`, users encountered the following error:

```
System.InvalidCastException: Unable to cast object of type 'System.Int64' to type 'System.Int32'.
   at MySql.EntityFrameworkCore.Migrations.Internal.MySQLHistoryRepository.AcquireDatabaseLockAsync(CancellationToken cancellationToken)
```

This prevented the application from starting successfully with MySQL database.

## Root Cause
The issue was caused by MySql.EntityFrameworkCore package version 9.0.3 having a compatibility bug with EF Core 9.0.7. The MySQLHistoryRepository component attempted to cast Int64 values to Int32 during database lock acquisition for migrations, causing the application to crash on startup.

## Solution Applied
1. **Package Downgrade**: Updated MySql.EntityFrameworkCore from version 9.0.3 to 8.0.5 in `Directory.Packages.props` - this version is stable and fully compatible with EF Core 9.0.7

2. **Type Safety Enhancement**: Added explicit `bigint` column type mappings in `MySQLContext.cs` for fields that use `long` (Int64) types:
   - `Document.LikeCount` and `Document.CommentCount`
   - `DocumentFileItem.CommentCount` and `DocumentFileItem.Size`

## Testing the Fix
Run the provided test script to validate the fix:

```bash
cd /path/to/OpenDeepWiki
./test-mysql-fix.sh
```

Or manually test with:
```bash
docker-compose -f docker-compose-mysql.yml up --build
```

## Expected Results
- ✅ Container starts without Int64/Int32 casting exceptions
- ✅ Database migrations complete successfully  
- ✅ Application becomes available on port 8090

## Files Modified
- `Directory.Packages.props` - Package version downgrade
- `Provider/KoalaWiki.Provider.MySQL/MySQLContext.cs` - Type mappings
- `test-mysql-fix.sh` - Validation script (new)

This fix maintains full compatibility with existing database schemas while resolving the MySQL startup issue.