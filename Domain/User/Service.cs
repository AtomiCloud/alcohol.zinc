using CSharp_Result;

namespace Domain.User;

public class UserService(
  IUserRepository repo,
  ITransactionManager tm
) : IUserService
{
  public Task<Result<IEnumerable<UserPrincipal>>> Search(UserSearch search)
  {
    return repo.Search(search);
  }

  public Task<Result<User?>> GetById(string id)
  {
    return repo.GetById(id);
  }

  public Task<Result<User?>> GetByUsername(string username)
  {
    return repo.GetByUsername(username);
  }

  public Task<Result<UserPrincipal>> Create(string id, UserRecord record, Func<Task<Result<Unit>>>? sync)
  {
    return tm.Start(() =>
      repo.Create(id, record)
        .DoAwait(DoType.MapErrors, _ => sync?.Invoke() ?? new Unit().ToAsyncResult())
    );
  }

  public Task<Result<UserPrincipal?>> Update(string id, UserRecord record, Func<Task<Result<Unit>>>? sync)
  {
    return tm.Start(() => repo.Update(id, record)
      .DoAwait(DoType.MapErrors, _ => sync?.Invoke() ?? new Unit().ToAsyncResult())
    );
  }

  public Task<Result<Unit?>> Delete(string id)
  {
    return repo.Delete(id);
  }
}
