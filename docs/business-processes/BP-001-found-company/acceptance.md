---
id: BP-001-acceptance
status: validated
validated_by: michaelatsey
validated_on: 2026-10-21
applies: [BP-001, INV-ACCESS-01, INV-ACCESS-02, INV-ACCESS-03, INV-ACCESS-05, INV-ACCESS-06, INV-ACCESS-07, INV-ACCESS-08, INV-ACCESS-09]
---

# BP-001 — Acceptance

Each scenario states an observable outcome, never the mechanism that produces it. Its identifier is
carried by the name of every test that covers it. What guarantees each scenario in production is
named in `contract.md`, written after #125.

## Founding

**BP001-AC-01 — A director founds their company**
- Given a signed-in person at the required identity level, with no company
- When they found "Kouassi BTP" with a first workspace "Siège Abidjan"
- Then the company "Kouassi BTP" exists, Declared
- And it holds exactly one workspace, "Siège Abidjan"
- And the person is its owner and its only member
- And the person can enter "Siège Abidjan"

**BP001-AC-02 — The first workspace is named after the company by default**
- Given a person describing their company "Kouassi BTP"
- When they leave the first workspace name as offered
- Then the founded company's first workspace is named "Kouassi BTP"

**BP001-AC-03 — Founding a second company**
- Given a person who already owns "Kouassi BTP"
- When they found "Kouassi Maintenance"
- Then they own both companies, each with its own first workspace
- And what they can do in "Kouassi BTP" is unchanged

**BP001-AC-04 — A member of another company founds their own**
- Given a person who belongs to "Kouassi BTP" without owning it
- When they found "Diallo Construction"
- Then they own "Diallo Construction"
- And their place in "Kouassi BTP" is unchanged

**BP001-AC-05 — The founder can enter the first workspace only**
- Given a person who has just founded "Kouassi BTP"
- When they list the workspaces they can enter
- Then the list holds exactly "Siège Abidjan" for "Kouassi BTP"

**BP001-AC-06 — The founder is the signed-in person**
- Given a signed-in person founding "Kouassi BTP"
- When the founding request names another person as founder
- Then the signed-in person is the owner, the member and the authorized person
- And the named person has no relation to "Kouassi BTP"

## Refusals

**BP001-AC-07 — A company name is required**
- Given a person describing their company with an empty company name, or one made only of spaces
- When they try to found it
- Then they are told the company name is missing
- And no company exists for them
- And what they had entered is still there

**BP001-AC-08 — A workspace name is required**
- Given a person who clears the offered first workspace name
- When they try to found the company
- Then they are told the workspace name is missing
- And no company exists for them

**BP001-AC-09 — Identity level not met**
- Given a signed-in person whose contact is not confirmed
- When they try to found a company
- Then they are told their contact must be confirmed first
- And no company exists for them
- And their description is kept

**BP001-AC-10 — Identity level lost while the request waited**
- Given a request described while the founder met the identity level, then kept without connection
- And the founder no longer meets the level when the connection returns
- When the request is submitted
- Then it is refused, no company exists, and the request is kept
- When the founder raises their level and submits it again
- Then the company is founded exactly once

**BP001-AC-11 — A person who is not signed in cannot found**
- Given nobody is signed in
- When a founding request is sent
- Then it is refused and no company exists

**BP001-AC-12 — Abandoning a request**
- Given a person describing their company
- When they abandon it
- Then no company exists from it and the description is gone

## Repetition and interruption

**BP001-AC-22 — A submitted description cannot change**
- Given a founding request for "Kouassi BTP" submitted without an answer
- When the founder tries to change the company name
- Then the change is not accepted while the request is pending
- And to found under another name, they abandon the request and start a new one

**BP001-AC-13 — Pressing "Found" twice**
- Given a person who submits their founding request
- When the same request is submitted a second time
- Then exactly one company exists
- And both submissions answer with that company

**BP001-AC-14 — Two identical submissions at the same moment**
- Given one founding request
- When it is submitted twice at the same moment
- Then exactly one company exists
- And both submissions answer with that company

**BP001-AC-15 — The answer is lost after the company was founded**
- Given a founding request that was founded, but whose answer never reached the founder
- When the founder submits the same request again
- Then they receive the company that was founded
- And no second company exists

**BP001-AC-16 — No connection when founding**
- Given a person who has described their company while offline
- When they ask to found it
- Then they are told the founding will happen once connected
- When they close the application and open it again, still offline
- Then their description is still there
- When the connection returns
- Then the company is founded exactly once

**BP001-AC-17 — A failure leaves nothing behind**
- Given a founding that fails before it completes
- Then no company, workspace, ownership, membership or access exists from it
- And if the person had no profile before, they still have none
- And their account is unaffected
- And submitting the same request again founds the company

**BP001-AC-18 — A kept request stays with its author**
- Given a request kept offline on a shared phone by a first person
- When a second person signs in on that phone and the connection returns
- Then the request is not submitted and the second person does not see it
- When the first person signs in again
- Then the request is submitted and the first person is the founder

## Isolation

**BP001-AC-19 — Two founders declare the same name**
- Given "Kouassi BTP" founded by one person
- When another person founds "Kouassi BTP"
- Then two separate companies exist
- And neither person sees the other's company

**BP001-AC-20 — A founded company is invisible to others**
- Given "Kouassi BTP" just founded by one person
- Then another signed-in person cannot see it, its workspace, or who owns it, even knowing its
  reference

**BP001-AC-21 — A request cannot claim someone else's company**
- Given "Kouassi BTP" founded by one person
- When another person submits a founding request whose company reference is that of "Kouassi BTP"
- Then it is refused, telling them only that the reference is in use
- And nothing about "Kouassi BTP" is revealed to them
- And "Kouassi BTP" is unchanged
- And when their request is submitted again with a new reference, their own company is founded
- And the same holds for a person who belongs to "Kouassi BTP" without owning it
