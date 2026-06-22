# Sistema de Autenticação e Autorização - ExportAzureWiki

## Visão Geral

Este sistema fornece autenticação e autorização completas para a aplicação ExportAzureWiki, com suporte para múltiplos provedores de identidade e gerenciamento granular de permissões.

## Funcionalidades

### Autenticação
- **Múltiplos Provedores**: Azure AD, Microsoft Account, GitHub, Google
- **OAuth 2.0 / OIDC**: Implementação completa com PKCE
- **Gestão de Sessões**: Sessões persistentes com tokens de refresh
- **Validação de Tokens**: Validação automática e renovação de tokens

### Autorização
- **Permissões por Wiki**: Controle granular de acesso a wikis específicas
- **Grupos de Wikis**: Organize wikis em grupos para facilitar a gestão
- **Tipos de Identidade Suportados**:
  - Usuário individual
  - Grupo do Azure AD
  - Grupo do Windows
  - Organização GitHub
  - Time do GitHub
  - Grupos customizados

- **Níveis de Permissão**:
  - **None**: Sem acesso
  - **Read**: Visualizar wikis
  - **Write**: Ler e exportar wikis
  - **Admin**: Gerenciar wikis e permissões
  - **Owner**: Acesso total

## Configuração

### 1. Azure Active Directory

Para habilitar autenticação com Azure AD:

