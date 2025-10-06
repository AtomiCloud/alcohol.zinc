using Domain.Charity;

namespace App.Modules.Charities.Data
{
    public static class ExternalIdMapper
    {
        // Data => Domain
        public static ExternalIdRecord ToRecord(this ExternalIdData data)
            => new()
            {
                Source = data.Source,
                ExternalKey = data.ExternalKey,
                Url = data.Url,
                Payload = data.Payload,
                LastSyncedAt = data.LastSyncedAt
            };

        // Domain => Data (Create)
        public static ExternalIdData ToData(this ExternalIdRecord record, Guid charityId)
            => new()
            {
                CharityId = charityId,
                Source = record.Source,
                ExternalKey = record.ExternalKey,
                Url = record.Url,
                Payload = record.Payload,
                LastSyncedAt = record.LastSyncedAt
            };

        // Domain => Data (Mutable update on existing Data)
        public static ExternalIdData ToData(this ExternalIdData data, ExternalIdRecord record)
        {
            // Do not mutate identity fields (Id, CharityId, Source, ExternalKey)
            data.Url = record.Url;
            data.Payload = record.Payload;
            data.LastSyncedAt = record.LastSyncedAt;
            return data;
        }
    }
}

