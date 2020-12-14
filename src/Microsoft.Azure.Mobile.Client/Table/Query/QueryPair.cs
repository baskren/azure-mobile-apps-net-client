using System;
using System.Collections.ObjectModel;
using System.Linq.Expressions;
using Microsoft.WindowsAzure.MobileServices;

namespace Microsoft.WindowsAzure.MobileServices.Sync
{
    public class QueryPair<T> where T : IBaseModel<T>
    {

        #region Properties
        public string Id { get; private set; }

        public Expression<Func<T, bool>> Predicate { get; private set; }

        public Func<T, bool> Lambda { get; private set; }
        #endregion

        #region Constructor
        public QueryPair()
        {
            Id = "all";
        }

        public QueryPair(string id, Expression<Func<T, bool>> predicate)
        {
            Id = id;
            Predicate = predicate;
            Lambda = predicate?.Compile();
        }
        #endregion


        #region Operators
        public static QueryPair<T> Or(QueryPair<T> a, QueryPair<T> b)
        {
            Expression<Func<T, bool>> predicate = null;
            string id = "all";
            if (a?.Predicate != null && b?.Predicate != null)
            {
                predicate = a.Predicate.Or(b.Predicate);
                id = "(" + a.Id + ")OR(" + b.Id + ")";
            }
            else if (a?.Predicate != null)
            {
                predicate = a.Predicate;
                id = a.Id;
            }
            else if (b?.Predicate != null)
            {
                predicate = b.Predicate;
                id = b.Id;
            }
            return new QueryPair<T>(id, predicate);
        }

        public static QueryPair<T> And(QueryPair<T> a, QueryPair<T> b)
        {
            Expression<Func<T, bool>> predicate = null;
            string id = "all";
            if (a?.Predicate != null && b?.Predicate != null)
            {
                predicate = a.Predicate.And(b.Predicate);
                id = "(" + a.Id + ")AND(" + b.Id + ")";
            }
            else if (a?.Predicate != null)
            {
                predicate = a.Predicate;
                id = a.Id;
            }
            else if (b?.Predicate != null)
            {
                predicate = b.Predicate;
                id = b.Id;
            }
            return new QueryPair<T>(id, predicate);
        }
        #endregion


        public override string ToString()
        {
            return "[" + Id + "][" + Predicate + "]";
        }

    }


}

