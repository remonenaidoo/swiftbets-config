-- Settings in force, one row per key, and every version each key has had.
CREATE SCHEMA IF NOT EXISTS config;

CREATE TABLE config.settings (
    key         text        PRIMARY KEY,
    value       text        NOT NULL,
    version     bigint      NOT NULL CHECK (version > 0),
    changed_by  text        NOT NULL,
    reason      text        NOT NULL,
    changed_at  timestamptz NOT NULL
);

CREATE TABLE config.history (
    key         text        NOT NULL,
    version     bigint      NOT NULL,
    value       text        NOT NULL,
    changed_by  text        NOT NULL,
    reason      text        NOT NULL,
    changed_at  timestamptz NOT NULL,
    PRIMARY KEY (key, version)
);
