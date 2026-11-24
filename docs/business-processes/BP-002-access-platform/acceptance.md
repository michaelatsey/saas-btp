---
id: BP-002-acceptance
status: validated
validated_by: michaelatsey
validated_on: 2026-11-24
applies: [BP-002, INV-ACCESS-01, INV-ACCESS-10, INV-ACCESS-11]
---

# BP-002 — Acceptance

Each scenario states an observable outcome, never the mechanism that produces it. Its identifier
is carried by the name of every test that covers it. What guarantees each scenario in production
is named in `contract.md`, written after the identity decisions.

In these scenarios, P1, P2 and P3 are phone numbers and E1 an e-mail address. "The recovery
delay" is the delay of rule BP002-R13, whatever its value. A "notice" is the message of rule
BP002-R15; every notice gives the platform operator's contact information and how to dispute the
event, and the notice of a pending recovery also gives the way to stop it without signing in.

## Creating an account and signing in

**BP002-AC-01 — Creating an account with a phone number**
- Given no account holds P1
- When Awa asks for a code for P1 and enters the code she received
- Then she is told that no account holds P1 and that a new account would be created
- When she confirms
- Then an account exists whose only contact is P1, confirmed, and she is signed in on her phone
- And she is offered a second contact, which she may skip
- And she is told that whoever holds P1 can sign in to her account, and that losing P1 would
  leave only the assisted recovery
- And she has no profile until she founds or joins a company

**BP002-AC-02 — Creating an account with an e-mail address**
- Given no account holds E1
- When Awa asks for a code for E1, enters it and confirms the creation
- Then an account exists whose only contact is E1, confirmed, and she is signed in
- And she is offered a second contact
- And she is not shown the notice about a phone-only account

**BP002-AC-03 — Adding a second contact when the account is created**
- Given Awa has just created her account with P1
- When she accepts the second contact offered, gives E1 and enters the code sent to E1
- Then her account holds P1 and E1, both confirmed
- And she is told to add only a contact that she alone controls

**BP002-AC-04 — Signing in again reaches the same account**
- Given Awa's account holds P1 and E1
- When she signs in with P1, and later with E1, on another phone
- Then both times she is signed in to the same account
- And no other account exists for her

**BP002-AC-05 — Declining to create an account**
- Given Awa's account holds P1, and she enters P2, which no account holds, by mistake
- When she enters the code sent to P2
- Then she is told that a new account would be created, and offered instead to sign in with
  another contact or, if she lost every contact, to ask for an assisted recovery
- When she declines
- Then no account holds P2 and nothing was created

**BP002-AC-06 — Asking for a code reveals nothing**
- Given no account holds P2, and an account holds P1
- When someone asks for a code for P1 and for P2, enters a wrong code for each, and lets a code
  expire for each
- Then every answer they see is the same for P1 as for P2

**BP002-AC-07 — Signing in needs a connection**
- Given Awa's phone has no connection
- When she asks to sign in
- Then she is told that signing in needs a connection
- And no code is sent

**BP002-AC-08 — The second contact is never required**
- Given Awa's account holds only P1, and she skipped the second contact
- When she signs in again later, and a second contact is suggested to her
- Then the suggestion carries the notice about a phone-only account
- And when she skips it again, she is signed in

**BP002-AC-09 — A new account meets the confirmed-contact level**
- Given Awa has just created her account with P1
- When she is signed in
- Then her account holds a confirmed contact, and she meets the confirmed-contact level
- And nothing in this process takes that level from her while she is signed in

**BP002-AC-10 — One phone number, however written**
- Given Awa's account holds P1
- When she signs in with P1 written with spaces, or with its country code
- Then she is signed in to the same account

**BP002-AC-11 — One e-mail address, whatever its letter case**
- Given Awa's account holds E1
- When she signs in with E1 written with other capital letters
- Then she is signed in to the same account

## Wrong code, expired code, lock

