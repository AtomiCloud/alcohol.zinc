# Developer Documentation

This file contains technical implementation details and patterns used in this codebase.

## Financial Data Storage

This codebase uses specific patterns for storing financial data with precision to avoid floating point errors and ensure accurate calculations.

### Money Storage - Cents + Currency

**Money values** are stored as whole numbers (cents) plus currency code in the database, and use NodaMoney in the domain layer.

#### Implementation Pattern

**Domain Layer (Business Logic):**

```csharp
using NodaMoney;

public record HabitRecord
{
    public Money Stake { get; init; }  // e.g., Money(10.50, "SGD")
}
```

**Data Layer (Database Storage):**

```csharp
public class HabitData
{
    public int StakeCents { get; set; }      // 1050 (for $10.50)
    public string StakeCurrency { get; set; } = "SGD";
}
```

**Mapping Between Layers:**

```csharp
// Domain → Data (storing)
data.StakeCents = (int)(principal.Record.Stake.Amount * 100);  // $10.50 * 100 = 1050 cents
data.StakeCurrency = principal.Record.Stake.Currency.Code;     // "SGD"

// Data → Domain (loading)
Stake = new Money(data.StakeCents / 100m, Currency.FromCode(data.StakeCurrency))  // 1050/100 = $10.50 SGD
```

#### Benefits

- ✅ **No floating point precision errors** for monetary calculations
- ✅ **Integer arithmetic** is exact and fast
- ✅ **Multi-currency support** with proper currency handling
- ✅ **NodaMoney integration** provides rich money operations in domain layer

### Percentage Storage - Basis Points

**Basis points** (bp or bps) are used throughout this codebase for storing percentage values with precision.

### Definition

- **1 basis point = 0.01%** (one hundredth of a percent)
- **100 basis points = 1%**
- **10,000 basis points = 100%**

### Examples

```
25.5% = 2,550 basis points
50% = 5,000 basis points
0.25% = 25 basis points
100% = 10,000 basis points
```

### Implementation in Habit System

**Domain Layer (Business Logic):**

```csharp
public record HabitRecord
{
    public decimal Ratio { get; init; }  // 0.255 (25.5%)
}
```

**Data Layer (Database Storage):**

```csharp
public class HabitData
{
    public int RatioBasisPoints { get; set; }  // 2550 (25.5% as basis points)
}
```

**Mapping Between Layers:**

```csharp
// Domain → Data (storing)
data.RatioBasisPoints = (int)(principal.Record.Ratio * 10000);  // 0.255 * 10000 = 2550

// Data → Domain (loading)
Ratio = data.RatioBasisPoints / 10000m  // 2550 / 10000 = 0.255
```

### Benefits

- ✅ **No floating point precision errors** when storing percentages
- ✅ **Integer storage** in database (faster, more reliable than decimals)
- ✅ **Industry standard** for financial percentage calculations
- ✅ **Precise calculations** for money transfers and charity donations

## Usage in Codebase

These financial storage patterns are used throughout:

### Money (Cents + Currency)

- **Habit stakes**: User monetary commitments for habit accountability
- **Charity donations**: Penalty amounts transferred when habits fail
- **Any monetary values**: Precise financial calculations without rounding errors

### Percentages (Basis Points)

- **Habit ratios**: Percentage of stake money going to charity
- **Financial calculations**: Ensuring exact penny-accurate transfers
- **Any percentage-based business logic**: Avoiding decimal precision issues

## Guidelines for Developers

1. **Always use NodaMoney.Money** in domain models for monetary values
2. **Always store as cents + currency** in data models
3. **Always use decimal** for percentages in domain models
4. **Always store as basis points** in data models
5. **Convert only at domain/data boundaries** - never mix types within a layer
6. **Use mappers** to handle conversions between storage and business representations

This ensures all financial calculations are precise and audit-compliant.
