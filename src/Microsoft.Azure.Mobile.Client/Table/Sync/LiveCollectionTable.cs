using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Specialized;
#if XAMARIN
using Xamarin.Essentials;
#endif

namespace Microsoft.WindowsAzure.MobileServices.Sync
{
    public class LiveCollectionTable : ILiveCollectionTable
    {
        static readonly object _tablesLock = new object();
        static Dictionary<string, ILiveCollectionTable> _tables;
        public static Dictionary<string, ILiveCollectionTable> Tables
        {
            get
            {
                if (_tables is null)
                {
                    lock (_tablesLock)
                        _tables = new Dictionary<string, ILiveCollectionTable>();
                }
                return _tables;
            }
        }

        public static void Reset()
        {
            _tables = null;
        }

        #region Properties
        public bool IsUpdatingCollection { get; private set; }

        public virtual Type Type { get; }

        public bool IsLoading { get; protected set; } = true;

        int _serverPullCount;
        public bool IsServerPulling
        {
            get => _serverPullCount > 0;
            set
            {
                if (value)
                    _serverPullCount++;
                else
                    _serverPullCount--;
                if (_serverPullCount == 1 || _serverPullCount == 0)
                    ServerPullingChanged?.Invoke(this, IsServerPulling);
            }
        }
        #endregion


        #region Events
        public event EventHandler<NotifyCollectionChangedEventArgs> CollectionChanged;

        public event EventHandler LoadingCompleted;

        public event EventHandler<bool> ServerPullingChanged;
        #endregion


        public virtual void ProcessJObjects(IEnumerable<JObject> items)
        {
            throw new NotImplementedException();
        }

        public virtual void RemoveItemsAtIds(IEnumerable<string> ids)
        {
            throw new NotImplementedException();
        }

        protected void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            CollectionChanged?.Invoke(this, args);
        }

        protected virtual void OnLoadingCompleted()
        {
            IsLoading = false;
            LoadingCompleted?.Invoke(this, null);
        }

