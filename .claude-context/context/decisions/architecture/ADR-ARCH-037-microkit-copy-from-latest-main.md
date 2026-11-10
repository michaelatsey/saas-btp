# ADR-ARCH-037 — A MicroKit library is copied from the latest commit of MicroKit's `main` when a work needs it, accepted by the merge of its copy; no declaration of soundness

Status: accepted
Date: 2026-11-10
Refs: issue #195; ADR-ARCH-032; ADR-ARCH-033; ADR-ARCH-035; ADR-ORG-004.

## Context
ADR-ARCH-035 §2 copies a MicroKit library into `libraries/` when both hold: a piece of work of
this repository needs it, and the maintainer has declared it sound; the pull request that copies
it names the MicroKit commit it is copied from. Its Consequences add that BP-001's implementation
waits for MicroKit.Result and MicroKit.Persistence to be declared sound, or copies them as they
are and fixes them here. Its Alternatives reject copying a library as it is, whatever its state,
because defects known in the MicroKit repository would enter this repository and be fixed twice.

No declaration of soundness is recorded in the repository or in an issue. The dry run of
`/deliver-bp BP-001` (proof of #136) relied on one made only in a Claude Code session, which
neither a reviewer nor an agent can find afterwards. The maintainer has decided to drop the
condition. BP-001's implementation (#138) is the first work to copy libraries.

`michaelatsey/microkit` is public, its default branch `main` (read with
`gh api repos/michaelatsey/microkit` on 2026-11-10).

## Decision
1. **When and from where a library is copied.** A library is copied when a piece of work of this
   repository needs it, from the latest commit of `main` of `michaelatsey/microkit` at the time
   of the copy: the commit at the head of `main` when the copy is taken. The pull request that
   copies it names that commit by its full SHA and the date and time it was read as the head of
   `main`, so that a reviewer can confirm it was the newest commit of `main` at that moment. A
   commit added to MicroKit's `main` while that pull request is open does not call for a new
   copy. A library already in `libraries/` is not copied again when a piece of work needs it: it
   evolves here, and a change made in the MicroKit repository reaches it only by the new,
   deliberate copy ADR-ARCH-035 §1 allows.
2. **How a copy is accepted.** The maintainer's merge of that pull request is the acceptance of
   the copied code. No declaration of soundness is made or required, before or outside that pull
   request.
3. **What this supersedes in ADR-ARCH-035.** The condition "and the maintainer has declared it
   sound" of §2, and the Consequence that BP-001's implementation waits for MicroKit.Result and
   MicroKit.Persistence to be declared sound. It also withdraws the rejection, among ADR-ARCH-035's
   Alternatives, of copying a library as it is, whatever its state, for the reason given under
   Alternatives considered below. Everything else in ADR-ARCH-035 stays in force,
   among it the rest of §2 (the pull request names the commit and brings the MicroKit libraries
   the copied one depends on; the libraries expected and those not copied), §1 (no
   synchronization in either direction) and its other Consequences. ADR-ARCH-035 stays accepted
   and unedited, as ADR-ARCH-032 did when ADR-ARCH-033 replaced parts of it.

## Consequences
- A defect corrected in the MicroKit repository before the copy arrives with the latest commit of
  `main`; one still present is fixed here (ADR-ARCH-035, first Consequence: "or here after it").
  Tests and review do not find every defect. One that the copied tests or the review reveal while
  a library is being copied is fixed in the copying pull request, so that its tests pass on this
  repository's stack (ADR-ARCH-035, Consequences), and the library's `README.md` lists the fix as
  a change made here (ADR-ARCH-035 §4, §6). One found after that pull request is merged is fixed
  here by its own pull request, with its test.
- The copying pull request is the only place a copy is accepted, so a reviewer or an agent checks
  the condition from the repository and GitHub alone: the work that needs the library, the SHA
  and the time the pull request names, checked against the history of MicroKit's `main`, and the
  merge.
- BP-001's implementation (#138) no longer waits for a declaration.
- Libraries copied at different times may come from different MicroKit commits, and a library
  copied earlier may carry changes made here since. A later copy may therefore expect a form of an
  already copied library that the copy here does not have. This is a cost of the decision; how
  such a copy is reconciled is not decided here.

## Alternatives considered
- Keeping the declaration of soundness (ADR-ARCH-035 §2): a declaration recorded nowhere cannot be
  checked, and a work waits on an act no file or issue shows.
- Recording the declaration in an issue: a second acceptance, separate from the review of the
  code it vouches for, while the copying pull request is already reviewed with its tests passing
  on this repository's stack.
- Copying from a commit the maintainer chooses: a declaration of soundness under another name.
- Keeping `a930a97`, the commit ADR-ARCH-035 read: it was read while the maintainer stated that
  MicroKit.Result and MicroKit.Persistence were being corrected (ADR-ARCH-035, Context), and fixing
  a commit ahead of the work that needs it is again a declaration under another name.
- Copying a library as it is, whatever its state: rejected by ADR-ARCH-035 because defects known
  in the MicroKit repository would enter this repository and be fixed twice; that rejection is
  withdrawn (decision 3), because its reason no longer holds. The copy is not synchronized
  (ADR-ARCH-035 §1), so a defect fixed here is fixed only here, and whether the MicroKit
  repository fixes it too is that repository's work, not this one's. The latest commit of `main`
  carries every correction already made there. A defect that remains is handled in the copying
  pull request or after it, as the Consequences state.
