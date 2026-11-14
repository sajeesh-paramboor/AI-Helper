# MTN FinTech Sandbox API - Complete Guide (Requirements, Flows, Inputs/Outputs, Testing)

Project documented: `MTN.FINTECH.Sandbox.Api` only (.NET 10 ASP.NET Core, Azure Table Storage / Azurite).
Audience: providers/developers who need to understand and test the MTN MoMo-style sandbox, and an AI chat assistant answering questions about it.

---

## 1. Overview and Scope

### 1.1 What the sandbox is
A stub implementation of the MTN MoMo Open API. It does not move money. Every response is produced by deterministic "scenario triggers": specific input values (an amount or a phone number) force specific outcomes (SUCCESSFUL, FAILED with a reason, PENDING, errors).

### 1.2 Modules
| Module | Purpose |
|---|---|
| Provisioning | Create API user, create API key, get API user |
| Collection | Invoice, Payment, RequestToPay, RequestToWithdraw, PreApproval, delivery notification |
| Disbursement | Deposit, Refund, Transfer |
| Remittance | Transfer, CashTransfer |
| Common (all 3 modules) | Basic user info, account balance, account holder active check |
| KYC with consent (all 3 modules) | bc-authorize, oauth2 token, userinfo |
| Health | ping, warmup |

### 1.3 Important notes
- **There is no "ACH" API in this project.** This guide covers every API actually exposed by `MTN.FINTECH.Sandbox.Api`.
- **API count:** the test matrix says 51 APIs, the business said 48, the code exposes more route registrations. The difference is how you count: per module (token x3, bc-authorize x3, etc.) or per distinct operation, and whether versioned aliases (v1_0/v2_0) are counted. Section 14 lists every route exactly as mapped in code.
- **Only currency `EUR` is accepted** (case-sensitive: `eur` is rejected).

### 1.4 Environments / base URLs
| Environment | Base URL |
|---|---|
| Local HTTPS | `https://localhost:7300` |
| Local HTTP | `http://localhost:5300` |
| Swagger UI | `/swagger` |
| Real MTN sandbox (from test matrix) | `https://sandbox.momodeveloper.mtn.com` |

Local run needs Azurite (Table Storage emulator) running.

### 1.5 Serialisation
- JSON is camelCase and null properties are omitted.
- Exceptions (snake_case): token responses (`access_token`, `token_type`, `expires_in`), OAuth2/CIBA responses (`auth_req_id`, `refresh_token`), basic user info (`given_name`, `family_name`, `updated_at`).

---

## 2. Authentication and Authorisation

### 2.1 Provisioning to token to API call
1. `POST /v1_0/apiuser` with header `X-Reference-Id: <new UUID>` and body `{"providerCallbackHost":"<host>"}`. The UUID becomes the API user id. Response 201 (empty body).
2. `POST /v1_0/apiuser/{apiUser}/apikey`. Response 201 `{"apiKey":"..."}`.
3. `POST /{collection|disbursement|remittance}/token/` with `Authorization: Basic base64(apiUser:apiKey)`. Response 200 token.
4. Call business APIs with `Authorization: Bearer <access_token>`.

Provisioning endpoints (steps 1-2, and GET apiuser) need no Bearer token. Token endpoints need Basic auth.

### 2.2 Token response (200)
```json
{
  "access_token": "<jwt>",
  "token_type": "access_token",
  "expires_in": 3600,
  "scope": "optional",
  "refresh_token": "optional",
  "refresh_token_expired_in": 0
}
```
- Request body (optional): form field `scope`.
- JWT: HMAC-SHA256; issuer and audience `mtn-fintech-sandbox`; claims `sub` (api user), `jti`, `iat`, `env=sandbox`, `scope` (only if provided); lifetime 3600 s; clock skew 0 (expires exactly on time).

### 2.3 Token endpoint errors
| Condition | Status | Body |
|---|---|---|
| Missing header or scheme not Basic | 401 | `{"message":"Basic Authorization header is required."}` |
| Base64 invalid or not `user:key` | 400 | `{"message":"Invalid Authorization header format. Expected Base64(apiUser:apiKey)."}` |
| API user unknown or inactive | 401 | `{"message":"Invalid API User or user is not active."}` |
| No active API key | 401 | `{"message":"No active API key found for this user."}` |
| Wrong API key | 401 | `{"message":"Invalid API Key."}` |
| Unexpected | 500 | `{"message":"An unexpected error occurred."}` |

### 2.4 Bearer validation on business APIs (evaluation order)
1. Authorization header missing: **400** `BAD_REQUEST`.
2. Scheme is not `Bearer`: **400** `BAD_REQUEST`.
3. Token malformed: **400**.
4. Token expired or signature/issuer/audience invalid: **401** `{"code":"UNAUTHORIZED","message":"Access token is missing or invalid."}`.
5. Authorize filter: claim `env` is not `sandbox`: **403** `NOT_ALLOWED_TARGET_ENVIRONMENT` ("Access to target environment is forbidden.").
6. Authorize filter: `sub` claim missing, or API user not found/inactive: **401** `UNAUTHORIZED`.

The OAuth2 filter (used only by `userinfo`) applies the same checks and additionally requires a `scope` claim. A token from `/token/` without a scope gets 401 on userinfo.

### 2.5 Headers
| Header | Used on | Notes |
|---|---|---|
| `Authorization` | all | Basic (token endpoints, oauth2 token) or Bearer (everything else) |
| `X-Reference-Id` | POST create calls, apiuser creation | UUID. Becomes the transaction id (row key). Duplicate for same operation = 409 |
| `X-Callback-Url` | async POST calls | Optional. If present a PUT callback is sent |
| `X-Target-Environment` | all | `sandbox`. Not enforced by code (environment is taken from the token claim `env`), but send it for parity with MTN |
| `Ocp-Apim-Subscription-Key` | all | Not enforced by code; required by the real MTN gateway |
| `Content-Type` | POST | `application/json`; `application/x-www-form-urlencoded` for token, bc-authorize, oauth2 token |

### 2.6 Transaction ownership
Each stored transaction records the API user that created it. Reading it with a token of another API user returns **403 NOT_ALLOWED** ("Access to the requested resource is forbidden."). Exceptions: RequestToPay delivery notification returns 404 (masked); CancelInvoice returns 409.

---

## 3. Common Error Catalogue

Standard error body: `{"code":"<CODE>","message":"<text>"}`.

