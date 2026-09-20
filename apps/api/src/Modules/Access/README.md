# SaasBtp.Access

MicroKit module scaffolded with **dotnet-forge / hexagonal-ddd-slice**.
Structure only — zero domain logic. Ready to plug into any .NET 10 host.

## Layers

| Project                    | Role                                             | May reference        |
|----------------------------|--------------------------------------------------|----------------------|
| `SaasBtp.Access.Domain`         | Domain model + domain-owned ports                | (nothing outward)    |
| `SaasBtp.Access.Application`    | Use cases: CQRS commands/queries/handlers, ports | Domain               |
| `SaasBtp.Access.Infrastructure` | Adapters + composition root                      | Application, Domain  |

Dependencies point inward only. EF Core, if introduced, is confined to
Infrastructure. Ports live in Domain/Application; adapters live in Infrastructure.

## Domain — organised by aggregate

> **Materialisation decision, taken at BP-001 plan slice S1.** The Domain is organised **by
> aggregate**, not by technical layer (`hexagonal-ddd-slice`, not `hexagonal-ddd-layered`).
>
> Reason: the BP-001 Implementation Design §3.1 requires Ownership, Organization Membership and
> Workspace Access to be **autonomous persisted elements — never internal components** of
> Organization or Workspace. A Domain arranged by technical layer (`Model/`, `Abstractions/`)
> makes that separation a naming convention; a Domain arranged by aggregate makes it structural.
>
> Scope of this decision: the **internal organisation** of the Domain project, and nothing else.
> It does **not** decide whether Organization and Workspace form one transactional aggregate or
> two — the Implementation Design deliberately leaves that open (§3.1), and it remains open.

The Domain is organised **by aggregate** (one folder per aggregate), not by technical
layer. At scaffold time it is intentionally **empty** — only the assembly marker and a
`.gitkeep`. Each aggregate is added later (by `add-aggregate`) as a self-contained folder:

```
src/SaasBtp.Access.Domain/<AggregatePlural>/
    <Aggregate>.cs             // aggregate root; guards invariants via CheckRule(new ...Rule(...))
    <Aggregate>Id.cs           // strongly-typed UUIDv7 id
    <ValueObject>.cs           // value objects (subfolder only if many)
    Rules/                     // IBusinessRule invariants, checked via CheckRule
    <Aggregate>Errors.cs       // Result-based errors for EXPECTED business-flow failures
    I<Aggregate>Repository.cs  // domain-required port
    Events/                    // domain events (when the aggregate emits any)
```

Two failure mechanisms coexist (see the recipe's Domain convention): invariants throw via
`CheckRule` → `BusinessRuleViolationException`; expected business-flow failures are
returned as `Result` / `Result<T>` with `<Aggregate>Errors`.

## Application — vertical slices

Use cases are co-located as vertical slices under `Application/Features/<Feature>/`:

```
src/SaasBtp.Access.Application/Features/<Feature>/
    <Feature>Command.cs        // : ICommand / ICommand<Result<...>>
    <Feature>Handler.cs        // : ICommandHandler<...> / IQueryHandler<...>
    <Feature>Validator.cs
src/SaasBtp.Access.Infrastructure/Adapters/         // port implementations
```

## Compose into a host

```csharp
using SaasBtp.Access.Infrastructure.DependencyInjection;

builder.Services.AddAccessModule();
```

## Build

This module is a citizen of the host solution, not its own. It carries no
`Directory.Build.props`, `Directory.Packages.props` or `.slnx` — those are inherited from the
repo root by MSBuild/NuGet nearest-ancestor resolution, so the host's Central Package
Management governs its versions.

```
dotnet build SaasBtp.slnx        # from the repository root
```
