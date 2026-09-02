/*
    FTMS - create the database.

    Run this FIRST, and only once. Everything after it is idempotent; this is not, deliberately -
    creating a database is not something you want a script to do twice by accident.

    The brief suggests the name FinancialTransactionsDb by example. This repository uses Ftms
    throughout - in appsettings.json, docker-compose.yml and the CI workflow - so the name is
    parameterised here rather than hard coded. If you change it, change the Database= segment of
    ConnectionStrings:FtmsDatabase in src/FTMS.Api/appsettings.json to match.

    design: doc 02. Collation is set explicitly rather than inherited from the server, because a
    case sensitive server default would change how status names compare and the seeded lookup
    rows would stop matching the smart enum.
*/

:setvar DatabaseName "Ftms"

USE [master];
GO

IF DB_ID(N'$(DatabaseName)') IS NULL
BEGIN
    PRINT 'Creating database $(DatabaseName).';

    CREATE DATABASE [$(DatabaseName)]
        COLLATE SQL_Latin1_General_CP1_CI_AS;
END
ELSE
BEGIN
    PRINT 'Database $(DatabaseName) already exists. Nothing to do.';
END
GO

-- Read committed snapshot isolation. Without it, the list endpoint's readers block behind a
-- writer holding row locks, which on SQL Server Express - four cores, ~1.4 GB buffer pool
-- (doc 07 section 2) - turns one slow write into a queue of slow reads.
IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'$(DatabaseName)' AND is_read_committed_snapshot_on = 0)
BEGIN
    PRINT 'Enabling READ_COMMITTED_SNAPSHOT.';

    ALTER DATABASE [$(DatabaseName)] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
END
GO
