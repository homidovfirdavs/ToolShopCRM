-- Creates/updates the least-privilege application role. Password comes from the environment (APP_DB_PASSWORD), never from a file.
-- usage: psql -v ON_ERROR_STOP=1 -f ensure-role.sql        (run as the owner/admin account)
SET log_statement = 'none';
\getenv app_user APP_DB_USER
\getenv app_pw APP_DB_PASSWORD
SELECT format('CREATE ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD %L', :'app_user', :'app_pw')
 WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'app_user') \gexec
SELECT format('ALTER ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD %L', :'app_user', :'app_pw')
 WHERE EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'app_user') \gexec
