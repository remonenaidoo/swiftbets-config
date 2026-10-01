SELECT key AS Key, value AS Value, version AS Version, changed_by AS ChangedBy, reason AS Reason, changed_at AS ChangedAt
FROM config.settings
ORDER BY key;
