CRM HubSpot Integration Conversation

Summary

- Goal: Provide a generic CRM interface to add/remove users with a marketing consent flag, and implement it for HubSpot. Configuration must be HubSpot‑agnostic at the interface level, and for HubSpot, pass a token via the existing HttpClient pattern (similar to Logto external client).
- Implementation:
  - Interface: `ICrmManagement` with `UpsertUser(CrmUser)` and `RemoveUser(string email)`; `CrmUser` includes `Email`, `FirstName?`, `LastName?`, `MarketingConsent`.
  - HubSpot: `HubSpotCrmManagement` uses contact search + create/patch, and updates consent via the HubSpot Communication Preferences API (subscription statuses) instead of a custom contact property.
  - Config: `HttpClient.HubSpot` for base URL and bearer token; `HubSpotOption` for `SubscriptionTypeId` and optional GDPR `LegalBasis`/`LegalBasisExplanation`.
  - Models: External HubSpot request/response models are records under `App/StartUp/Services/Crm/HubSpot/Models/`.
  - DI: `AddCrmService()` registers `ICrmManagement` -> `HubSpotCrmManagement`.
  - Tests: Integration tests in `IntTest/Crm/HubSpotCrmTests.cs` with stubbed `HttpMessageHandler` and FluentAssertions.

Key Files

- Interface and DI
  - App/StartUp/Services/Crm/CrmManagement.cs
  - App/StartUp/Services/CrmService.cs
  - App/StartUp/Server.cs (calls `services.AddCrmService()`)
- HubSpot implementation
  - App/StartUp/Services/Crm/HubSpot/HubSpotCrmManagement.cs
  - App/StartUp/Services/Crm/HubSpot/Models/HubSpotModels.cs
- Config and registration
  - App/StartUp/Registry/HttpClients.cs (adds `HubSpot`)
  - App/StartUp/Options/HubSpotOption.cs
  - App/StartUp/Options/OptionsExtensions.cs (registers `HubSpotOption`)
  - App/Config/settings.yaml (adds `HttpClient: HubSpot` and `HubSpot:` block)
  - App/Config/settings.example.yaml (examples)
- Tests
  - IntTest/Crm/HubSpotCrmTests.cs
  - IntTest/IntTest.csproj (references App)

How It Works

- UpsertUser:
  1. POST `crm/v3/objects/contacts/search` by email.
  2. If found: PATCH contact; else: POST contact.
  3. PUT `communication-preferences/v3/status/email/{email}` with `subscriptionStatuses` using `HubSpotOption.SubscriptionTypeId` and consent; includes legal basis fields if configured.
- RemoveUser:
  1. Search by email.
  2. If found: DELETE contact.

Config To Set

- Token (no "Bearer " prefix): `App/Config/settings.yaml` → `HttpClient → HubSpot → BearerAuth`.
- Subscription type and GDPR:
  - `HubSpot → SubscriptionTypeId: <int>`
  - Optional `HubSpot → LegalBasis`, `HubSpot → LegalBasisExplanation`.

Quick Usage

- Inject `ICrmManagement` and call:
  - `UpsertUser(new CrmUser { Email, FirstName?, LastName?, MarketingConsent })`
  - `RemoveUser(email)`

Integration Tests

- Run: `dotnet test IntTest -v n`
- Tests cover create+subscribe, update+subscribe, delete, and no‑op delete when contact not found, asserting endpoints and payloads.

Notes

- The CRM interface is provider‑agnostic.
- HubSpot’s subscription statuses are used (not a custom contact property) to ensure email compliance.
- External models are records under a models folder (no sealed classes) per request.

Open Follow‑ups (optional)

- Support multiple subscription type IDs.
- Conditional inclusion of legal basis based on a `GdprEnabled` flag.

Transcript

User: I want an interface to save and remail CRMs and the CRM I' using is HubSpot. So please design an interface such that I can add or remove users from there with a flag of whether they accepted our marketing consent or not. And then I want you to create an implementation of this interface in in for hubspot itself, right? And then add a configuration for it. Please study how the modules and startup folder works. Please make sure that the interface doesn't assume anything about hard spot. So for the configuration I just need to pass in the hotspot token. Please refer to the logto API so that is how we add an external client.

Assistant: I’m going to scan the repo to understand the modules/startup pattern and how the Logto external client is wired, then scaffold a CRM interface and a HubSpot implementation with config.

Assistant: I added a generic CRM interface, a HubSpot implementation, registered it in Startup, and wired config using the existing HttpClient pattern (like Logto).

Assistant (details):

- Added `ICrmManagement` + `CrmUser`.
- Implemented `HubSpotCrmManagement` using contacts search + create/patch.
- Added `HttpClients.HubSpot` and `HttpClient.HubSpot` config (base address, bearer token).
- Registered in DI with `AddCrmService()` and called it from `Server`.

User: yes, and help me write a simple intergation test (in IntTest) and use fluentassertion to let me test this

also, dont use sealed class, for external models, just create them in a model folder -- prefer records and class

Assistant: Switched consent handling to HubSpot subscriptions API, refactored external models into a models folder with records, and added FluentAssertions integration tests.

Assistant (details):

- Replaced contact property consent with calls to Communication Preferences (subscriptions) API.
- Created `HubSpotOption` for `SubscriptionTypeId` and optional `LegalBasis` and `LegalBasisExplanation`.
- Moved external models to `Models` folder as records.
- Added tests using stubbed HttpMessageHandlers covering create/update/delete and subscription updates.

User: what is legalbasis and legalbasisexplanation? what is subscription? is this a thing in hubspot?

Assistant: Explained that subscriptions are HubSpot’s opt-in per subscription type; `legalBasis`/`legalBasisExplanation` capture GDPR lawful basis and context. Recommended adding `SubscriptionTypeId` and legal basis config; offered to update code and tests accordingly.

User: yes please

Assistant: Implemented `HubSpotOption` and updated the implementation to use `PUT communication-preferences/v3/status/email/{email}` with `subscriptionStatuses`, including legal basis values from config; updated tests to assert the new endpoint and payload.
