-- Rolls back 0001_config. Every setting and its history is lost; consumers fall back to their defaults.
DROP TABLE IF EXISTS config.history;
DROP TABLE IF EXISTS config.settings;
DROP SCHEMA IF EXISTS config;
