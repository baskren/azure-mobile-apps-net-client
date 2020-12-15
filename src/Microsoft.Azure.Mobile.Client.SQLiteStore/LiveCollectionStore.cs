using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.MobileServices.Query;
using Microsoft.WindowsAzure.MobileServices.Sync;
using Newtonsoft.Json.Linq;
using SQLitePCL;

namespace Microsoft.WindowsAzure.MobileServices.SQLiteStore
{
    public class LiveCollectionStore : MobileServiceSQLiteStore
    {
        public LiveCollectionStore()
        {
        }

        public LiveCollectionStore(string fileName) : base(fileName) { }


        public override async Task UpsertAsync(string tableName, IEnumerable<JObject> items, bool ignoreMissingColumns)
        {
            await base.UpsertAsync(tableName, items, ignoreMissingColumns);
            ILiveCollectionTable table = null;
            if (LiveCollectionTable.Tables?.TryGetValue(tableName, out table) ?? false)
                table.ProcessJObjects(items);
        }

        public override async Task DeleteAsync(string tableName, IEnumerable<string> ids)
        {
            await base.DeleteAsync(tableName, ids);
            ILiveCollectionTable table = null;
            if (LiveCollectionTable.Tables?.TryGetValue(tableName, out table) ?? false)
                table.RemoveItemsAtIds(ids);
        }

        public override Task DeleteAsync(MobileServiceTableQueryDescription query)
        {
            return base.DeleteAsync(query);
        }
    }
}
