# Habit System - Sequence Diagrams

This document outlines the key flows in the habit tracking system, showing how the frontend, backend, and database interact across different scenarios.

## System Overview

The habit system consists of:

- **Habit Blueprints**: Versioned templates defining habit rules (task, schedule, stakes)
- **Habit Executions**: Daily instances tracking completion status
- **Configuration**: User settings including end-of-day time
- **Financial Processing**: Automatic penalty transfers to charity for failed habits

## Sequence Diagram

```mermaid
sequenceDiagram
    participant F as Frontend
    participant API as Backend API
    participant HS as HabitService
    participant HR as HabitRepository
    participant CS as ConfigService
    participant CR as ConfigRepository
    participant HER as HabitExecutionRepository
    participant PS as PaymentService
    participant CharityAPI as Charity Payment API
    participant DB as Database
    participant Job as Hourly Job

    %% Scenario 1: User creates new habit
    Note over F,DB: Scenario 1: Create New Habit
    F->>API: POST /api/habit (CreateHabitReq)
    API->>HS: Create(userId, habitRecord, charityId)
    HS->>HR: Create(habitPrincipal)
    HR->>DB: INSERT Habit (Id=guid1, PlanId=plan-abc, Version=1)
    DB-->>HR: Success
    HR-->>HS: HabitPrincipal
    HS-->>API: HabitPrincipal
    API-->>F: 201 Created (HabitRes)

    %% Scenario 2: User updates habit
    Note over F,DB: Scenario 2: Update Habit (Creates New Version)
    F->>API: PUT /api/habit/{planId} (UpdateHabitReq)
    API->>HS: Update(planId, updatedHabitRecord)
    HS->>HR: GetLatestVersion(planId)
    HR->>DB: SELECT * FROM Habits WHERE PlanId=planId ORDER BY Version DESC LIMIT 1
    DB-->>HR: Current version (Version=1)
    HR-->>HS: Current HabitPrincipal
    HS->>HR: Create(newHabitPrincipal) // New version
    HR->>DB: INSERT Habit (Id=guid2, PlanId=plan-abc, Version=2)
    DB-->>HR: Success
    HR-->>HS: New HabitPrincipal
    HS-->>API: Updated HabitPrincipal
    API-->>F: 200 OK (HabitRes)

    %% Scenario 3: Hourly job - End of day processing
    Note over Job,DB: Scenario 3: Hourly Job - End of Day Processing
    loop Every Hour
        Job->>CS: GetAllConfigurations()
        CS->>CR: GetAll()
        CR->>DB: SELECT * FROM Configurations
        DB-->>CR: All user configurations
        CR-->>CS: List<Configuration>
        CS-->>Job: List<Configuration>

        loop For each user at end of day
            Note over Job: Check if current hour matches user's EndOfDay
            alt Current hour == user.EndOfDay
                Note over Job: Step 1: Check today's pending executions
                Job->>HER: GetPendingExecutions(userId, today)
                HER->>DB: SELECT * FROM HabitExecutions he JOIN Habits h ON he.HabitId=h.Id WHERE h.UserId=userId AND he.Date=today AND he.Status='Pending'
                DB-->>HER: Today's pending executions
                HER-->>Job: List<PendingExecution>

                loop For each pending execution
                    Job->>HER: UpdateStatus(executionId, Failed)
                    HER->>DB: UPDATE HabitExecutions SET Status='Failed' WHERE Id=executionId
                    DB-->>HER: Success
                    Note over Job: This will trigger penalty processing later
                end

                Note over Job: Step 2: Create tomorrow's executions
                Job->>HS: GetActiveHabitsForUser(userId, tomorrow)
                HS->>HR: GetActiveHabits(userId, tomorrow)
                HR->>DB: SELECT * FROM Habits WHERE UserId=userId AND StartDate <= tomorrow AND EndDate >= tomorrow
                DB-->>HR: Active habits for tomorrow
                HR-->>HS: List<HabitPrincipal>
                HS-->>Job: Active habits for tomorrow

                loop For each active habit for tomorrow
                    Job->>HR: GetLatestVersion(planId)
                    HR->>DB: SELECT * FROM Habits WHERE PlanId=planId ORDER BY Version DESC LIMIT 1
                    DB-->>HR: Latest habit version
                    HR-->>Job: Latest HabitPrincipal

                    Job->>HER: CreateExecution(latestHabitId, tomorrow, status=Pending)
                    HER->>DB: INSERT HabitExecution (HabitId=latest.Id, Date=tomorrow, Status='Pending')
                    DB-->>HER: Success
                    HER-->>Job: Tomorrow's execution created
                end
            end
        end
    end

    %% Scenario 4: User marks habit as completed
    Note over F,DB: Scenario 4: User Marks Habit Completed
    F->>API: PUT /api/habit/execution/{executionId}/complete
    API->>HER: GetExecution(executionId)
    HER->>DB: SELECT * FROM HabitExecutions WHERE Id=executionId
    DB-->>HER: HabitExecution
    HER-->>API: Execution

    alt Status is Pending
        API->>HER: UpdateStatus(executionId, Completed, now())
        HER->>DB: UPDATE HabitExecutions SET Status='Completed', CompletedAt=now() WHERE Id=executionId
        DB-->>HER: Success
        HER-->>API: Updated execution
        API-->>F: 200 OK (ExecutionRes)
    else Status is Failed or Completed
        API-->>F: 400 Bad Request (Cannot modify completed/failed execution)
    end

```

## Scenario Descriptions

### 1. Create New Habit

- User defines a new habit with task, schedule, stakes, and charity
- System creates initial version (Version=1) with new PlanId
- Returns habit blueprint to frontend

### 2. Update Habit (Versioning)

- User modifies existing habit (task, stake amount, etc.)
- System creates new version with same PlanId but incremented Version
- Old executions remain linked to previous version for audit trail
- New executions will reference the latest version

### 3. Hourly Job - End of Day Processing

- Runs every hour checking user configurations
- When current hour matches user's EndOfDay setting:
  - **Step 1**: Mark today's pending executions as Failed (triggers penalties)
  - **Step 2**: Create tomorrow's executions with status=Pending
  - Uses latest habit version ID when creating new executions
  - Only processes habits within their StartDate/EndDate period

### 4. User Marks Habit Completed

- Frontend gets execution ID from dashboard
- Only allows updating executions with status=Pending
- Failed or already completed executions cannot be modified
- Direct update with completion timestamp

## TODO: Additional Scenarios

### 5. User Views Dashboard (TODO)

- Shows all active habits for specified date
- Includes completion status for each habit (Pending/Completed/Failed)
- Provides overview of daily progress
- Returns execution IDs for frontend to use in completion flow

### 6. Financial Penalty Processing (TODO)

- Daily job processes all failed executions from previous day
- Transfers stake money to designated charity based on ratio setting
- Marks executions as payment processed to avoid double-charging
- Handles payment failures and retry logic

## Key Design Principles

1. **Versioning**: Habit blueprints are versioned; executions reference specific versions
2. **Temporal Integrity**: Past executions maintain links to rules active when executed
3. **Latest Version Logic**: New executions always use latest version of habit
4. **Automated Processing**: System automatically creates failed executions and processes penalties
5. **User Control**: Users can complete habits early or view their progress anytime
