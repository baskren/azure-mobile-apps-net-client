using System;

namespace Microsoft.WindowsAzure.MobileServices
{
    public interface IBaseModel<T> : IBaseModel
    {
        public void UpdateFrom(T model);
    }

    public interface IBaseModel
    {
        public string Id { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public bool Deleted { get; set; }

        public string Version { set; get; }
    }
}
