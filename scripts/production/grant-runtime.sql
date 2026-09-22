-- Run as hospital_maintenance AFTER separately authorized migrations, before serving traffic.
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';
DO $$
DECLARE
    expected text[] := ARRAY['__EFMigrationsHistory','appointment','audit_event','availability_slot',
        'clinician_profile','consultation','fulfillment','medication','patient_profile',
        'pharmacist_profile','prescription','user_profile'];
BEGIN
    IF current_database() <> 'hospital_coordination' OR current_user <> 'hospital_maintenance' THEN
        RAISE EXCEPTION 'Wrong database or migration owner; grants refused';
    END IF;
    IF (SELECT array_agg(relname::text ORDER BY relname::text COLLATE "C") FROM pg_class
        WHERE relnamespace = 'public'::regnamespace AND relkind IN ('r','p')) IS DISTINCT FROM expected THEN
        RAISE EXCEPTION 'Unexpected table set; review grants for the actual migration';
    END IF;
    IF EXISTS (SELECT FROM pg_class WHERE relnamespace = 'public'::regnamespace
        AND relkind IN ('r','p','S','v','m','f') AND relowner <> current_user::regrole) THEN
        RAISE EXCEPTION 'Unexpected object owner; no ownership transfer will be attempted';
    END IF;
    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'hospital_runtime'
        AND (rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls))
       OR EXISTS (SELECT FROM pg_auth_members WHERE member = 'hospital_runtime'::regrole) THEN
        RAISE EXCEPTION 'Runtime role has elevated capabilities or membership';
    END IF;
    IF has_database_privilege('hospital_runtime', current_database(), 'CREATE')
       OR has_database_privilege('hospital_runtime', current_database(), 'TEMPORARY')
       OR has_schema_privilege('hospital_runtime', 'public', 'CREATE') THEN
        RAISE EXCEPTION 'Runtime creation privileges require review';
    END IF;
END $$;

REVOKE ALL ON ALL TABLES IN SCHEMA public FROM PUBLIC, hospital_runtime;
REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM PUBLIC, hospital_runtime;
DO $$
DECLARE
    item record;
BEGIN
    -- Table-level REVOKE does not remove earlier column-level privileges.
    FOR item IN SELECT c.relname, string_agg(quote_ident(a.attname), ',' ORDER BY a.attnum) AS columns
        FROM pg_class c JOIN pg_attribute a ON a.attrelid = c.oid
        WHERE c.relnamespace = 'public'::regnamespace AND c.relkind IN ('r','p')
          AND a.attnum > 0 AND NOT a.attisdropped GROUP BY c.relname
    LOOP
        EXECUTE format('REVOKE ALL (%s) ON TABLE public.%I FROM PUBLIC, hospital_runtime', item.columns, item.relname);
    END LOOP;
END $$;
GRANT SELECT ON user_profile, patient_profile, clinician_profile, pharmacist_profile,
    availability_slot, appointment, consultation, medication, prescription, fulfillment TO hospital_runtime;
GRANT INSERT, UPDATE ON appointment, consultation, medication, prescription, fulfillment TO hospital_runtime;
-- EF INSERT ... RETURNING id requires column-level SELECT, not audit-content access.
GRANT INSERT ON audit_event TO hospital_runtime;
GRANT SELECT (id) ON audit_event TO hospital_runtime;
-- IDENTITY ALWAYS columns generate IDs without granting nextval/setval on their sequences.
COMMIT;