| Code | Message | Typical status |
|---|---|---|
| `INTERNAL_SERVER_ERROR` | An unexpected error occurred. | 500 |
| `RESOURCE_ALREADY_EXIST` | Duplicated reference id. Creation of resource failed. | 409 |
| `RESOURCE_NOT_FOUND` | Requested resource was not found. | 404 (409 for CancelInvoice, 500/503 in some stubs) |
| `BAD_REQUEST` | request body required / field required / validation message | 400 |
| `NOT_ALLOWED` | Access to the requested resource is forbidden. | 403 |
| `UNSUPPORTED_SCOPE` | The scope is not supported | 500 |
| `NOT_ALLOWED_TARGET_ENVIRONMENT` | Access to target environment is forbidden. | 403 |
| `INTERNAL_PROCESSING_ERROR` | An internal error occurred while processing. | 500 |
| `SERVICE_UNAVAILABLE` | Service temporarily unavailable, try again later. | 503 |
| `UNAUTHORIZED` | Access token is missing or invalid. | 401 |
| `TRANSACTION_NOT_FOUND` | The transaction could not be found | 500 (refund original not found) |
| `INVALID_CURRENCY` | Currency not supported. | 500 |

Provisioning endpoints use a simpler body `{"message":"..."}` for 400/404/409 (see section 5).

---

## 4. Validation Rules (shared)

### 4.1 Account holder (party) id
`partyIdType` (case-insensitive) must be `MSISDN`, `EMAIL`, `ALIAS` or `ID`.
| Type | Rule |
|---|---|
| MSISDN | `^\d{7,15}$` |
| EMAIL | `^[^@\s]+@[^@\s]+\.[^@\s]+$` |
| ALIAS, ID | any non-empty string |

400 messages: `accountHolderIdType '{x}' is not supported. Allowed values: MSISDN, EMAIL, ALIAS, ID.`, `accountHolderId '{x}' is not a valid MSISDN. Must be 7-15 digits.`, `accountHolderId '{x}' is not a valid email address.`, `accountHolderId is required.`

### 4.2 Other ids
- Reference id must be a UUID: `referenceId '{x}' is not a valid UUID.` (400).
- PreApproval id: `^\d{1,20}$`: `preApprovalId '{x}' is not valid. Must be 1-20 digits.` (400).

### 4.3 Order of checks for body-based POSTs

**Deposit, Transfer, CashTransfer** (full validation):
1. Body null: 400 "request body required"
2. `amount` empty: 400 "amount is required."
3. `currency` empty: 400 "currency is required."
4. `currency` not `EUR`: **500** `INVALID_CURRENCY`
5. `externalId` empty: 400 "externalId is required."
6. `payee` null: 400 "payee is required."
7. payee type/id format: 400 (section 4.1)

**Refund:** steps 1-5 above, then `referenceIdToRefund` empty (400 "referenceIdToRefund is required.") and not a UUID (400 "referenceIdToRefund must be a valid UUID."). No payee check.

**Invoice, Payment, RequestToPay, RequestToWithdraw, PreApproval** (light validation): body null (400), currency empty (400 "currency is required."), currency not EUR (500 `INVALID_CURRENCY`). Currency field: `currency` (Invoice, RTP, RTW), `money.currency` (Payment), `payerCurrency` (PreApproval). Other fields are not validated.

---

## 5. Async Finance Behaviour and Callbacks

### 5.1 Async pattern (all "create" finance POSTs)
1. Validate the body (400/500 on failure).
2. Resolve scenario from the trigger value (amount or payee/payer id).
3. Save to table `sandboxfinance` (partition = operation type, row key = `X-Reference-Id`) with the owner API user.
4. If `X-Callback-Url` present, schedule a callback.
5. Return **202 Accepted, empty body**.

Duplicate `X-Reference-Id` for the same operation: **409 RESOURCE_ALREADY_EXIST**.
The scenario is decided at POST time and saved. A later GET only reads stored state; it never re-runs the scenario.
Unlisted trigger values give SUCCESSFUL.
Generated values: `financialTransactionId` (9-10 digit random string, only when SUCCESSFUL on RequestToPay, RequestToWithdraw, Deposit, Transfer, Refund, CashTransfer); `paymentReference` and `invoiceId` (9-10 digits).

### 5.2 Callback
- Method: **HTTP PUT** to `X-Callback-Url` with the same JSON as the GET status response.
- Fire-and-forget on a background task; HttpClient `SandboxCallback`, timeout 10 s, no retries. Failures are only logged.
- No host check against `providerCallbackHost` in code.
- PreApproval "second callback" scenarios: after the first PUT, a second PUT is sent with `status: "CREATED"` and no reason.
- Testing tip: use a request catcher (webhook.site, local listener) as the callback URL.

### 5.3 GET status common behaviour
| Condition | Result |
|---|---|
| Not found | 404 `RESOURCE_NOT_FOUND` |
| Owned by another user | 403 `NOT_ALLOWED` |
| Invalid UUID | 400 for Deposit, Refund, Transfer, CashTransfer; **404** for Invoice, Payment, RequestToPay, RequestToWithdraw, PreApproval (they do not validate the UUID) |

### 5.4 Trigger values convention
MSISDN triggers are of form `467331234xx`. "50" below means `46733123450`.

---

## 6. Test Tooling

PowerShell setup used by all examples:
```powershell
$base = "https://localhost:7300"
$apiUser = [guid]::NewGuid().ToString()
Invoke-RestMethod -Method Post "$base/v1_0/apiuser" -Headers @{"X-Reference-Id"=$apiUser} `
  -ContentType "application/json" -Body '{"providerCallbackHost":"webhook.site"}' -SkipCertificateCheck
