-- ExportAzureWiki Database Schema - SQLite
-- Version: 001 - Baseline (No Legacy)
-- Date: 2026-02-28

PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS users (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    username TEXT NOT NULL UNIQUE,
    email TEXT,
    password_hash TEXT NOT NULL,
    password_salt TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    last_login_at TEXT,
    last_modified_at TEXT,
    authentication_method INTEGER,
    external_id TEXT,
    display_name TEXT,
    preferred_language TEXT
);
CREATE INDEX IF NOT EXISTS ix_users_username ON users(username);
CREATE INDEX IF NOT EXISTS ix_users_email ON users(email);
CREATE INDEX IF NOT EXISTS ix_users_external_id ON users(external_id);

CREATE TABLE IF NOT EXISTS oauth_providers (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_name TEXT NOT NULL,
    display_name TEXT NOT NULL,
    is_enabled INTEGER NOT NULL DEFAULT 1,
    client_id TEXT NOT NULL,
    client_secret TEXT,
    tenant_id TEXT,
    redirect_uri TEXT,
    scopes TEXT,
    configuration_json TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    last_modified_at TEXT
);
CREATE INDEX IF NOT EXISTS ix_oauth_providers_provider_name ON oauth_providers(provider_name);
CREATE INDEX IF NOT EXISTS ix_oauth_providers_is_enabled ON oauth_providers(is_enabled);

CREATE TABLE IF NOT EXISTS ai_providers (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_name TEXT NOT NULL,
    display_name TEXT NOT NULL,
    is_enabled INTEGER NOT NULL DEFAULT 1,
    is_default INTEGER NOT NULL DEFAULT 0,
    endpoint_url TEXT,
    api_key TEXT,
    model_name TEXT,
    api_version TEXT,
    organization_id TEXT,
    configuration_json TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    last_modified_at TEXT
);
CREATE INDEX IF NOT EXISTS ix_ai_providers_provider_name ON ai_providers(provider_name);
CREATE INDEX IF NOT EXISTS ix_ai_providers_is_enabled ON ai_providers(is_enabled);
CREATE INDEX IF NOT EXISTS ix_ai_providers_is_default ON ai_providers(is_default);

CREATE TABLE IF NOT EXISTS authentication_configuration (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    primary_method INTEGER NOT NULL DEFAULT 0,
    allow_windows_auth INTEGER NOT NULL DEFAULT 0,
    allow_azure_ad INTEGER NOT NULL DEFAULT 0,
    allow_local_auth INTEGER NOT NULL DEFAULT 1,
    require_authentication INTEGER NOT NULL DEFAULT 1,
    sync_azure_ad_groups INTEGER NOT NULL DEFAULT 0,
    sync_windows_groups INTEGER NOT NULL DEFAULT 0,
    azure_ad_tenant_id TEXT,
    auto_create_users INTEGER NOT NULL DEFAULT 1,
    default_role TEXT NOT NULL DEFAULT 'User',
    use_local_permissions INTEGER NOT NULL DEFAULT 1,
    use_azure_ad_permissions INTEGER NOT NULL DEFAULT 0,
    use_windows_permissions INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT
);
INSERT INTO authentication_configuration (primary_method, allow_local_auth, use_local_permissions)
SELECT 0, 1, 1
WHERE NOT EXISTS (SELECT 1 FROM authentication_configuration);

CREATE TABLE IF NOT EXISTS wiki_configurations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    organization TEXT NOT NULL,
    project TEXT NOT NULL,
    wiki_identifier TEXT NOT NULL,
    personal_access_token TEXT NOT NULL,
    platform INTEGER NOT NULL DEFAULT 0,
    auth_type INTEGER NOT NULL DEFAULT 0,
    authentication_data_json TEXT,
    platform_specific_data_json TEXT,
    is_active INTEGER NOT NULL DEFAULT 1,
    icon_color TEXT,
    is_default INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    last_used_at TEXT,
    last_modified_at TEXT,
    owner_user_id TEXT,
    owner_display_name TEXT,
    visibility_scope TEXT NOT NULL DEFAULT 'Global',
    created_by_admin INTEGER NOT NULL DEFAULT 0,
    root_path TEXT
    -- No uniqueness on (organization, project, wiki_identifier): a project may
    -- have several configurations (e.g. one Repo-mode and one Wiki-mode, or
    -- different docs scopes). Identity is the integer primary key.
);
CREATE INDEX IF NOT EXISTS ix_wiki_configurations_is_default ON wiki_configurations(is_default);
CREATE INDEX IF NOT EXISTS ix_wiki_configurations_owner ON wiki_configurations(owner_user_id);

CREATE TABLE IF NOT EXISTS identity_groups (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    description TEXT,
    is_system INTEGER NOT NULL DEFAULT 0,
    source TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS user_identity_groups (
    user_id INTEGER NOT NULL,
    group_id INTEGER NOT NULL,
    PRIMARY KEY (user_id, group_id),
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
    FOREIGN KEY (group_id) REFERENCES identity_groups(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS application_settings (
    key TEXT PRIMARY KEY,
    value TEXT,
    is_encrypted INTEGER NOT NULL DEFAULT 0,
    last_modified_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS access_policies (
    id TEXT PRIMARY KEY,
    identity_type INTEGER NOT NULL,
    identity_id TEXT NOT NULL,
    identity_display_name TEXT NOT NULL,
    is_admin INTEGER NOT NULL DEFAULT 0,
    system_manage_wikis INTEGER NOT NULL DEFAULT 0,
    system_manage_users_and_groups INTEGER NOT NULL DEFAULT 0,
    system_manage_permissions INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    last_modified_at TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_access_policies_identity ON access_policies(identity_type, identity_id);
CREATE INDEX IF NOT EXISTS ix_access_policies_is_active ON access_policies(is_active);

CREATE TABLE IF NOT EXISTS access_policy_wikis (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    policy_id TEXT NOT NULL,
    wiki_id TEXT NOT NULL,
    start_points TEXT,
    can_view INTEGER NOT NULL DEFAULT 0,
    can_comment INTEGER NOT NULL DEFAULT 0,
    can_export_word INTEGER NOT NULL DEFAULT 0,
    can_export_pdf INTEGER NOT NULL DEFAULT 0,
    can_use_letterhead INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(policy_id) REFERENCES access_policies(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_access_policy_wikis_policy_id ON access_policy_wikis(policy_id);
CREATE INDEX IF NOT EXISTS ix_access_policy_wikis_wiki_id ON access_policy_wikis(wiki_id);

CREATE TABLE IF NOT EXISTS sessions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    session_token TEXT NOT NULL UNIQUE,
    refresh_token TEXT,
    expires_at TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    ip_address TEXT,
    user_agent TEXT,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_sessions_session_token ON sessions(session_token);
CREATE INDEX IF NOT EXISTS ix_sessions_expires_at ON sessions(expires_at);

CREATE TABLE IF NOT EXISTS audit_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER,
    action TEXT NOT NULL,
    entity_type TEXT,
    entity_id INTEGER,
    details TEXT,
    ip_address TEXT,
    timestamp TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS ix_audit_log_timestamp ON audit_log(timestamp);
CREATE INDEX IF NOT EXISTS ix_audit_log_user_id ON audit_log(user_id);

CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER PRIMARY KEY,
    applied_at TEXT NOT NULL DEFAULT (datetime('now')),
    description TEXT
);
INSERT OR IGNORE INTO schema_version (version, description)
VALUES (2, 'Baseline no-legacy schema (preferred language)');
