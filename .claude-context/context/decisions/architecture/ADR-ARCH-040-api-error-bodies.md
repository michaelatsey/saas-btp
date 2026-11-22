# ADR-ARCH-040 — The API's error bodies: RFC 9457 problem details returned by the endpoint, only for the answers that carry a detail

Status: accepted
Date: 2026-11-22
Refs: issue #220; parent #209 (BP-001); BP-001 `contract.md` §2, §9; ADR-ARCH-018; ADR-ARCH-032;
ADR-ARCH-034; ADR-ARCH-038.

## Context
BP-001's contract fixes what each answer of the founding reveals, and leaves open how it is
written (`contract.md` §9: "The format of the API's error bodies: no decision sets it"):

- 422 names the field at fault, `organizationName`, `workspaceName` or `organizationId`;
- 403 says only that the contact must be confirmed;
- 400, 401 and 500 carry no detail, and a 500 never names a constraint or a row
  (ADR-ARCH-032 §5).

The founding endpoint (#220) is the first to answer 422 and 403, so it needs a body for both.
The 401 is already produced by the host with a bare `WWW-Authenticate: Bearer` challenge and no
body (ADR-ARCH-038 decision 4, #217).

What ASP.NET Core 10 offers, read in the framework on 2026-11-22:

- `TypedResults.Problem` writes a `ProblemDetails` as `application/problem+json`, filling `type`
  and, when absent, `title` with the defaults of the status. A `HttpValidationProblemDetails`
  adds `errors`, a map from a field to its messages.
- `AddProblemDetails` registers a service that writes problem details for the whole host.
  Combined with `UseStatusCodePages` it gives a body to every empty error answer, and with
  `UseExceptionHandler` to every unhandled exception.

The maintainer decided the format on 2026-11-22, in #220.

## Decision
1. **Which answers carry a body.** Only an answer that carries a detail: today the 422 and the
   403 of `contract.md` §2. 400, 401 and 500 carry no body.
2. **The format.** That body is RFC 9457 problem details, media type
   `application/problem+json`, with the `type` and `status` of the answer.
3. **Returned by the endpoint.** The endpoint that answers builds the problem details and
   returns it explicitly. The host registers no `AddProblemDetails` with status code pages or an
   exception handler, nor anything else that would give a body to an answer this decision leaves
   without one. The 401 keeps the bare challenge of ADR-ARCH-038 decision 4.
4. **422.** Validation problem details: `errors` names the fields at fault and nothing else.
   Each field has one fixed message, the same whatever its fault, so that a missing name and a
   name too long give the same body. The message never says "missing" or "too long", and never
   repeats the value sent. The founding's three messages are:
   - `organizationId`: "The reference must be a UUIDv7.";
   - `organizationName`: "The company name must hold 1 to 200 characters.";
   - `workspaceName`: "The first workspace name must hold 1 to 200 characters.".

   As the founding writes it:

   ```json
   {"type":"https://tools.ietf.org/html/rfc4918#section-11.2","title":"One or more validation errors occurred.","status":422,"errors":{"organizationName":["The company name must hold 1 to 200 characters."]}}
   ```
5. **403.** Problem details whose `title` says only that the contact must be confirmed: "The
   contact must be confirmed.". It has no `detail` and no extension.

   ```json
   {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.4","title":"The contact must be confirmed.","status":403}
   ```
6. **The contract.** These bodies appear in the committed OpenAPI document (ADR-ARCH-018): the
   operation declares its 422 as validation problem details and its 403 as problem details.

## Consequences
- `contract.md` §9 cites this ADR for the format; the contract still fixes what each answer
  reveals.
- The host tests of #220 prove that 400, 401 and 500 have no body, and the bodies of 422 and 403.
- A new answer that carries a detail uses this format, returned by its endpoint. One that needs
  a field this ADR does not name, or a body on 400, 401 or 500, needs a new ADR.
- An answer that carries no detail is the endpoint's responsibility: nothing in the host fills
  it. An exception that escapes an endpoint is answered by the framework, so the founding's
  endpoint catches every failure itself and answers 500 with no body (#220).
- A client reads the field at fault from `errors` and tells the founder which name to correct;
  the reason, missing or too long, is the client's to show from what it holds.

## Alternatives considered
- **Host-wide `AddProblemDetails` with status code pages or an exception handler.** Rejected.
  They would give a body to the 400, 401 and 500 that `contract.md` §2 leaves without detail,
  and change the bare 401 challenge of ADR-ARCH-038 decision 4.
- **A minimal JSON body of our own**, such as `{"field":"organizationName"}`. Rejected: a format
  of the product's own, where RFC 9457 is the standard the framework already writes.
- **A message per reason, missing or too long.** Rejected: `contract.md` §2 reveals the field at
  fault only.
- **The problem details of MicroKit.Result.AspNetCore.** Not chosen: the library is not copied
  (ADR-ARCH-035, ADR-ARCH-037), and the framework's types suffice.

## References
- RFC 9457, "Problem Details for HTTP APIs".
- ASP.NET Core 10: `TypedResults.Problem`, `ProblemHttpResult`, `HttpValidationProblemDetails`,
  `AddProblemDetails`, `UseStatusCodePages`, `UseExceptionHandler`; bodies observed in the host
  tests of #220 on 2026-11-22.
- BP-001 `contract.md` §2, §9.
