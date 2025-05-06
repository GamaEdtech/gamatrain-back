#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
namespace GamaEdtech.Common.DataAccess.UnitOfWork
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading;
    using System.Threading.Tasks;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAccess.Exceptions;
    using GamaEdtech.Common.DataAccess.Repositories;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    public abstract class UnitOfWorkBase<TContext>(TContext context, IServiceProvider serviceProvider, ILogger<IDataAccess> logger) : IUnitOfWorkBase
        where TContext : DbContext
    {
        ~UnitOfWorkBase()
        {
            Dispose(false);
        }

        protected bool IsDisposed { get; set; }

        protected IServiceProvider ServiceProvider { get; } = serviceProvider;

        protected ILogger<IDataAccess> Logger { get; } = logger;

        protected TContext Context { get; set; } = context;

        public int SaveChanges()
        {
            CheckDisposed();
            return Context.SaveChanges();
        }

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            CheckDisposed();
            return await Context.SaveChangesAsync(cancellationToken);
        }

        public IRepository<TEntity, long> GetRepository<TEntity>()
            where TEntity : class, IEntity<TEntity, long>
            => GetRepository<TEntity, long>();

        public IRepository<TEntity, TKey> GetRepository<TEntity, TKey>()
            where TEntity : class, IEntity<TEntity, TKey>
            where TKey : IEquatable<TKey>
        {
            CheckDisposed();
            var repository = ServiceProvider.GetService<IRepository<TEntity, TKey>>();
            if (repository is null)
            {
                var type = typeof(IRepository<TEntity, TKey>).Name;
                throw new RepositoryNotFoundException(type, $"Repository {type} not found in the IOC container. Check if it is registered during startup.");
            }

            _ = ((repository as IRepositoryInjection<TContext>)?.SetContext(Context));
            return repository;
        }

        public TRepository GetCustomRepository<TRepository>()
        {
            CheckDisposed();
            var repository = ServiceProvider.GetService<TRepository>();
            if (repository is null)
            {
                var type = typeof(TRepository).Name;
                throw new RepositoryNotFoundException(type, string.Format("Repository {0} not found in the IOC container. Check if it is registered during startup.", type));
            }

            _ = ((repository as IRepositoryInjection<TContext>)?.SetContext(Context));
            return repository;
        }

        public async Task<int> ExecuteSqlCommandAsync([NotNull] string sql, IEnumerable<(string ParameterName, object? Value)>? param = null)
        {
            try
            {
                var connection = Context.Database.GetDbConnection();
                using var command = connection.CreateCommand();
                command!.CommandText = sql.Replace(Constants.SchemaIdentifier, Context.Model.GetDefaultSchema(), StringComparison.OrdinalIgnoreCase);
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }

                command.CommandType = CommandType.Text;
                command.CommandTimeout = 300;
                if (param is not null)
                {
                    foreach (var (parameterName, value) in param)
                    {
                        var parameter = command.CreateParameter();
                        parameter.ParameterName = parameterName;
                        parameter.Value = value;
                        _ = command.Parameters.Add(parameter);
                    }
                }

                return await command.ExecuteNonQueryAsync();
            }
            catch (Exception exc)
            {
                Logger.LogException(exc);
                throw;
            }
        }

        public async Task<DataSet> SqlQueryAsync([NotNull] string sql, IList<(string ParameterName, object? Value)>? param = null)
        {
            try
            {
                var connection = Context.Database.GetDbConnection();
                using var command = connection.CreateCommand();
                command.CommandText = sql.Replace(Constants.SchemaIdentifier, Context.Model.GetDefaultSchema(), StringComparison.OrdinalIgnoreCase);
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }

                command.CommandType = CommandType.Text;
                command.CommandTimeout = 300;

                if (param is not null)
                {
                    for (var i = 0; i < param.Count; i++)
                    {
                        var parameter = command.CreateParameter();
                        parameter.ParameterName = param[i].ParameterName;
                        parameter.Value = param[i].Value;
                        _ = command.Parameters.Add(parameter);
                    }
                }

                DataSet dataSet = new();
                using var reader = await command.ExecuteReaderAsync();
                do
                {
                    DataTable dt = new();
                    dt.Load(reader);
                    dataSet.Tables.Add(dt);
                }
                while (!reader.IsClosed && await reader.NextResultAsync());

                return dataSet;
            }
            catch (Exception exc)
            {
                Logger.LogException(exc);
                throw;
            }
        }

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected void CheckDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);

        protected virtual void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing && Context is not null)
            {
                Context.Dispose();
            }

            IsDisposed = true;
        }

        #endregion
    }
}

#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities
