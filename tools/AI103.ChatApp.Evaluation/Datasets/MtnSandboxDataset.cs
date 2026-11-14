using AI103.ChatApp.Evaluation.Models;

namespace AI103.ChatApp.Evaluation.Datasets;

public static class MtnSandboxDataset
{
    public static IReadOnlyList<TestCase> GetTestCases() =>
    [
        new TestCase(
            Id: "MTN-01",
            Category: "API Purpose",
            Question: "What is the purpose of Request to Pay in MTN Sandbox?",
            RequiredKeywords: ["Request to Pay", "202", "payer", "collection"],
            ProhibitedKeywords: ["credit card", "PayPal"],
            ExpectedCitations: ["MTN_Sandbox_API_Complete_Guide.md"],
            ExpectedSummary: "Initiates an asynchronous collection from a mobile subscriber to a merchant, returning 202 Accepted."),

        new TestCase(
            Id: "MTN-02",
            Category: "Mandatory Headers",
            Question: "What headers are mandatory when creating an API User with POST /v1_0/apiuser?",
            RequiredKeywords: ["X-Reference-Id", "Ocp-Apim-Subscription-Key"],
            ProhibitedKeywords: ["Authorization: Bearer"],
            ExpectedCitations: ["MTN_Sandbox_API_Complete_Guide.md"],
            ExpectedSummary: "Requires X-Reference-Id (UUIDv4) and Ocp-Apim-Subscription-Key."),

        new TestCase(
            Id: "MTN-03",
            Category: "Authentication & OAuth",
            Question: "Explain the step-by-step flow to generate an OAuth access token in MTN Sandbox.",
            RequiredKeywords: ["POST", "apiuser", "apikey", "Basic", "token"],
            ProhibitedKeywords: ["client_credentials grant type with client_secret from Azure AD"],
            ExpectedCitations: ["MTN_Sandbox_API_Complete_Guide.md"],
            ExpectedSummary: "1. Create API user. 2. Generate API key. 3. Call /{module}/token/ using Basic Auth base64(apiUser:apiKey)."),

        new TestCase(
            Id: "MTN-04",
            Category: "Callbacks & Async",
            Question: "Explain callback configuration and how async financial operations deliver results.",
            RequiredKeywords: ["202", "X-Callback-Url", "GET", "referenceId"],
            ProhibitedKeywords: ["WebSocket", "gRPC"],
            ExpectedCitations: ["MTN_Sandbox_API_Complete_Guide.md"],
            ExpectedSummary: "Finance POSTs return 202 Accepted. Callbacks use X-Callback-Url header; status can also be polled via GET /{referenceId}."),

        new TestCase(
            Id: "MTN-05",
            Category: "Sandbox Failure Triggers",
            Question: "Which phone number triggers an APPROVAL_REJECTED failure for RequestToPay in the sandbox?",
            RequiredKeywords: ["46733123451", "APPROVAL_REJECTED"],
            ProhibitedKeywords: [],
            ExpectedCitations: ["MTN_Sandbox_API_Complete_Guide.md"],
            ExpectedSummary: "MSISDN 46733123451 simulates customer rejection in Sandbox."),

        new TestCase(
            Id: "MTN-06",
            Category: "Negative / Scope Boundary",
            Question: "What is the API endpoint and procedure for ACH wire transfers in the MTN Sandbox?",
            RequiredKeywords: ["not supported", "not available"],
            ProhibitedKeywords: ["POST /ach/v1_0/transfer", "POST /v1_0/ach"],
            ExpectedCitations: ["MTN_Sandbox_API_Complete_Guide.md"],
            ExpectedSummary: "ACH transfers are not supported in the MTN Sandbox (only Collection, Disbursement, and Remittance).")
    ];
}