**BP002-AC-12 — A wrong code**
- Given a code was sent to P1
- When Awa enters a wrong code
- Then she is told the code is wrong and is not signed in
- When she then enters the right code
- Then she is signed in

**BP002-AC-13 — An expired code**
- Given a code was sent to P1 and its time has passed
- When Awa enters it
- Then she is told it has expired and is not signed in
- When she asks for a new code and enters it
- Then she is signed in

**BP002-AC-14 — A code works once**
- Given Awa signed in with a code sent to P1
- When the same code is entered again
- Then it is refused

**BP002-AC-15 — A new code replaces the former one**
- Given a code was sent to P1, then a second one
- When the first code is entered
- Then it is refused
- And the second one signs Awa in

**BP002-AC-16 — Repeated wrong codes lock the contact for a time**
- Given a code was sent to P1
- When wrong codes are entered until the limit is reached
- Then Awa is told when she may try again
- And during the lock, even the right code for P1 does not sign her in
- When the lock is over and she asks for a new code
- Then that code signs her in

**BP002-AC-17 — Too many codes asked for one contact**
- Given codes were asked for P1 until the limit on requests was reached
- When one more code is asked for P1
- Then no code is sent, and the person is told when they may ask again
- And the answer is the same whether or not an account holds P1

**BP002-AC-18 — A lock leaves the other contact usable**
- Given Awa's account holds P1 and E1, and P1 is locked
- When she signs in with E1
- Then she is signed in

**BP002-AC-19 — A lock answers the same without an account**
- Given no account holds P2
- When wrong codes are entered for P2 until the limit is reached
- Then the answers are the same as for a contact that an account holds

**BP002-AC-20 — Someone else's wrong codes lock a phone-only person out for a time**
- Given Awa's account holds only P1
- When someone else asks for codes for P1 and enters wrong ones until the limit is reached
- Then Awa cannot sign in until the lock is over
- And once it is over, she signs in with a new code
- And nothing in her account changed

**BP002-AC-21 — Repeated failed proofs of the stronger factor lock it**
- Given Awa's account has a stronger factor
- When it fails to be proved until the limit is reached
- Then Awa is told when she may try again
- And during the lock, nothing that requires stronger authentication is accepted for her

## Contacts

**BP002-AC-22 — Adding a second contact later**
- Given Awa's account holds only P1, and she is signed in
- When she adds E1 and enters the code sent to E1
- Then her account holds P1 and E1
- And P1 receives a notice that E1 was added, when, how to dispute it, and the operator's
  contact information

**BP002-AC-23 — Adding a contact requires being signed in**
- Given nobody is signed in
- When a request to add E1 to Awa's account is made
- Then it is refused and Awa's account is unchanged

**BP002-AC-24 — A contact of another account cannot be added**
- Given Koffi's account holds E1, and Awa is signed in to her own account
- When she asks to add E1
- Then, before she enters the code sent to E1, nothing tells her that E1 belongs to an account
- When she enters that code
- Then she is told that E1 belongs to another account, and her account is unchanged
- And Koffi's account is unchanged

**BP002-AC-25 — Changing a phone number with access to the former one**
- Given Awa's account holds P1 and she still receives codes on it
- When she changes P1 for P2, entering the code sent to P2 and the code sent to P1
- Then her account holds P2 and no longer P1
- And P1 receives a notice of the change, how to dispute it, and the operator's contact
  information

**BP002-AC-26 — Removing a contact the person still controls**
- Given Awa's account holds P1 and E1
- When she removes E1, entering a code sent to E1
- Then her account holds only P1
- And P1 and E1 each receive a notice of the removal

**BP002-AC-27 — The last contact cannot be removed**
- Given Awa's account holds only P1
- When she asks to remove P1
- Then it is refused, she is offered to change it instead, and her account still holds P1

