-- Same privilege model as backend/scripts/Rotate-DbCredentials.ps1: data read/write only, no DDL, no TRUNCATE,
-- migration history read-only. Re-runnable. Needs psql variables: -v app_user=... -v db=...
-- (psql substitutes :"name" as a safely quoted identifier)
GRANT CONNECT ON DATABASE :"db" TO :"app_user";
GRANT USAGE ON SCHEMA public TO :"app_user";
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO :"app_user";
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO :"app_user";
REVOKE INSERT, UPDATE, DELETE ON public."__EFMigrationsHistory" FROM :"app_user";
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"app_user";
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO :"app_user";
