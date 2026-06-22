using System.Data;
using ExportAzureWiki.Data.Repositories;

namespace ExportAzureWiki.Data;

/// <summary>
/// Unit of Work implementation
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly IDbConnectionFactory _connectionFactory;
    private IDbConnection? _connection;
    private IDbTransaction? _transaction;
    private bool _disposed;

    // Lazy-loaded repositories
    private IUserRepository? _users;
    private IWikiConfigurationRepository? _wikiConfigurations;
    private IOAuthProviderRepository? _oauthProviders;
    private IAiProviderRepository? _aiProviders;
    private IGroupRepository? _groups;
    private AuthenticationConfigurationRepository? _authenticationConfiguration;

    public UnitOfWork(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public IUserRepository Users
    {
        get
        {
            if (_users == null)
            {
                EnsureConnection();
                _users = new UserRepository(_connection!, _connectionFactory.GetDatabaseType());
            }
            return _users;
        }
    }

    public IWikiConfigurationRepository WikiConfigurations
    {
        get
        {
            if (_wikiConfigurations == null)
            {
                EnsureConnection();
                _wikiConfigurations = new WikiConfigurationRepository(_connection!, _connectionFactory.GetDatabaseType());
            }
            return _wikiConfigurations;
        }
    }

    public IOAuthProviderRepository OAuthProviders
    {
        get
        {
            if (_oauthProviders == null)
            {
                EnsureConnection();
                _oauthProviders = new OAuthProviderRepository(_connection!, _connectionFactory.GetDatabaseType());
            }
            return _oauthProviders;
        }
    }

    public IAiProviderRepository AiProviders
    {
        get
        {
            if (_aiProviders == null)
            {
                EnsureConnection();
                _aiProviders = new AiProviderRepository(_connection!, _connectionFactory.GetDatabaseType());
            }
            return _aiProviders;
        }
    }

    public IGroupRepository Groups
    {
        get
        {
            if (_groups == null)
            {
                EnsureConnection();
                _groups = new GroupRepository(_connection!, _connectionFactory.GetDatabaseType());
            }
            return _groups;
        }
    }

    public AuthenticationConfigurationRepository AuthenticationConfiguration
    {
        get
        {
            if (_authenticationConfiguration == null)
            {
                EnsureConnection();
                _authenticationConfiguration = new AuthenticationConfigurationRepository(_connection!, _connectionFactory.GetDatabaseType());
            }
            return _authenticationConfiguration;
        }
    }

    public async Task<bool> BeginTransactionAsync()
    {
        try
        {
            EnsureConnection();

            if (_transaction != null)
            {
                throw new InvalidOperationException("Transaction already in progress");
            }

            _transaction = _connection!.BeginTransaction();
            return await Task.FromResult(true).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> CommitAsync()
    {
        try
        {
            if (_transaction == null)
            {
                throw new InvalidOperationException("No transaction in progress");
            }

            _transaction.Commit();
            _transaction.Dispose();
            _transaction = null;

            return await Task.FromResult(true).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RollbackAsync()
    {
        try
        {
            if (_transaction == null)
            {
                throw new InvalidOperationException("No transaction in progress");
            }

            _transaction.Rollback();
            _transaction.Dispose();
            _transaction = null;

            return await Task.FromResult(true).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    private void EnsureConnection()
    {
        if (_connection == null || _connection.State != ConnectionState.Open)
        {
            _connection = _connectionFactory.CreateConnectionAsync().GetAwaiter().GetResult();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _transaction?.Dispose();
                _connection?.Dispose();
            }

            _disposed = true;
        }
    }
}
