using App.StartUp.Database;
using CSharp_Result;
using Domain.Completion;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Completion.Data
{
    public class CompletionRepository(MainDbContext db, ILogger<CompletionRepository> logger) : ICompletionRepository
    {
        public async Task<Result<CompletionModel>> Get(DateOnly date, int taskId)
        {
            try
            {
                logger.LogInformation("Retrieving Completion by Date: {Date}, TaskId: {TaskId}", date, taskId);
                var data = await db.Completions.AsNoTracking().FirstOrDefaultAsync(x => x.Date == date && x.TaskId == taskId);
                if (data == null)
                {
                    logger.LogWarning("Completion not found for Date: {Date}, TaskId: {TaskId}", date, taskId);
                    return (CompletionModel?)null;
                }
                return data.ToDomain();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving Completion by Date: {Date}, TaskId: {TaskId}", date, taskId);
                return e;
            }
        }

        public async Task<Result<List<CompletionModel>>> GetByTaskIdAsync(int taskId)
        {
            try
            {
                logger.LogInformation("Retrieving Completions for TaskId: {TaskId}", taskId);
                var data = await db.Completions.AsNoTracking().Where(x => x.TaskId == taskId).ToListAsync();
                logger.LogInformation("Retrieved {Count} Completions for TaskId: {TaskId}", data.Count, taskId);
                return data.Select(x => x.ToDomain()).ToList();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving Completions for TaskId: {TaskId}", taskId);
                return e;
            }
        }

        public async Task<Result<CompletionModel>> Create(CompletionModel model)
        {
            try
            {
                logger.LogInformation("Adding Completion for TaskId: {TaskId}, Date: {Date}", model.TaskId, model.Date);
                var data = model.ToData();
                var r = db.Completions.Add(data);
                await db.SaveChangesAsync();
                logger.LogInformation("Completion added for TaskId: {TaskId}, Date: {Date}", model.TaskId, model.Date);
                return r.Entity.ToDomain();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to create Completion for TaskId: {TaskId}, Date: {Date}", model.TaskId, model.Date);
                return e;
            }
        }

        public async Task<Result<Unit?>> Delete(global::System.DateOnly date, int taskId)
        {
            try
            {
                logger.LogInformation("Deleting Completion for Date: {Date}, TaskId: {TaskId}", date, taskId);
                var data = await db.Completions.FirstOrDefaultAsync(x => x.Date == date && x.TaskId == taskId);
                if (data == null)
                {
                    logger.LogWarning("Completion not found for delete, Date: {Date}, TaskId: {TaskId}", date, taskId);
                    return (Unit?)null;
                }

                db.Completions.Remove(data);
                await db.SaveChangesAsync();
                logger.LogInformation("Completion deleted for Date: {Date}, TaskId: {TaskId}", date, taskId);
                return new Unit();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to delete Completion for Date: {Date}, TaskId: {TaskId}", date, taskId);
                return e;
            }
        }
    }
}
