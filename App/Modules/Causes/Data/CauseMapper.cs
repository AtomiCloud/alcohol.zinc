using Domain.Cause;

namespace App.Modules.Causes.Data
{
    public static class CauseMapper
    {
        // Data => Domain (reuse chain)
        public static CauseRecord ToRecord(this CauseData data)
            => new() { Key = data.Key, Name = data.Name };

        public static CausePrincipal ToPrincipal(this CauseData data)
            => new() { Id = data.Id, Record = data.ToRecord() };

        public static Cause ToDomain(this CauseData data)
            => new() { Principal = data.ToPrincipal() };

        // Domain => Data
        // Variant 1: Create (no Id; DB generates)
        public static CauseData ToData(this CauseRecord record)
            => new() { Key = record.Key, Name = record.Name };

        // Variant 2: Mutable update on existing Data
        public static CauseData ToData(this CauseData data, CauseRecord record)
        {
            // Key is immutable after creation
            data.Name = record.Name;
            return data;
        }
    }
}
