-- =============================================================
-- Registro Territori - schema PostgreSQL (script unico)
-- Eseguire su un database vuoto, ad es.:
--   psql -h <host> -U <owner> -d territory -f db/001_schema.sql
-- Lo script e' idempotente sulle tabelle (IF NOT EXISTS).
-- Nomi e cognomi sono cifrati dall'applicazione (AES-GCM, prefisso enc:v1:): per questo le colonne sono text.
-- Il primo amministratore si crea dalla pagina di configurazione iniziale dell'applicazione.
-- =============================================================

BEGIN;

-- Database creati con il vecchio nome della tabella.
DO $$
BEGIN
    IF to_regclass('public.admin_user') IS NOT NULL AND to_regclass('public.users') IS NULL THEN
        ALTER TABLE admin_user RENAME TO users;
        ALTER INDEX IF EXISTS uq_admin_user_username RENAME TO uq_users_username;
        IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_admin_user_role') THEN
            ALTER TABLE users RENAME CONSTRAINT ck_admin_user_role TO ck_users_role;
        END IF;
    END IF;
END$$;

CREATE TABLE IF NOT EXISTS users (
    id              integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    username        varchar(50)  NOT NULL,
    display_name    text         NOT NULL, -- nome e cognome della persona (cifrato)
    password_hash   text         NOT NULL,
    security_stamp  uuid         NOT NULL DEFAULT gen_random_uuid(), -- cambia ad ogni cambio password: invalida le sessioni
    failed_attempts integer      NOT NULL DEFAULT 0,
    lockout_until   timestamptz  NULL,
    theme           smallint     NOT NULL DEFAULT 0 CHECK (theme IN (0, 1, 2)), -- 0=system 1=light 2=dark
    language        varchar(10)  NULL,
    role            smallint     NOT NULL DEFAULT 1 CONSTRAINT ck_users_role CHECK (role IN (1, 2, 3)), -- 1=amministratore 2=responsabile 3=assegnatore
    must_change_password boolean NOT NULL DEFAULT false, -- true per gli utenti creati da un amministratore, fino al primo cambio password
    created_at      timestamptz  NOT NULL DEFAULT now()
);

-- Database creati prima dei ruoli: gli utenti esistenti diventano amministratori.
ALTER TABLE users ADD COLUMN IF NOT EXISTS role smallint NOT NULL DEFAULT 1 CONSTRAINT ck_users_role CHECK (role IN (1, 2, 3));

-- Database creati prima del cambio password obbligatorio: gli utenti esistenti non devono cambiarla.
ALTER TABLE users ADD COLUMN IF NOT EXISTS must_change_password boolean NOT NULL DEFAULT false;

CREATE UNIQUE INDEX IF NOT EXISTS uq_users_username
    ON users (lower(username));

CREATE TABLE IF NOT EXISTS municipality (
    id   integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name varchar(100) NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_municipality_name
    ON municipality (lower(name));

CREATE TABLE IF NOT EXISTS territory (
    id              integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    municipality_id integer      NOT NULL REFERENCES municipality (id) ON DELETE RESTRICT,
    number          integer      NOT NULL CHECK (number > 0),
    name            varchar(100) NOT NULL,
    mymaps_id       varchar(100) NOT NULL,
    map_kml         text         NULL,
    map_fetched_at  timestamptz  NULL,
    CONSTRAINT uq_territory_municipality_number UNIQUE (municipality_id, number)
);

CREATE TABLE IF NOT EXISTS publisher (
    id         integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    first_name text        NOT NULL, -- cifrato
    last_name  text        NOT NULL, -- cifrato
    name_hash  varchar(64) NOT NULL, -- HMAC-SHA256 (hex) del nome normalizzato: unicita' senza salvare il nome in chiaro
    is_deleted boolean     NOT NULL DEFAULT false,
    deleted_at timestamptz NULL
);

-- Stesso nome/cognome (normalizzato) = stesso proclamatore: permette la riattivazione dei cancellati logicamente.
CREATE UNIQUE INDEX IF NOT EXISTS uq_publisher_name_hash
    ON publisher (name_hash);

CREATE TABLE IF NOT EXISTS assignment (
    id           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    territory_id integer     NOT NULL REFERENCES territory (id) ON DELETE CASCADE,
    publisher_id integer     NOT NULL REFERENCES publisher (id) ON DELETE RESTRICT,
    assigned_at  timestamptz NOT NULL,
    returned_at  timestamptz NULL,
    returned_by  smallint    NULL CHECK (returned_by IN (1, 2)), -- 1=proclamatore 2=amministratore
    returned_by_name text    NULL, -- nome dell'admin al momento della riconsegna (cifrato): lo storico non cambia se viene rinominato o eliminato
    token_hash   bytea       NULL,
    CONSTRAINT ck_assignment_returned_by_name CHECK (returned_by_name IS NULL OR returned_by = 2),
    CONSTRAINT ck_assignment_dates CHECK (returned_at IS NULL OR returned_at >= assigned_at),
    CONSTRAINT ck_assignment_open_has_token CHECK ((returned_at IS NULL) = (token_hash IS NOT NULL)),
    CONSTRAINT uq_assignment_token_hash UNIQUE (token_hash)
);

-- Un territorio puo' avere una sola assegnazione aperta.
CREATE UNIQUE INDEX IF NOT EXISTS uq_assignment_open_per_territory
    ON assignment (territory_id) WHERE returned_at IS NULL;

CREATE INDEX IF NOT EXISTS ix_assignment_territory_assigned
    ON assignment (territory_id, assigned_at DESC);

CREATE INDEX IF NOT EXISTS ix_assignment_publisher
    ON assignment (publisher_id);

-- Chiavi ASP.NET Core Data Protection condivise tra repliche.
CREATE TABLE IF NOT EXISTS data_protection_keys (
    id            integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    friendly_name text NULL,
    xml           text NULL
);

COMMIT;