**BP002-AC-28 — A contact that cannot be proved is not removed at once**
- Given Awa's account holds P1 and E1, has no stronger factor, and she no longer receives codes
  on P1
- When she asks to remove P1
- Then it is not removed at once
- And she is offered the self-service recovery

## Stronger factor

**BP002-AC-29 — Registering a stronger factor requires every contact**
- Given Awa's account holds P1 and E1, and she is signed in with P1
- When she registers a stronger factor and enters a code for P1 only
- Then it is refused and her account has no stronger factor
- When she enters a code for P1 and a code for E1
- Then her account has a stronger factor
- And P1 and E1 each receive a notice of it

**BP002-AC-30 — With a stronger factor, adding a contact requires it**
- Given Awa's account holds P1 and has a stronger factor
- When she adds E1 and enters the code sent to E1, without proving the stronger factor
- Then it is refused and her account still holds only P1
- When she does the same and proves the stronger factor
- Then her account holds P1 and E1

**BP002-AC-31 — With a stronger factor, a lost contact is removed at once**
- Given Awa's account holds P1 and E1 and has a stronger factor, and she has lost P1
- When, signed in with E1, she removes P1 and proves the stronger factor
- Then her account holds only E1, at once
- And P1 and E1 each receive a notice of the removal

**BP002-AC-32 — Removing the stronger factor**
- Given Awa's account has a stronger factor
- When she removes it and proves it
- Then her account has no stronger factor
- And every contact of her account receives a notice of it

**BP002-AC-33 — Removing a lost stronger factor with every contact**
- Given Awa's account holds P1 and E1 and has a stronger factor that she lost
- When she removes it, entering a code for P1 and a code for E1
- Then her account has no stronger factor, at once
- And P1 and E1 each receive a notice of it
- When she enters a code for P1 only
- Then the removal is refused

**BP002-AC-34 — Replacing the stronger factor**
- Given Awa's account has a stronger factor
- When she replaces it with a new one, proving the current one
- Then her account holds only the new stronger factor
- And every contact of her account receives a notice of it
- When she does the same without proving the current one, and without a code for every contact
- Then it is refused and the current one stays

**BP002-AC-35 — A proof of the stronger factor serves one operation**
- Given Awa proved her stronger factor to add E1
- When she then asks to remove P1 without proving P1
- Then she is asked to prove her stronger factor again

## Self-service recovery

**BP002-AC-36 — A lost phone, with an e-mail address kept**
- Given Awa's account holds P1 and E1, and she lost her phone
- When she signs in with E1 on another phone and ends the sessions of every other device
- Then the lost phone is no longer signed in, and nothing it sends under its ended session is
  accepted
- When she asks for a recovery that removes P1 and adds P3, entering the code sent to P3
- Then P1 and E1 each receive a notice of the pending recovery, with the way to stop it
- When the recovery delay has passed and nobody stopped it
- Then her account holds E1 and P3, and no longer P1
- And P1 and E1 each receive a notice that it took effect

**BP002-AC-37 — A contact being recovered cannot sign in**
- Given a recovery of Awa's account that removes P1 is pending
- When someone enters a correct code for P1 to sign in
- Then they are not signed in
- And they are offered to stop the recovery

**BP002-AC-38 — A recycled number**
- Given Awa's account holds P1 and E1, and P1 was given to someone else, who signs in to her
  account with it
- When Awa, signed in with E1, asks for a recovery that removes P1
- Then from that moment the holder of P1 is no longer signed in to her account and cannot sign
  in to it
- When the recovery delay has passed and nobody stopped it
- Then her account no longer holds P1
- And P1 can then create a new account of its own
- Given instead that the holder of P1 stops the recovery from the notice, proving P1
- Then the recovery is stopped, Awa's account still holds P1, and its holder can sign in to it
  again

**BP002-AC-39 — A number changed without access to the former one**
- Given Awa's account holds P1 and E1, and she changed her number to P2 without keeping P1
- When, signed in with E1, she asks for a recovery that removes P1 and adds P2, entering the code
  sent to P2
