-- ExportAzureWiki Database Schema - PostgreSQL
-- Version: 001 - Baseline (No Legacy)
-- Date: 2026-02-28

CREATE TABLE IF NOT EXISTS users (
    id SERIAL PRIMARY KEY,
    username VARCHAR(255) NOT NULL UNIQUE,
    email VARCHAR(255),
    password_hash VARCHAR(512) NOT NULL,
    password_salt VARCHAR(512) NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_login_at TIMESTAMP,
    last_modified_at TIMESTAMP,
    authentication_method INTEGER,
    external_id VARCHAR(255),
    display_name VARCHAR(255),
    preferred_language VARCHAR(16)
);
CREATE INDEX IF NOT EXISTS ix_users_username ON users(username);
CREATE INDEX IF NOT EXISTS ix_users_email ON users(email);
CREATE INDEX IF NOT EXISTS ix_users_external_id ON users(external_id);

CREATE TABLE IF NOT EXISTS oauth_providers (
    id SERIAL PRIMARY KEY,
    provider_name VARCHAR(100) NOT NULL,
    display_name VARCHAR(150) NOT NULL,
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    client_id VARCHAR(512) NOT NULL,
    client_secret VARCHAR(1024),
    tenant_id VARCHAR(512),
    redirect_uri VARCHAR(500),
    scopes TEXT,
    configuration_json TEXT,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_modified_at TIMESTAMP
);
CREATE INDEX IF NOT EXISTS ix_oauth_providers_provider_name ON oauth_providers(provider_name);
CREATE INDEX IF NOT EXISTS ix_oauth_providers_is_enabled ON oauth_providers(is_enabled);

CREATE TABLE IF NOT EXISTS ai_providers (
    id SERIAL PRIMARY KEY,
    provider_name VARCHAR(100) NOT NULL,
    display_name VARCHAR(150) NOT NULL,
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    is_default BOOLEAN NOT NULL DEFAULT FALSE,
    endpoint_url VARCHAR(500),
    api_key TEXT,
    model_name VARCHAR(200),
    api_version VARCHAR(100),
    organization_id VARCHAR(200),
    configuration_json TEXT,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_modified_at TIMESTAMP
);
CREATE INDEX IF NOT EXISTS ix_ai_providers_provider_name ON ai_providers(provider_name);
CREATE INDEX IF NOT EXISTS ix_ai_providers_is_enabled ON ai_providers(is_enabled);
CREATE INDEX IF NOT EXISTS ix_ai_providers_is_default ON ai_providers(is_default);

CREATE TABLE IF NOT EXISTS authentication_configuration (
    id SERIAL PRIMARY KEY,
    primary_method INTEGER NOT NULL DEFAULT 0,
    allow_windows_auth BOOLEAN NOT NULL DEFAULT FALSE,
    allow_azure_ad BOOLEAN NOT NULL DEFAULT FALSE,
    allow_local_auth BOOLEAN NOT NULL DEFAULT TRUE,
    require_authentication BOOLEAN NOT NULL DEFAULT TRUE,
    sync_azure_ad_groups BOOLEAN NOT NULL DEFAULT FALSE,
    sync_windows_groups BOOLEAN NOT NULL DEFAULT FALSE,
    azure_ad_tenant_id VARCHAR(512),
    auto_create_users BOOLEAN NOT NULL DEFAULT TRUE,
    default_role VARCHAR(100) NOT NULL DEFAULT 'User',
    use_local_permissions BOOLEAN NOT NULL DEFAULT TRUE,
    use_azure_ad_permissions BOOLEAN NOT NULL DEFAULT FALSE,
    use_windows_permissions BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP
);
INSERT INTO authentication_configuration (primary_method, allow_local_auth, use_local_permissions)
SELECT 0, TRUE, TRUE
WHERE NOT EXISTS (SELECT 1 FROM authentication_configuration);

