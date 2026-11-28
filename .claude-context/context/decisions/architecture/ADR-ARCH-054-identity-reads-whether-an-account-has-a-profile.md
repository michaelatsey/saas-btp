# ADR-ARCH-054 — The Identity module reads whether an account has a profile through a function Access publishes, and applies BP002-R14 at a recovery's effect under an advisory lock on that account

Status: accepted
Date: 2026-11-27
Refs: issue #248 (Goal: "so that BP-002's contract can be written"); BP-002 `process.md` (BP002-R14,
TSK-6, TSK-9), `acceptance.md` (BP002-AC-49, AC-50, AC-57, AC-59); `docs/domain/access.md`
(INV-ACCESS-01); ADR-ARCH-027; ADR-ARCH-028; ADR-ARCH-031; ADR-ARCH-032; ADR-ARCH-033;
ADR-ARCH-034 §4; ADR-ARCH-042; ADR-ARCH-043; ADR-ARCH-044; ADR-ARCH-049; ADR-ARCH-053.

## Context
BP002-R14: a recovery may take as its new contact a contact held by an account that has no profile;
"A contact held by an account that has a profile, because it takes or once took part in a relation,
is refused"; the rule "is applied when the recovery is requested and again when it would take
effect", and if by then that account has a profile, "the recovery does not take effect". A person
has a profile as soon as they take part in a relation (INV-ACCESS-01). Profiles are Access's
(ADR-ARCH-031 §1); in the schema, `access.profiles` holds one row per person, unique on `user_id`
(`0001__access__create_tables.sql`), the account's identifier, `sub` (ADR-ARCH-038 decision 3). Only
the account itself creates its profile: the insert policy admits a row only when `user_id` is
`app.user_id` (`0003__access__create_policies.sql`).

The Identity module is a module of its own, in its own host (ADR-ARCH-042), whose role holds nothing
on another schema (ADR-ARCH-053 decision 1). ADR-ARCH-034 §4: "No module calls another synchronously
... and no authorization decision relies on a local copy, unless an ADR accepts it, stating why an
event or a local copy does not fit and the consequences on transactions, availability and
extraction"; "The one exception accepted is `<module>.may_act_in`". The outbox that carries
integration events is introduced with its first consumer (ADR-ARCH-032 §1); none exists yet.

The race to close (finding G9 of the diff review): the API checks a session once per request, on its
own connection, before the body is read (ADR-ARCH-043 decision 2, ADR-ARCH-053 decision 5); the
profile is written later, in the request's unit of work (ADR-ARCH-033 §4). A founding that passed the
check before the other account's sessions ended can therefore commit its profile after Identity has
read that the account has none, and the account would end while owning a company (BP002-AC-50). No
decision in force bounds how long a request takes between its check and its commit: Npgsql's
Command Timeout bounds one command (ADR-ARCH-027), not a transaction.