- And the recovery delay passes without a stop
- Then her account holds E1 and P2

**BP002-AC-40 — A compromised SIM card, the person first**
- Given Awa's account holds P1 and E1, and someone else now receives the codes of P1
- When Awa, signed in with E1, asks for a recovery that removes P1
- Then the other person's sessions on her account end, and they can no longer sign in with P1
- When, still receiving the codes of P1, they stop the recovery from the notice by proving P1
- Then the recovery is stopped, and her account still holds P1 and E1
- When the carrier has blocked the SIM card of P1, and Awa asks for the recovery again
- And the delay passes without a stop
- Then her account no longer holds P1
- Given instead that the carrier restored P1 to Awa
- Then Awa keeps P1 and needs no recovery

**BP002-AC-41 — A compromised SIM card, the other person first**
- Given Awa's account holds P1 and E1, and someone else now receives the codes of P1
- When that person, signed in with P1, asks for a recovery that removes E1
- Then E1 receives a notice of it, with the way to stop it
- And Awa cannot sign in with E1, and is offered to stop the recovery
- When Awa stops it from the notice, entering a code sent to E1, without signing in
- Then the recovery is stopped, her account still holds P1 and E1, and she can sign in with E1
- And she may then recover P1 through E1, as in BP002-AC-40
- And if the same request is made again, she can stop it again in the same way

**BP002-AC-42 — A thief holding a lost phone, until the carrier blocks its SIM card**
- Given Awa's account holds P1 and E1, and a thief holds her phone and receives the codes of P1
- When Awa, signed in with E1, asks for a recovery that removes P1, and the thief stops it with
  a code sent to P1
- Then the recovery is stopped
- When the carrier has blocked the SIM card of P1, and Awa asks for the recovery again
- Then nobody can stop it with P1, and when the delay passes her account no longer holds P1

**BP002-AC-43 — A recovery that removes the stronger factor can be stopped by a kept contact**
- Given Awa's account holds P1 and E1 and has a stronger factor, and someone else holds P1
- When that person, signed in with P1, asks for a recovery that removes only the stronger factor
- Then E1 receives a notice of it, with the way to stop it
- When Awa stops it from the notice, entering a code sent to E1
- Then the recovery is stopped and her account keeps its stronger factor

**BP002-AC-44 — A recovery that adds a contact requires the stronger factor kept**
- Given Awa's account holds P1 and E1 and has a stronger factor
- When someone signed in with P1 asks for a recovery that removes E1 and adds P2, without
  proving the stronger factor
- Then it is refused and nothing changes

**BP002-AC-45 — Cancelling one's own recovery**
- Given Awa asked for a recovery that removes P1, and it is pending
- When she cancels it, signed in with E1
- Then it is stopped, her account still holds P1 and E1, and P1 can sign in again
- And P1 and E1 each receive a notice that it was stopped

**BP002-AC-46 — One recovery at a time, and no change while it is pending**
- Given a recovery of Awa's account is pending
- When a second recovery is asked for her account, signed in
- Then it is refused, and the person is told how to stop the pending one
- When Awa asks to add a contact or to register a stronger factor
- Then it is refused while the recovery is pending

**BP002-AC-47 — A recovery requires being signed in with a contact it keeps**
- Given Awa's account holds P1 and E1
- When a recovery that removes E1 is asked by someone not signed in, or by someone signed in
  with E1
- Then it is refused and nothing changes

**BP002-AC-48 — No e-mail address: two phone numbers**
- Given Awa has no e-mail address and her account holds P1 and P2
- When she loses P1 and, signed in with P2, asks for a recovery that removes P1
- And the delay passes without a stop
- Then her account holds only P2

**BP002-AC-49 — A new number that already holds an empty account**
- Given Awa's account holds P1 and E1, and she changed her number to P2 and created by mistake an
  account with P2 that takes part in no company