CREATE TABLE IF NOT EXISTS wiki_configurations (
    id SERIAL PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    organization VARCHAR(255) NOT NULL,
    project VARCHAR(255) NOT NULL,
    wiki_identifier VARCHAR(255) NOT NULL,
    personal_access_token TEXT NOT NULL,
    platform INTEGER NOT NULL DEFAULT 0,
    auth_type INTEGER NOT NULL DEFAULT 0,
    authentication_data_json TEXT,
    platform_specific_data_json TEXT,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    icon_color VARCHAR(32),
    is_default BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_used_at TIMESTAMP,
    last_modified_at TIMESTAMP,
    owner_user_id VARCHAR(128),
    owner_display_name VARCHAR(255),
    visibility_scope VARCHAR(32) NOT NULL DEFAULT 'Global',
    created_by_admin BOOLEAN NOT NULL DEFAULT FALSE,
    root_path TEXT
    -- No uniqueness on (organization, project, wiki_identifier): a project may
    -- have several configurations (e.g. one Repo-mode and one Wiki-mode, or
    -- different docs scopes). Identity is the integer primary key.
);
CREATE INDEX IF NOT EXISTS ix_wiki_configurations_is_default ON wiki_configurations(is_default);
CREATE INDEX IF NOT EXISTS ix_wiki_configurations_owner ON wiki_configurations(owner_user_id);

CREATE TABLE IF NOT EXISTS identity_groups (
    id SERIAL PRIMARY KEY,
    name VARCHAR(255) NOT NULL UNIQUE,
    description VARCHAR(500),
    is_system BOOLEAN NOT NULL DEFAULT FALSE,
    source VARCHAR(50),
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS user_identity_groups (
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    group_id INTEGER NOT NULL REFERENCES identity_groups(id) ON DELETE CASCADE,
    PRIMARY KEY (user_id, group_id)
);

CREATE TABLE IF NOT EXISTS application_settings (
    key VARCHAR(100) PRIMARY KEY,
    value TEXT,
    is_encrypted BOOLEAN NOT NULL DEFAULT FALSE,
    last_modified_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS access_policies (
    id VARCHAR(64) PRIMARY KEY,
    identity_type INTEGER NOT NULL,
    identity_id VARCHAR(128) NOT NULL,
    identity_display_name VARCHAR(255) NOT NULL,
    is_admin BOOLEAN NOT NULL DEFAULT FALSE,
    system_manage_wikis BOOLEAN NOT NULL DEFAULT FALSE,
    system_manage_users_and_groups BOOLEAN NOT NULL DEFAULT FALSE,
    system_manage_permissions BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL,
    last_modified_at TIMESTAMP NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE INDEX IF NOT EXISTS ix_access_policies_identity ON access_policies(identity_type, identity_id);
CREATE INDEX IF NOT EXISTS ix_access_policies_is_active ON access_policies(is_active);

CREATE TABLE IF NOT EXISTS access_policy_wikis (
    id SERIAL PRIMARY KEY,
    policy_id VARCHAR(64) NOT NULL REFERENCES access_policies(id) ON DELETE CASCADE,
    wiki_id VARCHAR(128) NOT NULL,
    start_points TEXT,
    can_view BOOLEAN NOT NULL DEFAULT FALSE,
    can_comment BOOLEAN NOT NULL DEFAULT FALSE,
    can_export_word BOOLEAN NOT NULL DEFAULT FALSE,
    can_export_pdf BOOLEAN NOT NULL DEFAULT FALSE,
    can_use_letterhead BOOLEAN NOT NULL DEFAULT FALSE
);
CREATE INDEX IF NOT EXISTS ix_access_policy_wikis_policy_id ON access_policy_wikis(policy_id);
CREATE INDEX IF NOT EXISTS ix_access_policy_wikis_wiki_id ON access_policy_wikis(wiki_id);

CREATE TABLE IF NOT EXISTS sessions (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    session_token VARCHAR(512) NOT NULL UNIQUE,
    refresh_token VARCHAR(512),
    expires_at TIMESTAMP NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ip_address VARCHAR(45),
    user_agent VARCHAR(500)
);
CREATE INDEX IF NOT EXISTS ix_sessions_session_token ON sessions(session_token);
CREATE INDEX IF NOT EXISTS ix_sessions_expires_at ON sessions(expires_at);

CREATE TABLE IF NOT EXISTS audit_log (
    id SERIAL PRIMARY KEY,
    user_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
    action VARCHAR(100) NOT NULL,
    entity_type VARCHAR(100),
    entity_id INTEGER,
    details TEXT,
    ip_address VARCHAR(45),
    timestamp TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS ix_audit_log_timestamp ON audit_log(timestamp);
CREATE INDEX IF NOT EXISTS ix_audit_log_user_id ON audit_log(user_id);

CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER PRIMARY KEY,
    applied_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    description VARCHAR(500)
);
INSERT INTO schema_version (version, description)
SELECT 2, 'Baseline no-legacy schema (preferred language)'
WHERE NOT EXISTS (SELECT 1 FROM schema_version WHERE version = 2);