$key = (Invoke-RestMethod -Method Post "$base/v1_0/apiuser/$apiUser/apikey" -SkipCertificateCheck).apiKey
$basic = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("${apiUser}:${key}"))
$tok = (Invoke-RestMethod -Method Post "$base/collection/token/" -Headers @{Authorization="Basic $basic"} -SkipCertificateCheck).access_token
$h = @{Authorization="Bearer $tok"; "X-Target-Environment"="sandbox"}
```
Repeat the token call with `/disbursement/token/` and `/remittance/token/` as needed (the same API user and key work for all three).
Use `-SkipCertificateCheck` (PowerShell 7) for the local dev certificate, or `curl -k`.

---

## 7. Provisioning APIs

### 7.1 Create API User - `POST /v1_0/apiuser`
**Purpose / use case:** register a sandbox API user (the first step for any provider).
**Auth:** none.
**Headers:** `X-Reference-Id: <UUID>` (becomes the API user id), `Content-Type: application/json`.
**Request body:**
```json
{ "providerCallbackHost": "webhook.site" }
```
**Responses:**
| Status | Body | When |
|---|---|---|
| 201 | empty | user created |
| 400 | `{"message":"providerCallbackHost is required."}` | body missing or `providerCallbackHost` blank |
| 409 | `{"message":"API User '<id>' already exists."}` | same `X-Reference-Id` used twice |
| 500 | `{"code":"INTERNAL_SERVER_ERROR","message":"An unexpected error occurred."}` | unexpected |

The user's target environment is always `sandbox`.
**Test steps:**
1. POST with a fresh GUID: expect 201.
2. Repeat with the same GUID: expect 409.
3. POST with `{}` or an empty `providerCallbackHost`: expect 400.

### 7.2 Get API User - `GET /v1_0/apiuser/{apiUser}`
**Purpose:** confirm the user exists and read its callback host.
**Auth:** none.
**200 response:**
```json
{ "providerCallbackHost": "webhook.site", "targetEnvironment": "sandbox" }
```
**Errors:** 404 `{"message":"API User '<id>' not found."}`; 500 standard error.
**Test:** create user then GET (200); GET a random GUID (404).

### 7.3 Create API Key - `POST /v1_0/apiuser/{apiUser}/apikey`
**Purpose:** generate the secret used with the API user id for Basic auth on the token endpoint.
**Auth:** none. **Body:** none.
**201 response:** `{ "apiKey": "<generated key>" }`
**Errors:** 404 `{"message":"API User '<id>' does not exist."}`; 500 standard error.
**Notes:** the key is shown only in this response; store it. Do not log or commit it.
**Test:** create key for an existing user (201); for an unknown GUID (404).

### 7.4 Create Access Token - `POST /{collection|disbursement|remittance}/token/`
**Purpose:** exchange API user + key for a Bearer JWT (3600 s).
**Auth:** `Authorization: Basic base64(apiUser:apiKey)`.
**Body:** optional form field `scope`.
**200 response and error table:** see sections 2.2 and 2.3.
**Test:**
1. Valid credentials: 200, decode JWT and confirm `env=sandbox`.
2. No header: 401. Header `Basic !!!`: 400. Wrong key: 401. Unknown user: 401.
3. Call a business API with the token (200/202), then wait for expiry (3600 s) and confirm 401.

---

## 8. Common APIs (available on collection, disbursement and remittance)

Replace `{module}` with `collection`, `disbursement` or `remittance`. All require a Bearer token (section 2.4).

### 8.1 Get Basic User Info - `GET /{module}/v1_0/accountholder/{partyIdType}/{partyId}/basicuserinfo`
**Purpose:** KYC-lite lookup of an account holder (no consent flow).
**Validation:** section 4.1 (400 on bad type/id).
**Scenario:** deny-list returns **400 `BAD_REQUEST`** with message "Account holder not found." for MSISDN `00000000000`, EMAIL `invalid@no.no`, ALIAS `bad-alias`. Any other valid id returns 200:
```json
{
  "sub": "0",
  "name": "Sand Box",
  "given_name": "Sand",
  "family_name": "Box",
  "birthdate": "1976-08-13",
  "locale": "sv_SE",
  "gender": "MALE",
  "updated_at": 1700000000
}
```
**Test:** `.../msisdn/46733123450/basicuserinfo` (200), `.../msisdn/00000000000/basicuserinfo` (400), `.../msisdn/abc/basicuserinfo` (400 invalid MSISDN), `.../foo/123/basicuserinfo` (400 unsupported type).

### 8.2 Get Account Balance - `GET /{module}/v1_0/account/balance`
**Purpose:** check provider account balance.
**Behaviour:** NO trigger. Each call returns one of 8 outcomes picked **at random**, so repeat the call several times to see them all:
| Outcome | Status | Body |
|---|---|---|
| Normal | 200 | `{"availableBalance":"1000","currency":"EUR"}` |
| Zero | 200 | `{"availableBalance":"0","currency":"EUR"}` |
| Negative | 200 | `{"availableBalance":"-25","currency":"EUR"}` |
| Target env forbidden | 403 | `NOT_ALLOWED_TARGET_ENVIRONMENT` |
| Internal error | 500 | `INTERNAL_PROCESSING_ERROR` |
| Unavailable | 503 | `SERVICE_UNAVAILABLE` |
| Not found | 404 | `RESOURCE_NOT_FOUND` |
| Not allowed | 403 | `NOT_ALLOWED` ("Authorization failed. Insufficient permissions.") |

**Known gap:** `GET /{module}/v1_0/account/balance/{currency}` appears in the test matrix but is **not implemented** in the code.
**Test:** call 20+ times and verify each response matches one of the rows; make the client handle all of them.

### 8.3 Validate Account Holder Status - `GET /{module}/v1_0/accountholder/{partyIdType}/{partyId}/active`
**Purpose:** check whether a party exists and is active before sending money.
**200 default:** `{"result":true}`.
**Triggers (identical outcome order for each id type):**
| Outcome | MSISDN | EMAIL | ALIAS |
|---|---|---|---|
| 404 `RESOURCE_NOT_FOUND` | 46733123450 | notfound@email.com | 5cecb5a7-8bd0-4f49-87b1-8eb9ecc7b7bc |
| 200 `{"result":false}` (inactive) | 46733123451 | notactive@email.com | b0040b3c-b426-4a90-af6e-673e65861cd7 |
| 500 `NOT_ALLOWED` | 46733123452 | notallowed@email.com | 4d5f500f-c385-4901-9be5-25b6d36ad220 |
| 500 `NOT_ALLOWED_TARGET_ENVIRONMENT` | 46733123453 | notallowedtargetenvironment@email.com | d2265f9b-0c22-496d-908f-79a65ad66266 |
| 500 `INTERNAL_PROCESSING_ERROR` | 46733123454 | internalprocessingerror@email.com | 8f548a78-ceb8-4d12-a243-5640b91e91a4 |
| 503 `SERVICE_UNAVAILABLE` | 46733123455 | serviceunavailable@email.com | 585c07a4-2c80-42f7-9e73-e586b36dee68 |

Any other valid id: 200 `{"result":true}`. Bad format: 400.
**Test:** call each trigger and the default id; assert status and body.

---

## 9. KYC with Consent (CIBA) APIs (collection, disbursement, remittance)

**Use case:** get a user's profile only after the user consents. Flow:
`bc-authorize` (get `auth_req_id`) then `oauth2/token` (exchange for a consent token) then `userinfo` (read profile).

### 9.1 `POST /{module}/v1_0/bc-authorize`
**Auth:** Bearer (from `/token/`). **Content-Type:** `application/x-www-form-urlencoded`.
**Form fields:**
| Field | Rule |
|---|---|
| `login_hint` | `ID:{msisdn}/MSISDN` e.g. `ID:46733123454/MSISDN` |
| `scope` | one of: limited, profile, address, email, phone, payment, merchant_payment, transfer, transfer_to_any_bank_account, all_info |
| `access_type` | `OFFLINE` or `ONLINE` (case-insensitive) |

**Check order and responses:**
1. Scope missing/invalid: **500** `UNSUPPORTED_SCOPE`.
2. `access_type` missing/invalid: **500 with empty body**.
3. `login_hint` missing or unparsable: **400** `BAD_REQUEST` ("login_hint is required." / "login_hint is missing or has an invalid format.").
4. Success: **200** `{"auth_req_id":"<uuid>","expires_in":3600,"interval":5}`.

The MSISDN from `login_hint` is stored with the request and decides the next step (9.2).

### 9.2 `POST /{module}/oauth2/token/`
**Auth:** **Basic** (apiUser:apiKey), not Bearer.
**Form fields:** `grant_type=urn:openid:params:grant-type:ciba`, `auth_req_id=<uuid from 9.1>`.
**Errors:**
| Condition | Status |
|---|---|
| Header missing or not Basic | 401 |
| Bad Base64 | 400 |
| Not form content type | 400 |
| Wrong `grant_type` | 400 |
| `auth_req_id` missing | 400 |
| Invalid credentials | 401 |
| `auth_req_id` not a UUID | 400 `{"error":"invalid_request"}` |
| Request expired | 400 `{"error":"expired_token"}` |

**Consent scenarios** (MSISDN stored at bc-authorize), returned as 400 `{"error":"<code>"}`:
| MSISDN | Error |
|---|---|
| 46733123450 | `authorization_pending` |
| 46733123451 | `access_denied` |
| 46733123452 | `authorization_pending` |
| 46733123453 | `expired_token` |
| 46733123455, 46733123456, 46733123457 | `invalid_grant` |
| 46733123454, 46733123458 to 46733123461, any other | success |

If the `auth_req_id` is a valid UUID but unknown, a token is still issued with a randomly generated scope.
**Success 200:**
```json
{
  "access_token": "<jwt>",
  "token_type": "Bearer",
  "expires_in": 300,
  "scope": "profile",
  "refresh_token": "only when access_type=OFFLINE",
  "refresh_token_expires_in": 31536000
}
```

### 9.3 `GET /{module}/oauth2/v1_0/userinfo`
**Auth:** Bearer token obtained from 9.2 (must contain a `scope` claim).
**200:** same "Sand Box" profile as basic user info. **401** if no scope claim (e.g. a token from `/token/`), or invalid token. **403** `NOT_ALLOWED_TARGET_ENVIRONMENT` possible.

### 9.4 Test flow
1. bc-authorize with `ID:46733123454/MSISDN`, `scope=profile`, `access_type=ONLINE`: 200 with `auth_req_id`.
2. oauth2/token with Basic auth and that id: 200 consent token.
3. userinfo with that token: 200.
4. Repeat with MSISDN `...450` (pending), `...451` (denied), `...453` (expired), `...455` (invalid_grant).
5. Negative: scope `bogus` (500 UNSUPPORTED_SCOPE), no `access_type` (500 empty), no `login_hint` (400), userinfo with the plain `/token/` token (401).

---

## 10. Collection APIs

All need a Bearer token. Create calls are async (section 5): 202 with empty body, callback optional. Common create headers: `Authorization`, `X-Reference-Id: <new UUID>`, `X-Callback-Url` (optional), `X-Target-Environment: sandbox`, `Content-Type: application/json`.
Common create errors: 400 (body/validation), 500 `INVALID_CURRENCY` (currency not EUR), 409 `RESOURCE_ALREADY_EXIST` (duplicate `X-Reference-Id`), 401/403 (auth, section 2.4), 500 `INTERNAL_SERVER_ERROR`.

### 10.1 Create Invoice - `POST /collection/v2_0/invoice`
**Use case:** a merchant issues an invoice to a payer.
**Request:**
```json
{
  "externalId": "INV-1001",
  "amount": 100.50,
  "currency": "EUR",
  "validityDuration": 3600,
  "intendedPayer": { "partyIdType": "MSISDN", "partyId": "46733123450" },
  "payee": { "partyIdType": "MSISDN", "partyId": "46733123451" },
  "description": "Order 1001"
}
```
`amount` is a decimal here and is the scenario trigger. Validation: body and currency only.
**Triggers (`amount`):**
| Amount | Stored status | Error reason |
|---|---|---|
| 1000, 3000, any unlisted | SUCCESSFUL | none |
| 2000 | FAILED | INTERNAL_PROCESSING_ERROR |
| 4000 | FAILED | none |
| 5000, 6000 | PENDING | none |

**Callback payload** = Get Invoice Status response.

### 10.2 Get Invoice Status - `GET /collection/v2_0/invoice/{referenceId}`
**200:**
```json
{
  "referenceId": "<uuid>", "externalId": "INV-1001", "amount": "100.50", "currency": "EUR",
  "status": "SUCCESSFUL", "paymentReference": "123456789", "invoiceId": "987654321",
  "expiryDateTime": "2026-01-01T10:00:00Z", "errorReason": "INTERNAL_PROCESSING_ERROR",
  "intendedPayer": { "partyIdType": "MSISDN", "partyId": "46733123450" }, "description": "Order 1001"
}
```
(`errorReason` only when set.) Errors: 404 not found or invalid UUID (UUID is not validated here), 403 other user's invoice.
**Status values:** SUCCESSFUL, FAILED, PENDING, CANCELLED.

### 10.3 Cancel Invoice - `DELETE /collection/v2_0/invoice/{referenceId}`
**Body:** `{ "externalId": "INV-1001" }`
| Condition | Result |
|---|---|
| Invalid UUID | 400 |
| Body missing | 400 |
| `externalId` missing | 400 "externalId is required." |
| Invoice not found or owned by someone else | **409 `RESOURCE_NOT_FOUND`** (deliberate: mimics real MTN) |
| Success | **202**; stored status becomes `CANCELLED` |

**Test:** create invoice (amount 5000, PENDING), cancel (202), GET status (CANCELLED), cancel unknown id (409).

### 10.4 Create Payment - `POST /collection/v2_0/createpayment`
**Note:** the test matrix lists `POST /collection/v2_0/payment`; the code route is `/collection/v2_0/createpayment`.
**Request:**
```json
{
  "externalTransactionId": "PAY-1",
  "money": { "amount": "1000", "currency": "EUR" },
  "customerReference": "46733123450",
  "serviceProviderUserName": "merchant1"
}
```
**Triggers (`money.amount`, string):** same as Invoice (1000/3000/7000 or unlisted SUCCESSFUL; 2000 FAILED/INTERNAL_PROCESSING_ERROR; 4000 FAILED; 5000/6000 PENDING). Validation uses `money.currency`.

### 10.5 Get Payment Status - `GET /collection/v2_0/payment/{referenceId}`
**200 fields:** `referenceId, externalTransactionId, amount, currency, status, paymentReference, serviceProviderUserName, customerReference, errorReason?`. Errors: 404 (also for invalid UUID), 403.

### 10.6 Request To Pay - `POST /collection/v1_0/requesttopay`
**Use case:** merchant asks a customer to pay (customer approves on the phone).
**Request:**
```json
{
  "externalId": "RTP-1", "amount": "100", "currency": "EUR",
  "payer": { "partyIdType": "MSISDN", "partyId": "46733123450" },
  "payerMessage": "Order", "payeeNote": "Thanks"
}
```
Amount and partyId can be sent as strings or numbers. Trigger is `payer.partyId` (MSISDN):
| partyId | Status | Reason |
|---|---|---|
| 46733123450 | FAILED | INTERNAL_PROCESSING_ERROR |
| ...51 | FAILED | APPROVAL_REJECTED |
| ...52 | FAILED | EXPIRED |
| ...53 | PENDING | |
| ...54 | CREATED | |
| ...55 | FAILED | PAYER_NOT_FOUND |
| ...56 | FAILED | PAYEE_NOT_ALLOWED_TO_RECEIVE |
| ...57 | FAILED | NOT_ALLOWED |
| ...58 | FAILED | NOT_ALLOWED_TARGET_ENVIRONMENT |
| ...59 | FAILED | INVALID_CALLBACK_URL_HOST |
| ...60 | FAILED | INVALID_CURRENCY |
| ...61 | FAILED | INTERNAL_PROCESSING_ERROR |
| ...62 | FAILED | SERVICE_UNAVAILABLE |
| ...63 | FAILED | COULD_NOT_PERFORM_TRANSACTION |
| ...64 | FAILED | TRANSFER_TYPE_UNKNOWN |
| other | SUCCESSFUL | (financialTransactionId generated) |

### 10.7 Get Request To Pay Status - `GET /collection/v1_0/requesttopay/{referenceId}`
**200:** `{"financialTransactionId":"...","externalId":"RTP-1","amount":"100","currency":"EUR","status":"SUCCESSFUL","payer":{...},"payerMessage":"Order","payeeNote":"Thanks","errorReason":"..."}`. The failure field is named `errorReason` in the code (the matrix shows `reason`). `financialTransactionId` appears only for SUCCESSFUL. Errors: 404 (also invalid UUID), 403.

### 10.8 RequestToPay Delivery Notification - `POST /collection/v1_0/requesttopay/{referenceId}/deliverynotification`
**Body:** `{ "notificationMessage": "Please approve" }`
**Order:** invalid UUID 400; body missing 400; `notificationMessage` empty 400 "notificationMessage is required."; transaction not found 404; owned by another user **404** (masked); success **200** empty body.

### 10.9 Request To Withdraw - `POST /collection/v1_0/requesttowithdraw` and `/collection/v2_0/requesttowithdraw`
Same request body as Request To Pay. Triggers on `payer.partyId`: same as RTP for `...50` to `...63` (there is no `...64`); `...54` = CREATED; other = SUCCESSFUL.

### 10.10 Get Request To Withdraw Status - `GET /collection/v1_0/requesttowithdraw/{referenceId}`
Same as RTP status plus `referenceId` in the body. Errors: 404 (also invalid UUID), 403.

### 10.11 Test flow - RequestToPay
1. POST RTP with partyId `46733123450` + `X-Callback-Url`: expect 202, empty body, and a PUT callback with FAILED/INTERNAL_PROCESSING_ERROR.
2. POST with `46733123499` (default): 202, GET status = SUCCESSFUL with `financialTransactionId`.
3. Repeat step 2 with the same `X-Reference-Id`: 409.
4. GET with another user's token: 403. GET random UUID: 404.
5. POST with currency `USD`: 500 INVALID_CURRENCY. Body `{}`: 400.
6. POST delivery notification: 200.

---

## 11. PreApproval APIs (collection)

### 11.1 Create PreApproval - `POST /collection/v2_0/preapproval`
**Use case:** a payer authorises a merchant to debit repeatedly.
**Request:**
```json
{
  "payer": { "partyIdType": "MSISDN", "partyId": "46733123450" },
  "payerCurrency": "EUR",
  "payerMessage": "Subscription",
  "validityTime": 3600
}
```
Validation: body and `payerCurrency`. Trigger is `payer.partyId`:
| partyId | Status | Reason | Second callback (CREATED) |
|---|---|---|---|
| 46733123450 | FAILED | none | yes |
| ...51 | FAILED | APPROVAL_REJECTED | yes |
| ...52 | FAILED | EXPIRED | yes |
| ...53 | PENDING | | no |
| ...54 | PENDING | | no |
| ...55 | FAILED | PAYER_NOT_FOUND | yes |
| ...56 | FAILED | NOT_ALLOWED | no |
| ...57 | FAILED | NOT_ALLOWED_TARGET_ENVIRONMENT | no |
| ...58 | FAILED | INVALID_CALLBACK_URL_HOST | no |
| ...59 | FAILED | INVALID_CURRENCY | no |
| ...60 | FAILED | INTERNAL_PROCESSING_ERROR | no |
| ...61 | FAILED | SERVICE_UNAVAILABLE | no |
| ...69, 256771234321, any other | SUCCESSFUL | | yes |

For "yes" rows, with `X-Callback-Url` set, two PUTs are sent: the status payload, then a payload with `status: CREATED`.

### 11.2 Get PreApproval Status - `GET /collection/v2_0/preapproval/{referenceId}`
**200:** `payer, payerCurrency, payerMessage, expirationDateTime, status, reason?`. Errors: 404 (also invalid UUID), 403.

### 11.3 Get Approved PreApprovals - `GET /collection/v1_0/preapprovals/{partyIdType}/{partyId}`
Validation: section 4.1 (400).
| Result | Inputs |
|---|---|
| 404 | MSISDN 46733123450, EMAIL notfound@email.com, ALIAS 5cecb5a7-8bd0-4f49-87b1-8eb9ecc7b7bc, ID notfound-id-001 |
| 500 `INTERNAL_SERVER_ERROR` | MSISDN 46733123454, EMAIL internalprocessingerror@email.com, ALIAS 8f548a78-ceb8-4d12-a243-5640b91e91a4, ID internalerror-id-001 |
| 200 | everything else |

**200 body:**
```json
{ "preApprovalDetails": [ {
  "preApprovalId": "775068", "toFri": "FRI:-491062047186004431/MM", "fromFri": "FRI:123456/MM",
  "fromCurrency": "EUR", "createdTime": "...", "approvedTime": "...", "expiryTime": "...",
  "status": "APPROVED", "message": "I PAY YOU", "frequency": null, "startDate": "...",
  "lastUsedDate": "...", "offer": "New offer", "externalId": "...", "maxDebitAmount": null } ] }