- When, signed in with E1, she asks for a recovery that removes P1 and adds P2, entering the code
  sent to P2
- And the delay passes without a stop
- Then her account holds E1 and P2, and the account created with P2 no longer exists
- And P2 receives a notice that the account created with it has ended
- Given instead that the account created with P2 founded or joined a company, even if that
  membership has since ended
- Then the recovery is refused with P2, and both accounts are unchanged

**BP002-AC-50 — A new contact taken by a company during the delay**
- Given Awa, signed in with E1, asked for a recovery that removes P1 and adds P2, and no account
  held P2 at the request
- When, during the delay, an account is created with P2 and founds a company
- Then, when the delay passes, the recovery does not take effect, and Awa is told
- And her account still holds P1 and E1, and the other account is unchanged

**BP002-AC-51 — A lock does not prevent stopping a recovery**
- Given someone holding P1 asked for a recovery of Awa's account that removes E1
- And they keep entering wrong codes for E1, so that E1 is locked for signing in
- When Awa stops the recovery from the notice she received at E1
- Then the recovery is stopped, and her account still holds P1 and E1

## Assisted recovery

**BP002-AC-52 — Every contact lost**
- Given Awa's account holds only P1, she owns "Kouassi BTP", and she lost P1 for good
- When she asks the operator for an assisted recovery naming P1, with P2, and enters the code
  sent to P2
- Then P1 receives a notice of it, with the way to stop it without signing in
- And from that moment nobody is signed in to her account
- When the recovery delay has passed and nobody stopped it
- Then she signs in with P2 to her own account, and still owns "Kouassi BTP" with every
  membership and access she had
- And her account no longer holds P1
- And P1 receives a notice that the recovery took effect
- And the operator holds a record of who asked, through P2, when, which operator took it, what
  was replaced, and the outcome

**BP002-AC-53 — A former contact is refused during the delay**
- Given an assisted recovery of Awa's account is pending
- When someone enters a correct code for P1 to sign in
- Then they are not signed in
- And they are offered to stop the recovery

**BP002-AC-54 — An assisted recovery stopped from the notice**
- Given someone asked for an assisted recovery of Awa's account, which holds P1 and E1
- When Awa stops it from the notice, entering a code sent to E1, without signing in
- Then the recovery is stopped, her account still holds P1 and E1, and she can sign in with both
- And the requester's new contact is not added to her account

**BP002-AC-55 — An assisted recovery stopped with the stronger factor**
- Given someone asked for an assisted recovery of Awa's account, which has a stronger factor
- When Awa stops it from the notice by proving her stronger factor
- Then the recovery is stopped and her account is unchanged

**BP002-AC-56 — An assisted request reveals nothing**
- Given an account holds P1, and no account holds P3
- When someone asks for an assisted recovery naming P1, and another naming P3, each with a new
  contact they confirm
- Then the answers they receive are the same

**BP002-AC-57 — A phone-only director who changed number and signed in with the new one**
- Given Awa's account holds only P1, she owns "Kouassi BTP", and she changed her number to P2
  without keeping P1
- When she signs in with P2 and enters the code sent to it
- Then she is warned that a new account would be created, and offered the assisted recovery
- Given she created the new account anyway, and it takes part in no company
- When she asks for an assisted recovery naming P1, with P2
- And the delay passes without a stop
- Then signing in with P2 reaches her own account, and she still owns "Kouassi BTP"
- And the account she created with P2 no longer exists

**BP002-AC-58 — A phone-only account whose number was recycled**
- Given Awa's account holds only P1, and P1 was given to someone else
- When she asks for an assisted recovery naming P1, with P2
- Then the new holder of P1 receives the notice, and cannot sign in to her account during the
  delay
- When the delay passes and the new holder did not stop it
- Then Awa signs in with P2 to her own account
- Given instead that the new holder stops it from the notice
- Then the recovery is stopped, and Awa's account cannot be recovered through this path

