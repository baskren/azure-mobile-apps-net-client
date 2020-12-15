using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Microsoft.WindowsAzure.MobileServices.Sync
{
    public interface ILiveCollectionTable
    {
        bool IsUpdatingCollection { get; }

        bool IsLoading { get; }

        bool IsServerPulling { get; set; }

        event EventHandler<System.Collections.Specialized.NotifyCollectionChangedEventArgs> CollectionChanged;

        event EventHandler LoadingCompleted;

        event EventHandler<bool> ServerPullingChanged;

        Type Type { get; }

        Task WaitForLoading();

        void ProcessJObjects(IEnumerable<JObject> items);

        void RemoveItemsAtIds(IEnumerable<string> ids);
    }

    public interface ILiveCollectionTable<T> : ILiveCollectionTable, IMobileServiceSyncTable<T> where T : IBaseModel<T>
    {
        ObservableConcurrentCollection<T> Collection { get; }

        Task PullAsync(QueryPair<T> queryPair);
    }
}
