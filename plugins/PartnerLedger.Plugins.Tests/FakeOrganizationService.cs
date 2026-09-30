using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins.Tests
{
    /// <summary>
    /// 独立監査2回目P1-1対応。GrantAccessRequest等の実共有APIやDataverse本体の
    /// SDKメッセージ処理を再現するのではなく、
    /// OperationLogRepository／PartnerAccessBootstrapPluginが
    /// IOrganizationServiceへ何を読み書きするかを検証するための最小限のテストダブル。
    /// FakeXrmEasy等の外部依存を追加せず、既存のテストプロジェクトが既に参照している
    /// Microsoft.Xrm.Sdk/Microsoft.Crm.Sdk.Proxyだけで完結させる。
    /// </summary>
    public sealed class FakeOrganizationService : IOrganizationService
    {
        private readonly Dictionary<(string LogicalName, Guid Id), Entity> _store = new();
        private readonly Dictionary<(string LogicalName, Guid Id), int> _rowVersions = new();

        public List<OrganizationRequest> ExecutedRequests { get; } = new();
        public Action<OrganizationRequest>? BeforeExecute { get; set; }
        public Func<Entity, Exception?>? BeforeCreate { get; set; }
        public Action<Entity>? BeforeUpdate { get; set; }
        public Func<string, Guid, bool>? CanRetrieve { get; set; }
        public Func<QueryExpression, Exception?>? BeforeRetrieveMultiple { get; set; }
        public EntityReference? DefaultOwner { get; set; }
        public AccessRights RetrievedPrincipalAccessRights { get; set; } = AccessRights.None;
        /// <summary>RetrieveUserPrivilegeByPrivilegeNameの答え（ユーザーID・権限名→持っているか）。既定は全員が持つ。</summary>
        public Func<Guid, string, bool> UserHasPrivilege { get; set; } = (_, _) => true;
        /// <summary>Retrieveの前に例外を投げさせる（本番のFaultException等を模す）。</summary>
        public Func<string, Guid, Exception?>? BeforeRetrieve { get; set; }
        public IReadOnlyCollection<PrincipalAccess> RetrievedSharedPrincipalAccesses { get; set; }
            = Array.Empty<PrincipalAccess>();

        public Entity Seed(Entity entity)
        {
            if (entity.Id == Guid.Empty)
            {
                entity.Id = Guid.NewGuid();
            }
            _store[(entity.LogicalName, entity.Id)] = entity;
            _rowVersions[(entity.LogicalName, entity.Id)] = 1;
            return entity;
        }

        public Guid Create(Entity entity)
        {
            if (!entity.Attributes.ContainsKey("ownerid") && DefaultOwner != null)
            {
                entity["ownerid"] = new EntityReference(DefaultOwner.LogicalName, DefaultOwner.Id);
            }
            var createFailure = BeforeCreate?.Invoke(entity);
            if (createFailure != null)
            {
                throw createFailure;
            }

            var id = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id;
            entity.Id = id;
            _store[(entity.LogicalName, id)] = entity;
            _rowVersions[(entity.LogicalName, id)] = 1;
            return id;
        }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            var retrieveFailure = BeforeRetrieve?.Invoke(entityName, id);
            if (retrieveFailure != null)
            {
                throw retrieveFailure;
            }

            if (CanRetrieve != null && !CanRetrieve(entityName, id))
            {
                throw new InvalidPluginExecutionException($"{entityName}({id}) へのRead権限がありません。");
            }

            if (!_store.TryGetValue((entityName, id), out var entity))
            {
                throw new InvalidPluginExecutionException($"{entityName}({id}) が見つかりません。");
            }
            entity.RowVersion = _rowVersions[(entityName, id)].ToString();
            return entity;
        }

        public void ForceRowVersion(string entityName, Guid id, int rowVersion)
            => _rowVersions[(entityName, id)] = rowVersion;

        public T RunInTransaction<T>(Func<T> operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));

            var storeSnapshot = _store.ToDictionary(
                pair => pair.Key,
                pair => CloneEntity(pair.Value));
            var rowVersionSnapshot = new Dictionary<(string LogicalName, Guid Id), int>(_rowVersions);
            var executedRequestCount = ExecutedRequests.Count;
            try
            {
                return operation();
            }
            catch
            {
                _store.Clear();
                foreach (var pair in storeSnapshot)
                {
                    _store[pair.Key] = pair.Value;
                }

                _rowVersions.Clear();
                foreach (var pair in rowVersionSnapshot)
                {
                    _rowVersions[pair.Key] = pair.Value;
                }

                if (ExecutedRequests.Count > executedRequestCount)
                {
                    ExecutedRequests.RemoveRange(executedRequestCount, ExecutedRequests.Count - executedRequestCount);
                }
                throw;
            }
        }

        public void Update(Entity entity)
        {
            BeforeUpdate?.Invoke(entity);
            var key = (entity.LogicalName, entity.Id);
            if (!_store.TryGetValue(key, out var existing))
            {
                throw new InvalidPluginExecutionException($"{entity.LogicalName}({entity.Id}) が見つかりません。");
            }

            if (entity.RowVersion != null && entity.RowVersion != _rowVersions[key].ToString())
            {
                throw new InvalidPluginExecutionException("行バージョンが一致しません（楽観的並行性制御）。");
            }

            foreach (var attribute in entity.Attributes)
            {
                existing[attribute.Key] = attribute.Value;
            }
            _rowVersions[key] = _rowVersions[key] + 1;
        }

        public void Delete(string entityName, Guid id) => throw new NotSupportedException();

        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw new NotSupportedException();

        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            => throw new NotSupportedException();

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            ExecutedRequests.Add(request);
            BeforeExecute?.Invoke(request);

            if (request is UpdateRequest updateRequest)
            {
                Update(updateRequest.Target);
                return new UpdateResponse();
            }

            if (request is CreateRequest createRequest)
            {
                var response = new CreateResponse();
                response.Results["id"] = Create(createRequest.Target);
                return response;
            }

            if (request is AssignRequest assignRequest)
            {
                var key = (assignRequest.Target.LogicalName, assignRequest.Target.Id);
                if (!_store.TryGetValue(key, out var assignedEntity))
                {
                    throw new InvalidPluginExecutionException($"{assignRequest.Target.LogicalName}({assignRequest.Target.Id}) が見つかりません。");
                }
                assignedEntity["ownerid"] = new EntityReference(assignRequest.Assignee.LogicalName, assignRequest.Assignee.Id);
                return new AssignResponse();
            }

            if (request is RetrievePrincipalAccessRequest)
            {
                var response = new RetrievePrincipalAccessResponse();
                response.Results["AccessRights"] = RetrievedPrincipalAccessRights;
                return response;
            }

            if (request is RetrieveUserPrivilegeByPrivilegeNameRequest privilegeRequest)
            {
                var response = new RetrieveUserPrivilegeByPrivilegeNameResponse();
                response.Results["RolePrivileges"] = UserHasPrivilege(privilegeRequest.UserId, privilegeRequest.PrivilegeName)
                    ? new[] { new RolePrivilege { Depth = PrivilegeDepth.Basic, PrivilegeId = Guid.NewGuid() } }
                    : Array.Empty<RolePrivilege>();
                return response;
            }

            if (request is RetrieveSharedPrincipalsAndAccessRequest)
            {
                var response = new RetrieveSharedPrincipalsAndAccessResponse();
                response.Results["PrincipalAccesses"] = RetrievedSharedPrincipalAccesses.ToArray();
                return response;
            }

            // GrantAccessRequest／ModifyAccessRequest／RevokeAccessRequestは、Dataverse本体の
            // 共有権エンジンを模倣せず、呼び出しの記録だけを行う（テストはExecutedRequestsを検証する）。
            return request switch
            {
                GrantAccessRequest => new GrantAccessResponse(),
                ModifyAccessRequest => new ModifyAccessResponse(),
                RevokeAccessRequest => new RevokeAccessResponse(),
                _ => throw new NotSupportedException($"未対応のリクエスト: {request.GetType().Name}"),
            };
        }

        private static Entity CloneEntity(Entity source)
        {
            var clone = new Entity(source.LogicalName, source.Id)
            {
                RowVersion = source.RowVersion,
            };
            foreach (var attribute in source.Attributes)
            {
                clone[attribute.Key] = CloneValue(attribute.Value);
            }
            return clone;
        }

        private static object? CloneValue(object? value)
            => value switch
            {
                EntityReference reference => new EntityReference(reference.LogicalName, reference.Id),
                OptionSetValue optionSet => new OptionSetValue(optionSet.Value),
                _ => value,
            };

        public EntityCollection RetrieveMultiple(QueryBase queryBase)
        {
            var query = (QueryExpression)queryBase;
            var readFailure = BeforeRetrieveMultiple?.Invoke(query);
            if (readFailure != null)
            {
                throw readFailure;
            }

            var candidates = _store.Values
                .Where(e => e.LogicalName == query.EntityName)
                .Where(e => MatchesQuery(e, query));

            var ordered = candidates.AsEnumerable();
            foreach (var order in query.Orders)
            {
                ordered = order.OrderType == OrderType.Descending
                    ? ordered.OrderByDescending(e => e.GetAttributeValue<object>(order.AttributeName))
                    : ordered.OrderBy(e => e.GetAttributeValue<object>(order.AttributeName));
            }

            var list = ordered.ToList();
            if (query.TopCount.HasValue)
            {
                list = list.Take(query.TopCount.Value).ToList();
            }

            var collection = new EntityCollection();
            collection.Entities.AddRange(list);
            collection.MoreRecords = false;
            return collection;
        }

        private static bool MatchesFilter(Entity entity, FilterExpression filter)
        {
            var results = filter.Conditions
                .Select(condition => MatchesCondition(entity, condition))
                .Concat(filter.Filters.Select(child => MatchesFilter(entity, child)))
                .ToList();

            if (results.Count == 0)
            {
                return filter.FilterOperator == LogicalOperator.And;
            }

            return filter.FilterOperator == LogicalOperator.Or
                ? results.Any(result => result)
                : results.All(result => result);
        }

        private bool MatchesQuery(Entity entity, QueryExpression query)
        {
            if (!MatchesFilter(entity, query.Criteria))
                return false;

            foreach (var link in query.LinkEntities)
            {
                var parentValue = entity.GetAttributeValue<object>(link.LinkFromAttributeName);
                if (parentValue == null)
                    return false;

                var related = _store.Values.Any(candidate =>
                    candidate.LogicalName == link.LinkToEntityName
                    && Equals(candidate.GetAttributeValue<object>(link.LinkToAttributeName), parentValue)
                    && MatchesFilter(candidate, link.LinkCriteria));
                if (!related)
                    return false;
            }

            return true;
        }

        private static bool MatchesCondition(Entity entity, ConditionExpression condition)
        {
            if (condition.Operator == ConditionOperator.Null)
            {
                return !entity.Attributes.Contains(condition.AttributeName)
                    || entity[condition.AttributeName] == null;
            }

            if (condition.Operator == ConditionOperator.NotNull)
            {
                return entity.Attributes.Contains(condition.AttributeName)
                    && entity[condition.AttributeName] != null;
            }

            if (condition.Operator != ConditionOperator.Equal)
            {
                throw new NotSupportedException($"未対応の演算子: {condition.Operator}");
            }

            var value = entity.GetAttributeValue<object>(condition.AttributeName);
            var expected = condition.Values.Single();
            if (value == null && condition.AttributeName == entity.LogicalName + "id")
            {
                // 主キーの条件は、属性に入れていなくても行のIDで照合する。
                value = entity.Id;
            }

            if (value is EntityReference entityRef && expected is Guid expectedGuid)
            {
                return entityRef.Id == expectedGuid;
            }

            if (value is OptionSetValue optionSet && expected is int expectedOption)
            {
                return optionSet.Value == expectedOption;
            }

            return Equals(value, expected);
        }
    }
}