**BP002-AC-59 — A new contact of an account with a profile is refused**
- Given Koffi's account holds P2 and he is, or once was, a member of a company
- When someone asks for a recovery, self-service or assisted, with P2 as the new contact, and
  enters the code sent to P2
- Then it is refused, and Koffi's account is unchanged

## Notices and disputes

**BP002-AC-60 — Disputing an event that took effect**
- Given E1 received a notice that P2 was added to Awa's account, and Awa did not add it
- When Awa reports it to the operator through the contact information of the notice
- Then the dispute is recorded
- And she is directed to remove P2, or to recover her account
- And her account is not changed by the dispute alone

## Sessions and offline capture

**BP002-AC-61 — Ending the sessions of a lost phone**
- Given Awa is signed in on two phones and lost one
- When, on the other, she ends the sessions of every other device
- Then the lost phone is no longer signed in, and nothing it sends under its ended session is
  accepted
- And whoever holds it can sign in again only with a correct code for one of her contacts

**BP002-AC-62 — A session that expires offline**
- Given Awa is signed in on her phone on a site with no network
- When her session expires
- Then she keeps capturing on her phone
- When the connection returns
- Then nothing she captured is sent until she signs in again
- When she signs in again on that phone
- Then what she captured is sent, under her

**BP002-AC-63 — Another person on the same phone**
- Given Awa captured while offline on a shared phone, after her session expired, and nothing is
  sent yet
- When Koffi signs in on that phone and the connection returns
- Then nothing Awa captured is sent, and Koffi does not see it
- When Awa signs in again on that phone
- Then what she captured is sent, under her

**BP002-AC-64 — Signing out with unsent captures**
- Given Awa has captures on her phone that are not sent yet
- When she signs out
- Then she is told they stay on the phone and will be sent once she signs in again
- And they are not sent under anyone else

## Coverage

| Scope situation or decision | Scenarios |
|---|---|
| Creating an account and signing in | BP002-AC-01 to BP002-AC-11 |
| A wrong code, an expired code, a temporary lock | BP002-AC-12 to BP002-AC-21 |
| Adding a second contact, changing a contact, removing one | BP002-AC-03, BP002-AC-22 to BP002-AC-28 |
| A lost phone | BP002-AC-36, BP002-AC-42, BP002-AC-61 |
| A recycled number | BP002-AC-38, BP002-AC-58 |
| A number changed without access to the former one | BP002-AC-39, BP002-AC-49, BP002-AC-50, BP002-AC-57 |
| A compromised SIM card | BP002-AC-40, BP002-AC-41, BP002-AC-51 |
| No e-mail address | BP002-AC-48, BP002-AC-57, BP002-AC-58 |
| Every contact lost (assisted recovery) | BP002-AC-52 to BP002-AC-59 |
| Stopping a recovery | BP002-AC-41 to BP002-AC-43, BP002-AC-45, BP002-AC-51, BP002-AC-54, BP002-AC-55 |
| A stronger factor, and the operations that require it | BP002-AC-21, BP002-AC-29 to BP002-AC-35, BP002-AC-43, BP002-AC-44, BP002-AC-55 |
| An expired session while offline | BP002-AC-62 to BP002-AC-64 |
| Notices, and how to dispute | BP002-AC-22, BP002-AC-25, BP002-AC-36, BP002-AC-60 |
| BP002-D1 | BP002-AC-01, BP002-AC-02, BP002-AC-09 |
| BP002-D2 | BP002-AC-01, BP002-AC-03, BP002-AC-08 |
| BP002-D3 | BP002-AC-52 to BP002-AC-59 |
| BP002-D4 | BP002-AC-62 to BP002-AC-64 |
| BP002-D5 | BP002-AC-09, BP002-AC-30, BP002-AC-31, BP002-AC-35 |
