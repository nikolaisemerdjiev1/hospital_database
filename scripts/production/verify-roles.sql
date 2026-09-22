-- Read-only role/configuration check; contains no application data or password queries.
BEGIN READ ONLY;
DO $$
BEGIN
    IF current_database() <> 'hospital_coordination' THEN
        RAISE EXCEPTION 'Wrong database';
    END IF;
    IF (SELECT count(*) FROM pg_roles WHERE rolname IN ('hospital_runtime', 'hospital_maintenance')) <> 2
       OR EXISTS (SELECT FROM pg_roles WHERE rolname IN ('hospital_runtime', 'hospital_maintenance')
            AND (NOT rolcanlogin OR rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls))
       OR EXISTS (SELECT FROM pg_auth_members WHERE member IN ('hospital_runtime'::regrole, 'hospital_maintenance'::regrole)) THEN
        RAISE EXCEPTION 'Role configuration requires review';
    END IF;
    IF has_database_privilege('hospital_runtime', current_database(), 'CREATE')
       OR has_database_privilege('hospital_runtime', current_database(), 'TEMPORARY')
       OR has_schema_privilege('hospital_runtime', 'public', 'CREATE') THEN
        RAISE EXCEPTION 'Runtime has excessive creation privileges';
    END IF;
END $$;
SELECT current_database() AS database, current_user AS connected_role;
SELECT rolname, rolcanlogin, rolsuper, rolcreatedb, rolcreaterole, rolreplication, rolbypassrls
FROM pg_roles WHERE rolname IN ('hospital_runtime', 'hospital_maintenance') ORDER BY rolname;
SELECT count(*) AS application_tables FROM pg_tables WHERE schemaname = 'public';
COMMIT;
