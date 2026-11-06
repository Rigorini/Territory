-- =============================================================
-- Versione 2: blocco degli accessi per coppia utente + rete del client.
-- Chi conosce uno username puo' bloccare l'account solo dalla propria rete (indirizzo IPv4 o prefisso /64 IPv6),
-- non al suo proprietario.
-- =============================================================

CREATE TABLE IF NOT EXISTS login_lockout (
    user_id         integer     NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    client_key      varchar(64) NOT NULL,
    failed_attempts integer     NOT NULL DEFAULT 0,
    lockout_until   timestamptz NULL,
    updated_at      timestamptz NOT NULL, -- ultimo tentativo fallito: le righe vecchie vengono eliminate
    PRIMARY KEY (user_id, client_key)
);

CREATE INDEX IF NOT EXISTS ix_login_lockout_updated_at
    ON login_lockout (updated_at);

-- Sostituite da login_lockout. Restano finche' una versione precedente dell'applicazione potrebbe leggerle.
COMMENT ON COLUMN users.failed_attempts IS 'Non piu'' usata: i tentativi falliti sono in login_lockout';
COMMENT ON COLUMN users.lockout_until IS 'Non piu'' usata: i blocchi sono in login_lockout';
