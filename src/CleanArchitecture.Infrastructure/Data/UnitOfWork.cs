﻿using CleanArchitecture.Core;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Infrastructure.Data
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _dbContext;

        public UnitOfWork(AppDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public void Add<TEntity>(IEnumerable<TEntity> entities)
            where TEntity : class, IEntity => _dbContext.AddRange(entities);

        public void Add<TEntity>(params TEntity[] entities)
            where TEntity : class, IEntity => _dbContext.AddRange(entities);

        public void Update<TEntity>(IEnumerable<TEntity> entities)
            where TEntity : class, IEntity => _dbContext.UpdateRange(entities);

        public void Update<TEntity>(params TEntity[] entities)
            where TEntity : class, IEntity => _dbContext.UpdateRange(entities);

        public void Remove<TEntity>(IEnumerable<TEntity> entities)
            where TEntity : class, IEntity => _dbContext.RemoveRange(entities);

        public void Remove<TEntity>(params TEntity[] entities)
            where TEntity : class, IEntity => _dbContext.RemoveRange(entities);

        public Task<TEntity?> FindAsync<TEntity>(long id) where TEntity : class, IEntity
        {
            return _dbContext.FindAsync<TEntity>(id).AsTask();
        }

        public Task CompleteAsync() => _dbContext.SaveChangesAsync();

        public IQueryable<TEntity> Query<TEntity>()
            where TEntity : class, IEntity => _dbContext.Set<TEntity>();
    }
}