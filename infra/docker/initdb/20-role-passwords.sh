# Run by the postgres image's entrypoint when the data volume is created, after 10-roles.sql
# (infra/supabase/roles.sql, mounted unchanged): files of /docker-entrypoint-initdb.d run in
# sorted name order, and a *.sh file that is not executable is sourced.
#
# Gives the two roles of roles.sql their local password, from SAASBTP_MIGRATOR_PASSWORD and
# SAASBTP_API_PASSWORD (compose.dev.yml). psql reads each one from its environment with \getenv
# into a variable and quotes it as a literal with :'...', so that no password is written into
# the SQL text or onto a command line. The quoted delimiter keeps the shell from expanding it.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --no-psqlrc <<'SQL'
\getenv migrator_password SAASBTP_MIGRATOR_PASSWORD
\getenv api_password SAASBTP_API_PASSWORD
alter role saasbtp_migrator password :'migrator_password';
alter role saasbtp_api password :'api_password';
SQL
