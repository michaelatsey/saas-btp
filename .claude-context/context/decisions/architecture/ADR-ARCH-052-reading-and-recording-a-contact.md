# ADR-ARCH-052 — Reading and recording a contact: phone numbers parsed with libphonenumber, Côte d'Ivoire by default, recorded in E.164; e-mail addresses compared on Identity's normalized form

Status: accepted
Date: 2026-11-27
Refs: issue #248 (Scope item 13); BP-002 `process.md` (TSK-1, BP002-R5), `acceptance.md`
(BP002-AC-10, AC-11); `docs/domain/access.md` (INV-ACCESS-10); ADR-ARCH-042; ADR-ARCH-045;
ADR-ARCH-047; ADR-ARCH-055.

## Context
BP002-R5 is decided: "Two writings of the same phone number are one contact; a phone number is
recorded with its country code. An e-mail address is compared without regard to letter case." #248's
item 13 asks how it is applied: reading a number entered with or without its country code, and the
form a number is recorded in. TSK-1's error: "Not a phone number nor an e-mail address: told so."
INV-ACCESS-10: a contact belongs to at most one account.

What the sources state, read on 2026-11-27:
- ITU, national numbering plan notification of Côte d'Ivoire from ARTCI
  (https://www.itu.int/dms_pub/itu-t/oth/02/02/T02020000310006PDFE.pdf; index page "Updated :
  2021-01-14"): "modification of the national numbering plan of Côte d'Ivoire, from eight (8) to ten
  (10) digits, effective 31 January 2021"; "all calls to Côte d'Ivoire mobile numbers must include the
  digit "0" after the ITU-T E.164 country code "225", as it is part of the national (significant)
  number (N(S)N)"; "+225 01 XX XX XXXX, +225 05 XX XX XXXX and +225 07 XX XX XXXX for MOOV CI, MTN CI
  and ORANGE CI respectively"; "X = 0 for mobile telephone services ... X = 2 for fixed telephone
  services"; "00" is the "International access prefix".
- Google libphonenumber's metadata
  (https://raw.githubusercontent.com/google/libphonenumber/master/resources/PhoneNumberMetadata.xml;
  release v9.0.44, 2026-11-20): `<territory id="CI" countryCode="225" internationalPrefix="00">`, no
  `nationalPrefix`; mobile `0[157]\d{8}`, 10 digits.
- `libphonenumber-csharp` on NuGet (https://www.nuget.org/packages/libphonenumber-csharp): 9.0.44,
  published 2026-11-22, targeting net10.0, net8.0 and netstandard2.0.
- ASP.NET Core Identity's `UpperInvariantLookupNormalizer` normalizes an e-mail by Unicode
  normalization and upper-casing ([SRC], `dotnet/aspnetcore` `release/10.0` 500a5c64).

## Decision
1. **Reading a phone number.** The text entered is parsed with `libphonenumber-csharp`, Côte d'Ivoire
   (`CI`) as the default region:
   - "+225 07 47 31 84 32", "00225 0747318432" and "07 47 31 84 32" are one number; spaces, dots,
     dashes and parentheses are ignored;
   - a number of another country is read from its "+" or "00" prefix;
   - the number is accepted only if libphonenumber finds it valid, of type mobile (a code cannot be
     sent to a fixed line, ADR-ARCH-045), and its country is on the destination list (ADR-ARCH-055 decision
     2: `+225` in production, `+225` and `+33` in staging, a setting). Otherwise the person is told it is not a phone number the platform can use
     (TSK-1's error);
   - an 8-digit number of the former plan is not a valid number; it is refused like any other, and the
     person is told to enter the 10 digits.
2. **Recording it.** In E.164: "+" then the country code and the national significant number, for Côte
   d'Ivoire "+225" and 10 digits (for example `+2250747318432`). That form is the contact's value; the
   uniqueness of INV-ACCESS-10 is on it. It is shown in the national format libphonenumber gives
   ("07 47 31 8432").
3. **Reading and recording an e-mail address.** Surrounding white space is removed; the address is
   recorded as entered; it is compared, and unique (INV-ACCESS-10), on ASP.NET Core Identity's
   `ILookupNormalizer` form (`UpperInvariantLookupNormalizer`). Its syntax is checked only enough to tell
   it from a phone number; proof of control is the code (BP002-D1).
4. **One rule everywhere.** Every input of a contact (TSK-1, TSK-3, TSK-6, TSK-9, the stop page, the
   operator console) goes through the same reading, before any lookup and before any limit is counted
   (ADR-ARCH-047, ADR-ARCH-055), so that two writings never count as two contacts.
5. **Verification.**

   | Requirement | Verification |
   |---|---|
   | One phone number, however written (BP002-AC-10) | BP002-AC-10, with the forms of decision 1 |
   | One e-mail address, whatever its letter case (BP002-AC-11) | BP002-AC-11 |
   | Not a phone number nor an e-mail address (TSK-1) | Unit tests of the reading: a fixed line, an 8-digit number, a country off the list, text |

## Consequences
- libphonenumber's metadata is updated with the package; a change of the national plan reaches the
  platform through an update of `libphonenumber-csharp`.
- Opening a country is a change of the destination list and of Twilio's Geo Permissions, with its
  delivery cost read again (ADR-ARCH-045, ADR-ARCH-055).
- Two e-mail addresses that differ only by letter case are one contact; addresses that differ in any
  other way (a dot, a "+" part) are distinct.

## Alternatives considered
- **A regular expression for Ivorian numbers.** Rejected: it would duplicate a maintained plan and fail
  on other countries.
- **Recording the number as entered.** Rejected by BP002-R5.
- **Lower-casing e-mail addresses by hand.** Rejected: Identity's normalizer exists, with Unicode
  normalization.

## References
Read on 2026-11-27:
- https://www.itu.int/dms_pub/itu-t/oth/02/02/T02020000310006PDFE.pdf;
  https://www.itu.int/oth/T0202000031/en.
- https://raw.githubusercontent.com/google/libphonenumber/master/resources/PhoneNumberMetadata.xml;
  https://www.nuget.org/packages/libphonenumber-csharp; https://github.com/twcclegg/libphonenumber-csharp.
- `dotnet/aspnetcore` `release/10.0` 500a5c64, `src/Identity/Extensions.Core/src/UpperInvariantLookupNormalizer.cs`.
