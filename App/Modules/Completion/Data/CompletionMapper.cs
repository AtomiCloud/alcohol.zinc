using Domain.Completion;

namespace App.Modules.Completion.Data
{
    public static class CompletionMapper
    {
        public static CompletionModel ToDomain(this CompletionData data)
        {
            if (data == null) return null;
            return new CompletionModel
            {
                Date = data.Date,
                TaskId = data.TaskId
            };
        }

        public static CompletionData ToData(this CompletionModel model)
        {
            if (model == null) return null;
            return new CompletionData
            {
                Date = model.Date,
                TaskId = model.TaskId
            };
        }
    }
}
