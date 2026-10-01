INSERT INTO config.settings (key, value, version, changed_by, reason, changed_at)
VALUES (@Key, @Value, @Version, @ChangedBy, @Reason, @ChangedAt)
ON CONFLICT (key) DO UPDATE
SET value = EXCLUDED.value, version = EXCLUDED.version, changed_by = EXCLUDED.changed_by, reason = EXCLUDED.reason, changed_at = EXCLUDED.changed_at;