```

### 11.4 Cancel PreApproval - `DELETE /collection/v1_0/preapproval/{preApprovalId}`
| preApprovalId | Result |
|---|---|
| not 1-20 digits | 400 |
| `00000000` | 404 |
| `99999999` | 500 `INTERNAL_SERVER_ERROR` |
| other digits | 200, empty body |

### 11.5 Test flow
1. Create preapproval with `...50`, callback URL set: 202; two callbacks arrive (FAILED, then CREATED).
2. Create with `...53`: 202, GET status = PENDING.
3. Create with default id: GET status = SUCCESSFUL.
4. Get approved list for an unknown-trigger MSISDN: 200; for `46733123450`: 404.
5. Cancel `12345` (200), `00000000` (404), `99999999` (500), `abc` (400).

---

## 12. Disbursement APIs

Same async pattern, headers and common errors as section 10. Money fields are strings.

### 12.1 Deposit - `POST /disbursement/v1_0/deposit` and `/disbursement/v2_0/deposit`
**Use case:** pay money into a customer's wallet (e.g. salary, payout).
**Request:**
```json
{
  "amount": "100", "currency": "EUR", "externalId": "DEP-1",
  "payee": { "partyIdType": "MSISDN", "partyId": "46733123499" },
  "payerMessage": "Salary", "payeeNote": "Thanks"
}
```
**Validation order:** section 4.3 (full validation: amount, currency, EUR, externalId, payee, payee id).
**Triggers (`payee.partyId`):**
| partyId | Status | Reason |
|---|---|---|
| 46733123450 | FAILED | INTERNAL_PROCESSING_ERROR |
| ...51 | FAILED | APPROVAL_REJECTED |
| ...52 | FAILED | EXPIRED |
| ...53, ...54, ...55 | SUCCESSFUL | |
| ...56 | FAILED | PAYEE_NOT_ALLOWED_TO_RECEIVE |
| ...57 | FAILED | NOT_ALLOWED |
| ...58 | FAILED | NOT_ALLOWED_TARGET_ENVIRONMENT |
| ...59 | FAILED | INVALID_CALLBACK_URL_HOST |
| ...60 | FAILED | INVALID_CURRENCY |
| ...61 | FAILED | INTERNAL_PROCESSING_ERROR |
| ...62 | FAILED | SERVICE_UNAVAILABLE |
| ...63 | FAILED | COULD_NOT_PERFORM_TRANSACTION |
| other | SUCCESSFUL | |

SUCCESSFUL results include a generated `financialTransactionId`.

### 12.2 Get Deposit Status - `GET /disbursement/v1_0/deposit/{referenceId}`
**200:** `financialTransactionId?, externalId, amount, currency, payee, payerMessage, payeeNote, status, reason?`.
Errors: invalid UUID **400**, not found 404, other owner 403.

### 12.3 Refund - `POST /disbursement/v1_0/refund` and `/disbursement/v2_0/refund`
**Use case:** reverse an earlier RequestToPay.
**Request:**
```json
{
  "amount": "5", "currency": "EUR", "externalId": "REF-1",
  "payerMessage": "Refund", "payeeNote": "Sorry",
  "referenceIdToRefund": "<X-Reference-Id of the original RequestToPay>"
}
```
**Rules:**
- Validation order: section 4.3 (Refund).
- The original must be a **RequestToPay** that exists (looked up by `referenceIdToRefund`). If not found: **500 `TRANSACTION_NOT_FOUND`** ("The transaction could not be found").
- The refund payee is the original RTP payer; the stored payment reference equals `referenceIdToRefund`.

**Triggers (`amount`, string "1" to "13"):**
| amount | Status | Reason |
|---|---|---|
| 1 | FAILED | TRANSACTION_NOT_FOUND |
| 2 | FAILED | INTERNAL_PROCESSING_ERROR |
| 3 | FAILED | APPROVAL_REJECTED |
| 4 | FAILED | EXPIRED |
| 5, 6 | SUCCESSFUL | |
| 7 | FAILED | NOT_ALLOWED |
| 8 | FAILED | NOT_ALLOWED_TARGET_ENVIRONMENT |
| 9 | FAILED | INVALID_CALLBACK_URL_HOST |
| 10 | FAILED | INVALID_CURRENCY |
| 11 | FAILED | INTERNAL_PROCESSING_ERROR |
| 12 | FAILED | SERVICE_UNAVAILABLE |
| 13 | FAILED | COULD_NOT_PERFORM_TRANSACTION |
| other | SUCCESSFUL | |

### 12.4 Get Refund Status - `GET /disbursement/v1_0/refund/{referenceId}`
Same response shape as Deposit status. Errors: invalid UUID 400, not found 404, other owner 403.

### 12.5 Test flow - Refund
1. Create a RequestToPay (collection) with ref `R1`, default payer: 202, wait, GET = SUCCESSFUL.
2. Create refund with `referenceIdToRefund=R1`, amount `5`: 202; GET status = SUCCESSFUL.
3. Refund with a random `referenceIdToRefund`: 500 TRANSACTION_NOT_FOUND.
4. Refund with `referenceIdToRefund=not-a-uuid`: 400.
5. Refund amount `3` with valid original: FAILED/APPROVAL_REJECTED.

---

## 13. Transfer and Remittance APIs

### 13.1 Transfer - `POST /remittance/v1_0/transfer` and `POST /disbursement/v1_0/transfer`
**Use case:** move money to another wallet (shared implementation for both modules). Note: a bare `/v1_0/transfer` route is mentioned in a code comment but is **not mapped**.
**Request:**
```json
{
  "amount": "100", "currency": "EUR", "externalId": "TRF-1",
  "payee": { "partyIdType": "MSISDN", "partyId": "46733123499" },
  "payerMessage": "Transfer", "payeeNote": "Thanks"
}
```
**Validation order:** section 4.3 (full validation).
**Triggers (`payee.partyId`):**
| partyId | Status | Reason |
|---|---|---|
| 46733123450 | FAILED | INTERNAL_PROCESSING_ERROR |
| ...51 | FAILED | APPROVAL_REJECTED |
| ...52 | FAILED | EXPIRED |
| ...53, ...54 | PENDING | |
| ...55 | FAILED | NOT_ENOUGH_FUNDS |
| ...56 | FAILED | PAYER_LIMIT_REACHED |
| ...57 | FAILED | PAYEE_NOT_FOUND |
| ...58 | FAILED | NOT_ALLOWED |
| ...59 | FAILED | NOT_ALLOWED_TARGET_ENVIRONMENT |
| ...60 | FAILED | INVALID_CALLBACK_URL_HOST |
| ...61 | FAILED | INVALID_CURRENCY |
| ...62 | FAILED | INTERNAL_PROCESSING_ERROR |
| ...63 | FAILED | SERVICE_UNAVAILABLE |
| ...64 | FAILED | TRANSFER_TYPE_UNKNOWN |
| other | SUCCESSFUL | |

### 13.2 Get Transfer Status - `GET /remittance/v1_0/transfer/{referenceId}` and `GET /disbursement/v1_0/transfer/{referenceId}`
**200:** `financialTransactionId?, externalId, amount, currency, payee, payerMessage, payeeNote, status, reason?`. Errors: invalid UUID 400, 404, 403.
Transfers are stored per operation type, so a transfer created on one module's route is read through either module's GET (shared service).

### 13.3 Cash Transfer - `POST /remittance/v2_0/cashtransfer`
**Use case:** international remittance with sender (payer) identity details.
**Request:**
```json
{
  "amount": "100", "currency": "EUR", "externalId": "CT-1",
  "payee": { "partyIdType": "MSISDN", "partyId": "46733123499" },
  "payerMessage": "Family support", "payeeNote": "Thanks",
  "originatingCountry": "SE", "originalAmount": "1000", "originalCurrency": "SEK",
  "payerIdentificationType": "PASSPORT", "payerIdentificationNumber": "X1234567",
  "payerIdentity": "ID-1", "payerFirstName": "Anna", "payerSurName": "Svensson",
  "payerLanguageCode": "en", "payerEmail": "anna@example.com",
  "payerMsisdn": "46701234567", "payerGender": "F"
}
```
**Validation order:** section 4.3 (full). Only the first six fields are validated; payer detail fields are stored and echoed.
**Triggers (`amount`, string "1" to "13"):**
| amount | Status | Reason |
|---|---|---|
| 1 | FAILED | TRANSACTION_NOT_FOUND |
| 2 | FAILED | INTERNAL_PROCESSING_ERROR |
| 3 | FAILED | APPROVAL_REJECTED |
| 4 | FAILED | EXPIRED |
| 5 | FAILED | PAYEE_NOT_FOUND |
| 6 | FAILED | PAYEE_NOT_ALLOWED_TO_RECEIVE |
| 7 | FAILED | NOT_ALLOWED |
| 8 | FAILED | NOT_ALLOWED_TARGET_ENVIRONMENT |
| 9 | FAILED | INVALID_CALLBACK_URL_HOST |
| 10 | FAILED | INVALID_CURRENCY |
| 11 | FAILED | INTERNAL_PROCESSING_ERROR |
| 12 | FAILED | SERVICE_UNAVAILABLE |
| 13 | FAILED | COULD_NOT_PERFORM_TRANSACTION |
| other | SUCCESSFUL | |

### 13.4 Get Cash Transfer Status - `GET /remittance/v2_0/cashtransfer/{referenceId}`
**200:** Transfer status fields plus `originatingCountry, originalAmount, originalCurrency, payerIdentificationType, payerIdentificationNumber, payerIdentity, payerFirstName, payerSurName, payerLanguageCode, payerEmail, payerMsisdn, payerGender`.
Errors: invalid UUID 400, not found 404, other owner 403, unexpected 500.

### 13.5 Test flow - Transfer / CashTransfer
1. Remittance token; POST transfer with payee `46733123499`: 202; GET: SUCCESSFUL.
2. Payee `...53`: PENDING; `...55`: FAILED/NOT_ENOUGH_FUNDS; `...57`: FAILED/PAYEE_NOT_FOUND.
3. Invalid payee `abc` with type MSISDN: 400; currency `usd`: 500 INVALID_CURRENCY.
4. CashTransfer with amount `5`: FAILED/PAYEE_NOT_FOUND; amount `100`: SUCCESSFUL with echoed payer fields.

---

## 14. Health APIs and Full Route Inventory

### 14.1 Health (no auth)
| Route | Response |
|---|---|
| `GET /api/health/ping` | 200 `{"status":"healthy","timestamp":"..."}` |
| `GET /api/health/warmup` | 200 `{"status":"warmed up","message":"Sandbox API warmup complete"}` |

### 14.2 Every route registered in code (53 route attributes)
| # | Method and route | Auth |
|---|---|---|
| 1 | POST `/v1_0/apiuser` | none |
| 2 | GET `/v1_0/apiuser/{apiUser}` | none |
| 3 | POST `/v1_0/apiuser/{apiUser}/apikey` | none |
| 4-6 | POST `/collection/token/`, `/disbursement/token/`, `/remittance/token/` | Basic |
| 7-9 | POST `/{collection,remittance,disbursement}/v1_0/bc-authorize` | Bearer |
| 10-12 | POST `/{collection,remittance,disbursement}/oauth2/token/` | Basic |
| 13-15 | GET `/{collection,remittance,disbursement}/oauth2/v1_0/userinfo` | Bearer (scoped) |
| 16-18 | GET `/{module}/v1_0/accountholder/{partyIdType}/{partyId}/basicuserinfo` | Bearer |
| 19-21 | GET `/{module}/v1_0/account/balance` | Bearer |
| 22-24 | GET `/{module}/v1_0/accountholder/{partyIdType}/{partyId}/active` | Bearer |
| 25 | POST `/collection/v2_0/invoice` | Bearer |
| 26 | GET `/collection/v2_0/invoice/{referenceId}` | Bearer |
| 27 | DELETE `/collection/v2_0/invoice/{referenceId}` | Bearer |
| 28 | POST `/collection/v2_0/createpayment` | Bearer |
| 29 | GET `/collection/v2_0/payment/{referenceId}` | Bearer |
| 30-31 | POST `/collection/v1_0/requesttowithdraw`, `/collection/v2_0/requesttowithdraw` | Bearer |
| 32 | GET `/collection/v1_0/requesttowithdraw/{referenceId}` | Bearer |
| 33 | POST `/collection/v1_0/requesttopay` | Bearer |
| 34 | GET `/collection/v1_0/requesttopay/{referenceId}` | Bearer |
| 35 | POST `/collection/v1_0/requesttopay/{referenceId}/deliverynotification` | Bearer |
| 36 | POST `/collection/v2_0/preapproval` | Bearer |
| 37 | GET `/collection/v2_0/preapproval/{referenceId}` | Bearer |
| 38 | GET `/collection/v1_0/preapprovals/{partyIdType}/{partyId}` | Bearer |
| 39 | DELETE `/collection/v1_0/preapproval/{preApprovalId}` | Bearer |
| 40-41 | POST `/disbursement/v1_0/deposit`, `/disbursement/v2_0/deposit` | Bearer |
| 42 | GET `/disbursement/v1_0/deposit/{referenceId}` | Bearer |
| 43-44 | POST `/disbursement/v1_0/refund`, `/disbursement/v2_0/refund` | Bearer |
| 45 | GET `/disbursement/v1_0/refund/{referenceId}` | Bearer |
| 46-47 | POST `/remittance/v1_0/transfer`, `/disbursement/v1_0/transfer` | Bearer |
| 48-49 | GET `/remittance/v1_0/transfer/{referenceId}`, `/disbursement/v1_0/transfer/{referenceId}` | Bearer |
| 50 | POST `/remittance/v2_0/cashtransfer` | Bearer |
| 51 | GET `/remittance/v2_0/cashtransfer/{referenceId}` | Bearer |
| 52-53 | GET `/api/health/ping`, `/api/health/warmup` | none |

### 14.3 Why the API count differs (48 / 51 / 53)
It depends on counting rules: per route attribute (53 above, including 2 health routes), per distinct operation (token, bc-authorize, oauth2 token, userinfo, basicuserinfo, balance, active are each one operation across 3 modules; v1_0/v2_0 aliases are one operation), or the business-provided list (48) versus the test matrix sheet "Required APIS" (51). Use the table above as the source of truth for what is actually implemented.

---

## 15. End-to-End Test Scenarios

### 15.1 Happy path: provisioning to collection to refund
1. Create API user (7.1), create API key (7.3), get token (7.4).
2. `POST /collection/v1_0/requesttopay` ref `A`, payer `46733123499`, callback URL set: 202.
3. Check the callback receiver got a PUT with `status: SUCCESSFUL`.
4. `GET /collection/v1_0/requesttopay/A`: SUCCESSFUL with `financialTransactionId`.
5. Disbursement token. `POST /disbursement/v2_0/refund` with `referenceIdToRefund=A`, amount `5`: 202.
6. `GET /disbursement/v1_0/refund/{refundRef}`: SUCCESSFUL.

### 15.2 Failure-injection pass (per API)
For each create API, run every trigger row in its table. Assert: POST returns 202, GET returns the mapped status/reason, callback payload equals the GET body.

### 15.3 Negative pass (apply to every create API)
| Test | Expected |
|---|---|
| No Authorization header | 400 BAD_REQUEST |
| `Authorization: Basic ...` on a Bearer API | 400 |
| Expired or tampered token | 401 UNAUTHORIZED |
| Token with `env` other than sandbox | 403 NOT_ALLOWED_TARGET_ENVIRONMENT |
| Empty body | 400 |
| `currency` = `USD` or `eur` | 500 INVALID_CURRENCY |
| Missing `currency` | 400 "currency is required." |
| Same `X-Reference-Id` twice | 409 RESOURCE_ALREADY_EXIST |
| Missing `X-Reference-Id` | not rejected by the controller (empty id is used); always send a UUID |

### 15.4 Ownership pass
Create transaction with user A, read with user B's token: 403 NOT_ALLOWED (404 for deliverynotification, 409 for CancelInvoice).

### 15.5 Test matrix checklist (tick per API)
- Success path with default values
- Every trigger value
- Callback received (PUT, correct payload)
- Duplicate reference id
- Invalid body / field missing
- Invalid currency
- GET not found, GET invalid UUID, GET wrong owner
- Auth failures (400, 401, 403)

---

## 16. Known Quirks and Mismatches

1. **No ACH API** exists in this project.
2. Payment create route is `/collection/v2_0/createpayment` (matrix says `/payment`).
3. `GET /{module}/v1_0/account/balance/{currency}` is in the matrix but **not implemented**.
4. A bare `/v1_0/transfer` route mentioned in a comment is **not mapped**; only the remittance and disbursement routes exist.
5. Account balance is **random**, not trigger-based, and returns non-200 statuses even though a code comment says all are 200.
6. Currency other than EUR returns **500 INVALID_CURRENCY**, not 400.
7. `bc-authorize` with a bad `access_type` returns 500 with an empty body.
8. CancelInvoice returns **409 RESOURCE_NOT_FOUND** for unknown invoices (mimics real MTN).
9. Delivery notification masks other users' transactions as 404.
10. UUID validation on GET differs: 400 for Deposit/Refund/Transfer/CashTransfer, 404 for Invoice/Payment/RTP/RTW/PreApproval.
11. RTP failure field is `errorReason` in the status body (matrix says `reason`).
12. Callback is **PUT** (not POST), no retry, 10 s timeout, no host check against `providerCallbackHost`.
13. PreApproval can send a second callback with status CREATED.
14. `X-Target-Environment` and `Ocp-Apim-Subscription-Key` are not enforced.
15. Scenario is decided at POST time; GET never re-evaluates. PENDING transactions never change on their own.
16. Amount/partyId accept numbers or strings per the review comments.
17. Unlisted trigger values always give SUCCESSFUL.

---

## 17. Troubleshooting

| Symptom | Likely cause and fix |
|---|---|
| Connection refused / 500 on first call | Azurite not running; start it, then retry (`/api/health/warmup`) |
| Certificate error | Use `-SkipCertificateCheck` / `curl -k`, or the HTTP URL `http://localhost:5300` |
| 400 on every business call | Bearer header missing or sent as Basic |
| 401 right after token | Token expired (3600 s) or signed with a different JWT key than the running instance |
| 401 on userinfo | Token has no `scope`; use the token from `oauth2/token/` |
| 403 NOT_ALLOWED on GET | The transaction belongs to a different API user |
| 409 on create | Reused `X-Reference-Id`; generate a new GUID per request |
| 500 INVALID_CURRENCY | Currency must be exactly `EUR` |
| Callback not received | Receiver unreachable or slower than 10 s; check API logs (failures are only logged); callback is PUT |
| Always SUCCESSFUL | Trigger value did not match a table row (check MSISDN digits or amount string) |

---

## 18. Questions This Document Answers (for chat retrieval)
- How do I get a token for the sandbox? (sections 2, 7)
- Which phone number makes a RequestToPay fail with APPROVAL_REJECTED? (10.6: `46733123451`)
- Which amount makes a refund fail with EXPIRED? (12.3: `4`)
- What does a 409 mean on create? (5.1, 15.3)
- Why is my currency rejected? (4.3, 16)
- How do I test callbacks? (5.2)
- Is there an ACH API? (1.3: no)

*End of document.*
