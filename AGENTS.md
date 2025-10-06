Agent Conventions for This Repository

Purpose: capture patterns to follow when adding or modifying domain/data layers so code stays consistent with the 3‑tier architecture, Result monads, and mapper style used here.

Service/Repository Naming

- If only a single fetch by ID exists, name it `Get(Guid id)` (not `GetById`). Add specific variants only when needed (e.g., `GetByKey`).

Update Signatures

- Update operations accept the immutable identity separately and a record payload: `Update(Guid id, <Record> record)`. Identity (Guid Id) is not mutable and must not be embedded inside a principal or mutated.

Mapper Rules

- Domain => Data

  - Only two variants are needed:
    - Create: `<Data> ToData(this <Record> record)` — does NOT set `Id`. The database generates IDs.
    - Mutable update: `<Data> ToData(this <Data> data, <Record> record)` — apply how the record mutates the data model. Do not change identity fields.

- Data => Domain (always provide all three, and reuse smaller mappers)
  - `<Record> ToRecord(this <Data> data)` — map columns to domain record.
  - `<Principal> ToPrincipal(this <Data> data)` — construct principal using `ToRecord()` for the record.
  - `<Aggregate> ToDomain(this <Data> data)` — construct aggregate using `ToPrincipal()`.

Other Notes

- Avoid storing PII unless explicitly allowed. For charities, do not store emails/phones from external sources (e.g., Pledge).
- Prefer unique constraints at the DB level to enforce invariants; let repositories translate expected DB exceptions into Result failures where appropriate.
- Keep mappers focused and side‑effect free. Business logic (e.g., validation, dedupe) belongs in services/repositories.

Error Handling

- Do not return exceptions in `Result` unless the exception is expected and mapped to a domain/API Problem (e.g., unique constraint -> `EntityConflict`).
- For unexpected exceptions, log and rethrow (`throw;`) so the upstream error pipeline handles them uniformly.

Search vs. GetAll

- Avoid `GetAll()` in repositories/services/controllers unless the dataset is guaranteed to be very small.
- Prefer a `Search` method with a domain search model (e.g., `<Entity>Search`), supporting pagination (`Limit`, `Skip`) and filter fields.

Linting

- Always run `pls lint` after completing a task to ensure repository linters pass. If unavailable in the environment, instruct the user to run it locally.