What the sources state, read on 2026-11-28:
- PostgreSQL 17, "System Administration Functions"
  (https://www.postgresql.org/docs/17/functions-admin.html): `pg_advisory_xact_lock(key bigint)`
  "Obtains an exclusive transaction-level advisory lock, waiting if necessary";
  `pg_advisory_xact_lock_shared(key bigint)` "Obtains a shared transaction-level advisory lock, waiting
  if necessary". "Explicit Locking" (https://www.postgresql.org/docs/17/explicit-locking.html):
  transaction-level advisory lock requests "are automatically released at the end of the transaction,
  and there is no explicit unlock operation".
- "Transaction Isolation" (https://www.postgresql.org/docs/17/transaction-iso.html), Read Committed:
  "a SELECT query ... sees only data committed before the query began".
- `transaction_timeout` (https://www.postgresql.org/docs/17/runtime-config-client.html; release notes
  of 17.0, https://www.postgresql.org/docs/release/17.0/): added in PostgreSQL 17; "Terminate any
  session that spans longer than the specified amount of time in a transaction". ALTER ROLE
  (https://www.postgresql.org/docs/17/sql-alterrole.html): a role's setting "becomes the session
  default" when the role "starts a new session ... This only happens at login time".
- Supabase (https://supabase.com/docs/guides/platform/upgrading;
  https://supabase.com/docs/guides/database/connecting-to-postgres;
  https://supabase.com/docs/guides/database/postgres/timeouts): the pages read do not state which
  PostgreSQL major version a hosted project runs, only that upgrades to 17 are started by the
  project's owner ("Use the "Upgrade project" button"); a custom role connects to the shared pooler as
  `[ROLE].[PROJECT-REF]`; the Timeouts guide sets a role's `statement_timeout` with `alter role` and
  does not state that a role's settings apply to connections through the pooler. The repository's
  record of PostgreSQL 17.6 (ADR-ARCH-027, ADR-ARCH-028, `apps/api/tests/PostgresImage.cs`) is not
  checked against the hosted projects by anything.

## Decision
1. **What Access publishes.** `access.account_has_profile(account_id uuid) returns boolean`,
   `language sql`, `stable`, `security invoker`, empty `search_path`: true when a row of
   `access.profiles` has that `user_id`. It is granted to `saasbtp_identity` with `usage` on `access`
   and `select` on `access.profiles(user_id)` only, through a policy for `saasbtp_identity` alone that
   admits every row: Identity's flows have no subject (ADR-ARCH-053 decision 3). It reveals whether an
   account has a profile, and nothing else of Access.
2. **When Identity reads it.** At a recovery's request, for the new contact's account (TSK-6, TSK-9),
   and at its effect, as decision 3 states. Nothing else in Identity reads it.
3. **The effect under an exclusive advisory lock on the other account.** When a recovery whose new
   contact belongs to another account reaches its effect (ADR-ARCH-049 decision 1), Identity runs one
   transaction:
   - first statement: `pg_advisory_xact_lock(k)`, where `k` is the other account's lock key
     (decision 5), with `set local lock_timeout` of 30 seconds; a timeout rolls the transaction back
     and the effect is tried again a minute later, so a request that hangs delays the effect without
     changing its outcome;
   - then it reads `access.account_has_profile`. False: the recovery takes effect as BP002-R14 states;
     in the same transaction the other account's authorizations and tokens are revoked
     (ADR-ARCH-044 decision 5) and the account ends. True: the recovery does not take effect, the
     person is told, and nothing of the other account changes;
   - commit releases the lock.

   No session of the other account is ended or suspended unless the recovery takes effect, so when it
   does not, the other account is unchanged, its sessions included (BP002-AC-50).
4. **A shared lock and a session re-check in every unit of work of the API.** ADR-ARCH-033 §4's unit
   of work gains two statements, run by its interceptor right after the statement that sets the
   subject, before any statement of the module:
   - `pg_advisory_xact_lock_shared(k)`, `k` the caller's lock key (decision 5);
   - `select identity.session_is_active(@sid)`, with `sid` from the validated token (ADR-ARCH-053
     decision 5). False: the transaction is rolled back and the request answers 401 with the bare
     challenge of ADR-ARCH-038 decision 4, nothing written.

   Why this closes the race, case by case, for a unit of work of the other account:
   - it took its shared lock before the effect asked for the exclusive one: the effect waits until it
     commits or rolls back, so a profile it writes is committed before the read, and is seen;
   - it asks for its shared lock while the effect holds or waits for the exclusive one: it waits until
     the effect commits; its re-check then runs after that commit, sees the revoked authorization if
     the recovery took effect (Read Committed), and it writes nothing; if the recovery did not take
     effect, it goes on unchanged;
   - it starts after the effect: its re-check sees the revoked authorization.

   Units of work hold the shared lock of their own caller only: they never wait on each other, and a
   person's requests wait only during the effect of a recovery that names their account, for the
   length of one transaction.
5. **The lock key.** A `bigint`: the first 8 bytes of the account's identifier (`sub`, a UUID) read as
   a signed 64-bit integer, computed by one function shared by the API and Identity and passed as a
   parameter. Two accounts with the same key only wait on each other during an effect; correctness
   does not depend on keys being distinct.
6. **Why the lock rather than `transaction_timeout`.** The maintainer's direction of 2026-11-28 on #248
   preferred an enforced timeout: `transaction_timeout` on the API's role, the effect suspending the
   other account's sessions, waiting longer than that bound, then reading the profile; and directed to
   keep the lock if PostgreSQL 17 on the server or the setting's reach through the pooler could not be
   guaranteed from the provider's documentation. Neither can:

   | | `transaction_timeout` with a wait | Advisory lock (decided) |
   |---|---|---|
   | Needs PostgreSQL 17 | Yes; not stated for the hosted projects by Supabase's pages read | No |
   | Needs a role setting through the pooler | Yes; not stated by Supabase's Timeouts guide; read at login only, so pooled connections keep an earlier value | No; a transaction-level lock is released at its transaction's end, in any pool mode |
   | Every unit of work | The session re-check (decision 4), needed too, since the timeout bounds a transaction and not the time between the request's check and its unit of work | One shared lock and the session re-check |
   | The other account during the effect | Suspended for the wait (more than the timeout), restored if the recovery does not take effect | Unchanged; its requests wait for one transaction at most |
   | Every legitimate API transaction | Must stay under the timeout, or be terminated with its session | No limit added |
   | Coupling to Identity in the shared database | The re-check in every unit of work | The re-check and the lock in every unit of work |

   Both designs put an Identity statement in every unit of work; the lock adds one statement and
   needs neither an unstated server version nor an unstated pooler behaviour.
7. **Why neither an event nor a local copy.** No event channel exists yet, and a local copy fed by
   events is up to date only once the event is consumed: a profile created during the delay could be
   missed at the effect (BP002-AC-50), which the rule forbids. The function reads the current state,
   at the moment the rule is applied.
8. **An exception to ADR-ARCH-034 §4's rule**, beside `may_act_in` and the session check of
   ADR-ARCH-053, accepted here with its consequences.
9. **Verification.** BP002-AC-49 (an account without a profile ends, its contact passes), BP002-AC-50
   (a profile created during the delay is seen at the effect, the recovery does not take effect, the
   other account's sessions still work), BP002-AC-57, BP002-AC-59 (refused at the request); a grant
   test: `saasbtp_identity` reads only `access.profiles(user_id)` and executes only that function; and
   three integration tests on PostgreSQL, one per case of decision 4: a founding whose unit of work is
   held open (paused after its shared lock) while the effect starts, then committed: its profile is
   seen and the recovery does not take effect; a founding that asks for its lock while the effect
   holds it: refused with 401 when the recovery takes effect, nothing written; a unit of work after
   the effect: refused. A test that the interceptor runs the lock and the re-check before any module
   statement, and refuses a unit of work whose session ended.

## Consequences
- ADR-ARCH-033 §4 is extended for the API's units of work by decision 4; its other rules hold.
- Transactions: no transaction writes to two modules; the effect is one transaction of the Identity
  module that reads one Access function.
- Availability: both modules share the database (ADR-ARCH-033); the function and the lock are
  available when Identity is.
- Extraction: the shared advisory lock relies on every module sharing one database (ADR-ARCH-033);
  extracting a module from that database requires revisiting this ADR. The lock and the re-check of
  decision 4 are then no longer possible for that module, and BP002-R14 needs another mechanism, by a
  new ADR.
- One more statement per unit of work, and a second session read in it, besides the request's check
  of ADR-ARCH-043.
- The script that creates the function is Access's, named after it (ADR-ARCH-033 §2), and the grant to
  `saasbtp_identity` assumes the role exists (ADR-ARCH-053 decision 1).

## Alternatives considered
- **`transaction_timeout` on the API's role, a suspension and a wait.** Rejected under the maintainer's
  condition (decision 6).
- **The two steps without a bound (the earlier draft).** Rejected: nothing bounded how long a founding
  already past the session check took to commit (finding G9).
- **A local copy fed by an Access event.** Rejected (decision 7).
- **A transaction across Access and Identity.** Rejected by ADR-ARCH-031 §3.
- **Reading the profile at the request only.** Rejected: BP002-R14 applies it again at the effect.
- **A broader read of `access` for Identity.** Rejected: Identity needs one fact.

## References
Read on 2026-11-28:
- PostgreSQL 17: https://www.postgresql.org/docs/17/functions-admin.html;
  https://www.postgresql.org/docs/17/explicit-locking.html;
  https://www.postgresql.org/docs/17/transaction-iso.html;
  https://www.postgresql.org/docs/17/runtime-config-client.html;
  https://www.postgresql.org/docs/17/sql-alterrole.html; https://www.postgresql.org/docs/release/17.0/.
- Supabase: https://supabase.com/docs/guides/platform/upgrading;
  https://supabase.com/docs/guides/database/connecting-to-postgres;
  https://supabase.com/docs/guides/database/postgres/timeouts.

Repository:
- `apps/api/database/SaasBtp.Migrations/Scripts/0001__access__create_tables.sql` (`access.profiles`,
  `ux_profiles__user_id`); `0003__access__create_policies.sql`; `apps/api/tests/PostgresImage.cs`;
  ADR-ARCH-027; ADR-ARCH-028; ADR-ARCH-033 §4; ADR-ARCH-034 §4; ADR-ARCH-031 §3; ADR-ARCH-032 §1.
