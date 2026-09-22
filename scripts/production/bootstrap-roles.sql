-- Run once as the Neon database owner, on the dedicated EMPTY database.
-- No passwords, migrations, seed data or existing-object ownership changes.
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';
DO $$
BEGIN
    IF current_database() <> 'hospital_coordination' THEN
        RAISE EXCEPTION 'Wrong database; role setup refused';
    END IF;
    IF EXISTS (SELECT FROM pg_roles WHERE rolname IN ('hospital_runtime', 'hospital_maintenance')) THEN
        RAISE EXCEPTION 'Roles already exist; inspect previous setup instead of overwriting';
    END IF;
    IF EXISTS (SELECT FROM pg_class WHERE relnamespace = 'public'::regnamespace)
       OR EXISTS (SELECT FROM pg_proc WHERE pronamespace = 'public'::regnamespace)
       OR EXISTS (SELECT FROM pg_type WHERE typnamespace = 'public'::regnamespace) THEN
        RAISE EXCEPTION 'Public schema is not empty; review ownership and privileges manually';
    END IF;
    IF EXISTS (SELECT FROM pg_namespace WHERE nspname NOT IN ('public', 'information_schema')
        AND nspname NOT LIKE 'pg_%') THEN
        RAISE EXCEPTION 'Unexpected user schema; dedicated-database setup refused';
    END IF;
END $$;

CREATE ROLE hospital_maintenance LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
CREATE ROLE hospital_runtime LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
-- The owner can administer migration defaults; neither app role inherits this owner.
GRANT hospital_maintenance TO CURRENT_USER WITH INHERIT FALSE, SET TRUE;
REVOKE ALL ON DATABASE hospital_coordination FROM PUBLIC;
GRANT CONNECT ON DATABASE hospital_coordination TO hospital_maintenance, hospital_runtime;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO hospital_maintenance;
GRANT USAGE ON SCHEMA public TO hospital_runtime;
ALTER ROLE hospital_maintenance IN DATABASE hospital_coordination SET search_path = public;
ALTER ROLE hospital_runtime IN DATABASE hospital_coordination SET search_path = public;
SET LOCAL ROLE hospital_maintenance;
ALTER DEFAULT PRIVILEGES REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;
RESET ROLE;
-- Deliberately no default runtime table/sequence grants. Review each migration.
COMMIT;
