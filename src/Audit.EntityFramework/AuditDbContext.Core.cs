﻿#if NETCOREAPP1_0
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Audit.EntityFramework
{
    /// <summary>
    /// The base DbContext class for Audit.
    /// NET CORE
    /// </summary>
    public abstract partial class AuditDbContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AuditDbContext" /> class.
        /// </summary>
        /// <param name="options">The options.</param>
        protected AuditDbContext(DbContextOptions options) : base(options)
        {
            SetConfig();
        }

        /// <summary>
        /// Gets the entities changes for this entry.
        /// </summary>
        /// <param name="entry">The entry.</param>
        private List<EventEntryChange> GetChanges(EntityEntry entry)
        {
            var result = new List<EventEntryChange>();
            var props = Model.FindEntityType(entry.Entity.GetType()).GetProperties();
            foreach (var prop in props)
            {
                PropertyEntry propEntry = entry.Property(prop.Name);
                if (propEntry.IsModified)
                {
                    result.Add(new EventEntryChange()
                    {
                        ColumnName = GetColumnName(prop),
                        NewValue = propEntry.CurrentValue,
                        OriginalValue = propEntry.OriginalValue
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// Gets the column values for an insert/delete operation.
        /// </summary>
        /// <param name="entry">The entity entry.</param>
        private Dictionary<string, object> GetColumnValues(EntityEntry entry)
        {
            var result = new Dictionary<string, object>();
            var props = Model.FindEntityType(entry.Entity.GetType()).GetProperties();
            foreach (var prop in props)
            {
                PropertyEntry propEntry = entry.Property(prop.Name);
                var value = (entry.State != EntityState.Deleted) ? propEntry.CurrentValue : propEntry.OriginalValue;
                result.Add(prop.Name, value);
            }
            return result;
        }

        /// <summary>
        /// Gets the name of the column.
        /// </summary>
        private static string GetColumnName(IProperty prop)
        {
            return prop.SqlServer().ColumnName ?? prop.Name;
        }
        /// <summary>
        /// Gets the name of the entity.
        /// </summary>
        private static string GetEntityName(IEntityType entityType)
        {
            var tableName = entityType.Name.Split('.');
            return tableName[tableName.Length - 1];
        }
        /// <summary>
        /// Gets the primary key values for an entity
        /// </summary>
        private static Dictionary<string, object> GetPrimaryKey(IEntityType entityType, object entity)
        {
            var result = new Dictionary<string, object>();
            var pkDefinition = entityType.FindPrimaryKey();
            if (pkDefinition != null)
            {
                foreach (var pkProp in pkDefinition.Properties)
                {
                    var value = entity.GetType().GetProperty(pkProp.Name).GetValue(entity);
                    result.Add(pkProp.Name, value);
                }
            }
            return result;
        }

        /// <summary>
        /// Creates the Audit Event.
        /// </summary>
        /// <param name="includeEntities">To indicate if it must incluide the serialized entities.</param>
        /// <param name="mode">The option mode to include/exclude entities from Audit.</param>
        private EntityFrameworkEvent CreateAuditEvent(bool includeEntities, AuditOptionMode mode)
        {
            var modifiedEntries = GetModifiedEntries(mode);
            if (modifiedEntries.Count == 0)
            {
                return null;
            }
            var clientConnectionId = GetClientConnectionId(Database.GetDbConnection());
            var efEvent = new EntityFrameworkEvent()
            {
                Entries = new List<EventEntry>(),
                Database = Database.GetDbConnection()?.Database,
                ConnectionId = clientConnectionId,
                TransactionId = GetCurrentTransactionId(clientConnectionId)
            };
            foreach (var entry in modifiedEntries)
            {
                var entity = entry.Entity;
                var validationResults = GetValidationResults(entity);
                var entityType = Model.FindEntityType(entry.Entity.GetType());
                efEvent.Entries.Add(new EventEntry()
                {
                    Valid = validationResults == null,
                    ValidationResults = validationResults?.Select(x => x.ErrorMessage).ToList(),
                    Entity = includeEntities ? entity : null,
                    Action = GetStateName(entry.State),
                    Changes = entry.State == EntityState.Modified ? GetChanges(entry) : null,
                    ColumnValues = GetColumnValues(entry),
                    PrimaryKey = GetPrimaryKey(entityType, entity),
                    Table = GetEntityName(entityType)
                });
            }
            return efEvent;
        }

        /// <summary>
        /// Tries to get the current transaction identifier.
        /// </summary>
        /// <param name="clientConnectionId">The client ConnectionId.</param>
        private string GetCurrentTransactionId(string clientConnectionId)
        {
            var dbtxmgr = this.GetInfrastructure().GetService<IDbContextTransactionManager>();
            var relcon = dbtxmgr as IRelationalConnection;
            var dbtx = relcon.CurrentTransaction;
            var tx = dbtx?.GetDbTransaction();
            if (tx == null)
            {
                return null;
            }
            return GetTransactionId(tx, clientConnectionId);
        }
    }
}
#endif