1. Acesse o [Portal do Azure](https://portal.azure.com)
2. Vá para **Azure Active Directory** > **App registrations** > **New registration**
3. Configure:
   - Nome: `ExportAzureWiki`
   - Tipos de conta suportados: Escolha conforme sua necessidade
   - Redirect URI: `http://localhost:8080/callback` (Web)
4. Após criar, copie o **Application (client) ID** e **Directory (tenant) ID**
5. Em **Certificates & secrets**, crie um novo client secret (opcional para PKCE)

No código (`Program.cs`):

```csharp
authService.RegisterProvider(new AzureADProvider(new Dictionary<string, string>
{
    ["ClientId"] = "seu-client-id-aqui",
    ["TenantId"] = "seu-tenant-id-aqui",
    ["RedirectUri"] = "http://localhost:8080/callback"
}));
```

### 2. Microsoft Account

1. Acesse o [Portal de Registro de Aplicações Microsoft](https://portal.azure.com/#blade/Microsoft_AAD_RegisteredApps)
2. Siga os mesmos passos do Azure AD, mas use `common` como tenant

```csharp
authService.RegisterProvider(new MicrosoftAccountProvider(new Dictionary<string, string>
{
    ["ClientId"] = "seu-client-id-aqui",
    ["RedirectUri"] = "http://localhost:8080/callback"
}));
```

### 3. GitHub OAuth

1. Acesse [GitHub Developer Settings](https://github.com/settings/developers)
2. Clique em **New OAuth App**
3. Configure:
   - Application name: `ExportAzureWiki`
   - Homepage URL: `http://localhost`
   - Authorization callback URL: `http://localhost:8080/callback`
4. Copie o **Client ID** e **Client Secret**

```csharp
authService.RegisterProvider(new GitHubProvider(new Dictionary<string, string>
{
    ["ClientId"] = "seu-client-id-aqui",
    ["ClientSecret"] = "seu-client-secret-aqui",
    ["RedirectUri"] = "http://localhost:8080/callback"
}));
```

### 4. Google OAuth

1. Acesse o [Google Cloud Console](https://console.cloud.google.com/)
2. Crie um novo projeto ou selecione um existente
3. Vá para **APIs & Services** > **Credentials**
4. Clique em **Create Credentials** > **OAuth 2.0 Client ID**
5. Configure:
   - Application type: Desktop app
   - Name: `ExportAzureWiki`
6. Adicione `http://localhost:8080/callback` às Authorized redirect URIs
7. Copie o **Client ID** e **Client Secret**

```csharp
authService.RegisterProvider(new GoogleProvider(new Dictionary<string, string>
{
    ["ClientId"] = "seu-client-id-aqui",
    ["ClientSecret"] = "seu-client-secret-aqui",
    ["RedirectUri"] = "http://localhost:8080/callback"
}));
```

## Uso

### Habilitar/Desabilitar Autenticação

Para desabilitar a autenticação (modo desenvolvimento):

```csharp
var authConfig = authService.GetConfig();
authConfig.RequireAuthentication = false;
authService.SaveConfig(authConfig);
```

### Gerenciar Permissões Programaticamente

#### Conceder Permissão a um Usuário

```csharp
var authorizationService = _serviceProvider.GetRequiredService<IAuthorizationService>();

await authorizationService.GrantPermissionAsync(
    wikiId: "wiki-id",
    identityId: "user-id",
    identityType: IdentityType.User,
    level: PermissionLevel.Write
);
```

#### Conceder Permissão a um Grupo do Azure AD

```csharp
await authorizationService.GrantPermissionAsync(
    wikiId: "wiki-id",
    identityId: "azure-ad-group-id",
    identityType: IdentityType.AzureADGroup,
    level: PermissionLevel.Read
);
```

#### Verificar Permissão

```csharp
var hasPermission = await authorizationService.HasPermissionAsync(
    userId: "user-id",
    wikiId: "wiki-id",
    requiredLevel: PermissionLevel.Write
);

if (hasPermission)
{
    // Usuário pode exportar a wiki
}
```

#### Obter Wikis Acessíveis

```csharp
var accessibleWikis = await authorizationService.GetAccessibleWikisAsync("user-id");
```

### Criar Grupos de Wikis

```csharp
var authorizationService = _serviceProvider.GetRequiredService<AuthorizationService>();

var wikiGroup = authorizationService.CreateWikiGroup(
    name: "Wikis de Desenvolvimento",
    description: "Todas as wikis relacionadas ao desenvolvimento",
    wikiIds: new List<string> { "wiki1-id", "wiki2-id", "wiki3-id" }
);

// Conceder permissão ao grupo inteiro
await authorizationService.GrantPermissionAsync(
    wikiId: wikiGroup.Id,
    identityId: "team-id",
    identityType: IdentityType.Custom,
    level: PermissionLevel.Write
);
```

### Criar Grupos de Identidades

```csharp
var group = authorizationService.CreateIdentityGroup(
    name: "Equipe de DevOps",
    type: IdentityType.Custom,
    externalId: null
);

// Adicionar membros ao grupo
await authorizationService.AddUserToGroupAsync("user1-id", group.Id);
await authorizationService.AddUserToGroupAsync("user2-id", group.Id);
```

## Interface de Gerenciamento

A aplicação inclui uma interface gráfica completa para gerenciar permissões:

```csharp
var permissionsForm = _serviceProvider.GetRequiredService<PermissionsManagementForm>();
permissionsForm.ShowDialog();
```

A tela possui três abas:
1. **Permissões de Wikis**: Visualize e gerencie permissões por wiki
2. **Grupos de Usuários**: Gerencie grupos de identidades
3. **Grupos de Wikis**: Organize wikis em grupos

## Arquitetura

### Estrutura de Pastas

```
ExportAzureWiki/
├── Models/
│   └── Authentication/
│       ├── AuthenticationProvider.cs
│       ├── User.cs
│       ├── Identity.cs
│       └── Permission.cs
├── Services/
│   ├── Authentication/
│   │   ├── AuthenticationService.cs
│   │   └── Providers/
│   │       ├── BaseAuthenticationProvider.cs
│   │       ├── AzureADProvider.cs
│   │       ├── MicrosoftAccountProvider.cs
│   │       ├── GitHubProvider.cs
│   │       └── GoogleProvider.cs
│   └── Authorization/
│       └── AuthorizationService.cs
├── Interfaces/
│   ├── IAuthenticationProvider.cs
│   └── IAuthorizationService.cs
└── Forms/
    ├── LoginForm.cs
    └── PermissionsManagementForm.cs
```

### Fluxo de Autenticação

1. Aplicação inicia
2. `AuthenticationService` verifica se há sessão válida
3. Se não houver, mostra `LoginForm`
4. Usuário seleciona provedor
5. Provedor inicia fluxo OAuth
6. Navegador abre para autenticação
7. Callback recebe código de autorização
8. Provedor troca código por tokens
9. Sessão é criada e persistida
10. Aplicação continua normalmente

### Armazenamento de Dados

Os dados são armazenados em:
- **Windows**: `%APPDATA%\ExportAzureWiki\`
  - `auth/session.json` - Sessão atual (criptografada)
  - `auth/users.json` - Histórico de usuários
  - `auth/auth-config.json` - Configurações de autenticação
  - `permissions/permissions.json` - Permissões
  - `permissions/wiki-groups.json` - Grupos de wikis
  - `permissions/identity-groups.json` - Grupos de identidades
  - `permissions/roles.json` - Papéis/perfis

## Segurança

- **Tokens Criptografados**: Todos os tokens são criptografados usando `EncryptionHelper`
- **PKCE**: Implementado para todos os provedores OAuth 2.0
- **HTTPS**: Endpoints OAuth usam HTTPS
- **Sessões Temporárias**: Sessões expiram após período configurável
- **Validação de Estado**: Proteção contra CSRF em fluxos OAuth

## Exemplos de Integração

### Verificar Permissão Antes de Exportar

```csharp
public class ExportService
{
    private readonly IAuthorizationService _authService;
    private readonly AuthenticationService _authenticationService;

    public async Task<bool> ExportWikiAsync(string wikiId)
    {
        var currentUser = _authenticationService.CurrentUser;
        if (currentUser == null)
        {
            throw new UnauthorizedAccessException("Usuário não autenticado");
        }

        var hasPermission = await _authService.HasPermissionAsync(
            currentUser.Id,
            wikiId,
            PermissionLevel.Write
        );

        if (!hasPermission)
        {
            throw new UnauthorizedAccessException(
                "Você não tem permissão para exportar esta wiki"
            );
        }

        // Prosseguir com exportação...
    }
}
```

### Filtrar Wikis por Permissão

```csharp
public async Task<List<WikiConfiguration>> GetAccessibleWikisAsync()
{
    var currentUser = _authenticationService.CurrentUser;
    if (currentUser == null) return new List<WikiConfiguration>();

    var accessibleWikiIds = await _authService.GetAccessibleWikisAsync(currentUser.Id);
    var allWikis = _configService.GetAllConfigurations();

    return allWikis.Where(w => accessibleWikiIds.Contains(w.Id)).ToList();
}
```

## Troubleshooting

### Erro: "Provider not configured"
- Verifique se você configurou os ClientId/ClientSecret no `Program.cs`
- Certifique-se de que as credenciais estão corretas

### Erro: "Authentication cancelled or failed"
- Verifique se a URL de callback está correta (http://localhost:8080/callback)
- Confirme que a porta 8080 está disponível
- Verifique o firewall/antivírus

### Sessão expira muito rápido
- Ajuste `SessionTimeoutMinutes` nas configurações:

```csharp
var config = authService.GetConfig();
config.SessionTimeoutMinutes = 1440; // 24 horas
authService.SaveConfig(config);
```

## Próximos Passos

Para completar a implementação:

1. **Adicionar validações de permissão** nas ações críticas (exportar, deletar, etc.)
2. **Implementar UI para adicionar/editar permissões** na tela de gerenciamento
3. **Adicionar auditoria** para registrar ações dos usuários
4. **Implementar sincronização** de grupos do Azure AD automaticamente
5. **Adicionar suporte a SSO** para ambientes corporativos

## Suporte

Para questões ou problemas, abra uma issue no repositório do projeto.
