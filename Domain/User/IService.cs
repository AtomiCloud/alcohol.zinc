using CSharp_Result;

namespace Domain.User;

public interface IUserService
{

  Task<Result<IEnumerable<UserPrincipal>>> Search(UserSearch search);

  Task<Result<User?>> GetById(string id);
  Task<Result<User?>> GetByUsername(string username);

  Task<Result<UserPrincipal>> Create(string id, UserRecord record, Func<Task<Result<Unit>>>? sync);
  Task<Result<UserPrincipal?>> Update(string id, UserRecord record, Func<Task<Result<Unit>>>? sync);

  Task<Result<Unit?>> Delete(string id);
}