        #region Init
        public async Task WaitForLoading()
        {
            while (IsLoading)
            {
                if (MobileServiceClient.Verbose)
                    System.Diagnostics.Debug.WriteLine("[LiveCollectionTable." + DebugExtensions.CallerString() + ": T [" + Type + "]");
                await Task.Delay(500);
            }
            if (MobileServiceClient.Verbose)
                System.Diagnostics.Debug.WriteLine("[LiveCollectionTable." + DebugExtensions.CallerString() + ": T [" + Type + "]");
            return;
        }
        #endregion
    }

    public class LiveCollectionTable<T> : LiveCollectionTable, ILiveCollectionTable<T> where T : IBaseModel<T>
    {
        #region Properties
        public ObservableConcurrentCollection<T> Collection { get; private set; } = new ObservableConcurrentCollection<T>();

        public override Type Type => typeof(T);
        #endregion


        #region Fields
        readonly private IMobileServiceSyncTable<T> innerTable;
        static readonly JsonSerializer serializer = new JsonSerializer
        {
            NullValueHandling = NullValueHandling.Ignore
        };
        #endregion


        #region Constructor
        internal LiveCollectionTable(string tableName, MobileServiceTableKind kind, MobileServiceClient client)
        {
            Tables.Add(tableName, this);
            innerTable = new MobileServiceSyncTable<T>(tableName, kind, client);
            Task.Run(async () =>
            {
                await Task.Delay(5).ConfigureAwait(false);
                var items = await ReadAsync();
                var itemsArray = items.ToArray();
                //System.Diagnostics.Debug.WriteLine("[LiveCollectionTable." + CallerString() + ": init<"+typeof(T)+"> [" + itemsArray.Length + "]");
                if (Collection.AddRange(itemsArray) is NotifyCollectionChangedEventArgs args)
                    OnCollectionChanged(Collection, args);
                //System.Diagnostics.Debug.WriteLine("[LiveCollectionTable." + CallerString() + ": init<" + typeof(T) + "> ["  + "]");
                OnLoadingCompleted();
                //System.Diagnostics.Debug.WriteLine("[LiveCollectionTable." + CallerString() + ": init<" + typeof(T) + "> [" + "]");
            });
#if XAMARIN
            Xamarin.Essentials.Connectivity.ConnectivityChanged += Connectivity_ConnectivityChanged;
#endif
        }
        #endregion


        #region Server Pull Queuing
        readonly List<QueryPair<T>> PendingQueries = new List<QueryPair<T>>();

        protected override void OnLoadingCompleted()
        {
            base.OnLoadingCompleted();
            ProcessNextPendingServerRefresh();
        }

#if XAMARIN
        private void Connectivity_ConnectivityChanged(object sender, Xamarin.Essentials.ConnectivityChangedEventArgs e)
        {
            if (e.NetworkAccess == Xamarin.Essentials.NetworkAccess.Internet)
                ProcessNextPendingServerRefresh();
        }
#endif

        public void ProcessNextPendingServerRefresh()
        {
            if (PendingQueries.FirstOrDefault() is QueryPair<T> pendingQuery)
                Task.Run(async () => await InnerPullAsync(pendingQuery)).ConfigureAwait(false);
        }

        bool _pulling;
        public Task PullAsync(QueryPair<T> queryPair)
        {
            if (PendingQueries.Any(q => q.Id == queryPair.Id))
                return Task.CompletedTask;

            PendingQueries.Add(queryPair);

            return InnerPullAsync(queryPair);
        }

        Task InnerPullAsync(QueryPair<T> queryPair)
        {
#if XAMARIN
            if (_pulling || IsLoading || Xamarin.Essentials.Connectivity.NetworkAccess != Xamarin.Essentials.NetworkAccess.Internet)
                return Task.CompletedTask;
#else
            if (_pulling || IsLoading)
                return Task.CompletedTask;
#endif
            _pulling = true;

            var query = CreateQuery();
            if (queryPair?.Predicate != null)
                query = query.Where(queryPair.Predicate);
            System.Diagnostics.Debug.WriteLine("[LiveCollectionTable]" + GetType().ToString() + "." + DebugExtensions.CallerString() + ": Colleciton.Count[" + Collection.Count() + "] QueryPair=[" + queryPair + "] query=[" + query + "]");
            var result = PullAsync(queryPair.Id, query, true, CancellationToken.None, null);
            System.Diagnostics.Debug.WriteLine("[LiveCollectionTable]" + GetType() + "." + DebugExtensions.CallerString() + ": Colleciton.Count[" + Collection.Count() + "]");

            PendingQueries.Remove(queryPair);
            _pulling = false;
            ProcessNextPendingServerRefresh();

            return result;
        }

        #endregion


        #region Collection Update from Server
        public override void ProcessJObjects(IEnumerable<JObject> serverJObjects)
        {
            //if (MobileServiceClient.Verbose)
            System.Diagnostics.Debug.WriteLine("[LiveCollectionTable." + DebugExtensions.CallerString() + ": serverJObjects<" + typeof(T) + ">.Count[" + serverJObjects.Count() + "]");
            var deleteItems = new List<T>();
            var insertItems = new List<T>();
            foreach (var jobject in serverJObjects)
            {
                if (jobject.ToObject<T>(serializer) is T serverItem)
                {
                    if (Collection.FirstOrDefault(i => i.Id == serverItem.Id) is T oldItem)
                    {
                        if (serverItem.Deleted)
                            deleteItems.Add(serverItem);
                        else
                            oldItem.UpdateFrom(serverItem);
                    }
                    else if (!serverItem.Deleted)
                        insertItems.Add(serverItem);
                }
                else
                    throw new Exception("huh?");
            }
#if XAMARIN
            MainThread.BeginInvokeOnMainThread(() =>
            {
#endif
                if (deleteItems.Any())
                    OnCollectionChanged(Collection, Collection.RemoveRange(deleteItems));
                if (insertItems.Any())
                    OnCollectionChanged(Collection, Collection.AddRange(insertItems));

                System.Diagnostics.Debug.WriteLine("[LiveCollectionTable]" + GetType() + "." + DebugExtensions.CallerString() + ": Collection.Count[" + Collection.Count + "]");
#if XAMARIN
            });
#endif
        }

        public override void RemoveItemsAtIds(IEnumerable<string> ids)
        {
            var deleteItems = new List<T>();
            foreach (var id in ids)
                if (Collection.FirstOrDefault(i => i.Id == id) is T item)
                    deleteItems.Add(item);

            if (deleteItems.Any())
#if XAMARIN
                MainThread.BeginInvokeOnMainThread(() =>
#endif
                OnCollectionChanged(Collection, Collection.RemoveRange(deleteItems))
#if XAMARIN
                    );
#else
                    ;
#endif
        }
        #endregion


        #region Wrapper Implementation
        public MobileServiceClient MobileServiceClient => innerTable.MobileServiceClient;

        public string TableName => innerTable.TableName;

        public MobileServiceRemoteTableOptions SupportedOptions { get => innerTable.SupportedOptions; set => innerTable.SupportedOptions = value; }

        public IMobileServiceTableQuery<T> CreateQuery()
        {
            return innerTable.CreateQuery();
        }

        public Task DeleteAsync(T instance)
        {
            throw new NotSupportedException();
            //return innerTable.DeleteAsync(instance);
        }

        public Task DeleteAsync(JObject instance)
        {
            throw new NotSupportedException();
            // return innerTable.DeleteAsync(instance);
        }

        public IMobileServiceTableQuery<T> IncludeTotalCount()
        {
            return innerTable.IncludeTotalCount();
        }

        public Task InsertAsync(T instance)
        {
            throw new NotSupportedException();
            //return innerTable.InsertAsync(instance);
        }

        public Task<JObject> InsertAsync(JObject instance)
        {
            throw new NotSupportedException();
            //return innerTable.InsertAsync(instance);
        }

        public Task<T> LookupAsync(string id)
        {
            return innerTable.LookupAsync(id);
        }

        public IMobileServiceTableQuery<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
        {
            return innerTable.OrderBy(keySelector);
        }

        public IMobileServiceTableQuery<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
        {
            return innerTable.OrderByDescending(keySelector);
        }

        public async Task PullAsync<U>(string queryId, IMobileServiceTableQuery<U> query, bool pushOtherTables, CancellationToken cancellationToken, PullOptions pullOptions)
        {
            await Task.Delay(5);
            IsServerPulling = true;
            await innerTable.PullAsync(queryId, query, pushOtherTables, cancellationToken, pullOptions);
            // if things go wrong, an exception is thrown during the processing of the above line
            IsServerPulling = false;
        }



        public async Task PullAsync(string queryId, string query, IDictionary<string, string> parameters, bool pushOtherTables, CancellationToken cancellationToken, PullOptions pullOptions)
        {
            await Task.Delay(5);
            IsServerPulling = true;
            await innerTable.PullAsync(queryId, query, parameters, pushOtherTables, cancellationToken, pullOptions);
            IsServerPulling = false;
        }

        public Task PurgeAsync<U>(string queryId, IMobileServiceTableQuery<U> query, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
            //return innerTable.PurgeAsync(queryId, query, cancellationToken);
        }

        public Task PurgeAsync<U>(string queryId, IMobileServiceTableQuery<U> query, bool force, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
            // return innerTable.PurgeAsync(queryId, query, force, cancellationToken);
        }

        public Task PurgeAsync(string queryId, string query, bool force, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
            //return innerTable.PurgeAsync(queryId, query, force, cancellationToken);
        }

        public Task<IEnumerable<T>> ReadAsync()
        {
            return innerTable.ReadAsync();
        }

        public Task<IEnumerable<U>> ReadAsync<U>(IMobileServiceTableQuery<U> query)
        {
            return innerTable.ReadAsync(query);
        }

        public Task<JToken> ReadAsync(string query)
        {
            return innerTable.ReadAsync(query);
        }

        public Task RefreshAsync(T instance)
        {
            return innerTable.RefreshAsync(instance);
        }

        public IMobileServiceTableQuery<U> Select<U>(Expression<Func<T, U>> selector)
        {
            return innerTable.Select(selector);
        }

        public IMobileServiceTableQuery<T> Skip(int count)
        {
            return innerTable.Skip(count);
        }

        public IMobileServiceTableQuery<T> Take(int count)
        {
            return innerTable.Take(count);
        }

        public IMobileServiceTableQuery<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector)
        {
            return innerTable.ThenBy(keySelector);
        }

        public IMobileServiceTableQuery<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
        {
            return innerTable.ThenByDescending(keySelector);
        }

        public Task<IEnumerable<T>> ToEnumerableAsync()
        {
            return innerTable.ToEnumerableAsync();
        }

        public Task<List<T>> ToListAsync()
        {
            return innerTable.ToListAsync();
        }

        public Task UpdateAsync(T instance)
        {
            return innerTable.UpdateAsync(instance);
        }

        public Task UpdateAsync(JObject instance)
        {
            return innerTable.UpdateAsync(instance);
        }

        public IMobileServiceTableQuery<T> Where(Expression<Func<T, bool>> predicate)
        {
            return innerTable.Where(predicate);
        }

        Task<JObject> IMobileServiceSyncTable.LookupAsync(string id)
        {
            return innerTable.LookupAsync(id) as Task<JObject>;
        }
        #endregion




    }
}
