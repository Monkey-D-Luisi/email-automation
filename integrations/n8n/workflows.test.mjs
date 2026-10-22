import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const integrationRoot = new URL("./", import.meta.url);
const readJson = async name => JSON.parse(await readFile(new URL(name, integrationRoot), "utf8"));
const [ingestion, exportWorkflow, fixture, mapperSource] = await Promise.all([
  readJson("gmail-ingestion.json"),
  readJson("gmail-approved-draft-export.json"),
  readJson("fixtures/gmail-message.json"),
  readFile(new URL("scripts/gmail-message-mapper.js", integrationRoot), "utf8"),
]);

function executeSyntheticFixture() {
  const source = ingestion.nodes.find(node => node.name === "Synthetic Parsed Gmail Fixture").parameters.jsCode;
  return new Function("$input", source)({ all: () => [] });
}

function executeMapper(messages) {
  const mapper = ingestion.nodes.find(node => node.name === "Map and Validate Gmail Message").parameters.jsCode;
  assert.equal(mapper.trim(), mapperSource.trim());
  return new Function("$input", mapper)({ all: () => messages.map(json => ({ json })) });
}

function executeForEachItem(workflow, nodeName, items, linkedItems = {}) {
  const node = workflow.nodes.find(candidate => candidate.name === nodeName);
  assert.equal(node.parameters.mode, "runOnceForEachItem");
  return items.map((item, index) => new Function("$input", "$", node.parameters.jsCode)(
    { item: { json: item } },
    name => ({ item: { json: linkedItems[name][index] } })
  ));
}

test("both n8n exports are valid inactive workflows without credentials or machine-specific hosts", () => {
  for (const workflow of [ingestion, exportWorkflow]) {
    assert.equal(workflow.active, false);
    assert.ok(workflow.nodes.length > 0);
    assert.ok(workflow.connections);
    const nodeNames = new Set(workflow.nodes.map(node => node.name));
    assert.equal(nodeNames.size, workflow.nodes.length);
    for (const [sourceName, outputs] of Object.entries(workflow.connections)) {
      assert.ok(nodeNames.has(sourceName), `Unknown connection source: ${sourceName}`);
      for (const branches of Object.values(outputs))
        for (const branch of branches)
          for (const edge of branch)
            assert.ok(nodeNames.has(edge.node), `Unknown connection target: ${edge.node}`);
    }
    assert.equal(JSON.stringify(workflow).includes("localhost"), false);
    assert.equal(JSON.stringify(workflow).includes("127.0.0.1"), false);
    for (const node of workflow.nodes) assert.equal(node.credentials, undefined);
  }
  assert.equal(ingestion.settings.saveDataSuccessExecution, "none");
  assert.equal(ingestion.settings.saveDataErrorExecution, "none");
  assert.equal(exportWorkflow.settings.saveDataSuccessExecution, "none");
  assert.equal(exportWorkflow.settings.saveDataErrorExecution, "none");
});

test("Gmail ingestion fetches full inbox messages without modifying read state or downloading attachments", () => {
  const trigger = ingestion.nodes.find(node => node.type === "n8n-nodes-base.gmailTrigger");
  assert.equal(trigger.typeVersion, 1.4);
  assert.equal(trigger.parameters.simple, false);
  assert.equal(trigger.parameters.maxResults, 10);
  assert.equal(trigger.parameters.filters.includeDrafts, false);
  assert.equal(trigger.parameters.filters.includeSpamTrash, false);
  assert.deepEqual(trigger.parameters.filters.labelIds, ["INBOX"]);
  assert.equal(trigger.parameters.filters.readStatus, "both");
  assert.equal(trigger.parameters.options.downloadAttachments, false);
  assert.match(trigger.parameters.filters.q, /-in:sent/);
  assert.match(trigger.parameters.filters.q, /-in:drafts/);
  assert.equal(ingestion.nodes.some(node => /mark as read|delete|label/i.test(node.name)), false);
  assert.equal(ingestion.nodes.some(node => node.type === "n8n-nodes-base.manualTrigger"), true);
});

test("the mapper uses Gmail provider identity, prefers parsed plain text, and keeps thread and attachment metadata", () => {
  const [mapped] = executeMapper([fixture]);
  assert.equal(mapped.json.valid, true);
  assert.equal(mapped.json.payload.gmailMessageId, fixture.id);
  assert.notEqual(mapped.json.payload.gmailMessageId, fixture.messageId);
  assert.equal(mapped.json.payload.gmailThreadId, fixture.threadId);
  assert.equal(mapped.json.payload.senderEmail, "alex@example.test");
  assert.equal(mapped.json.payload.senderName, "Synthetic Sender");
  assert.equal(mapped.json.payload.bodyText, fixture.text);
  assert.equal(mapped.json.payload.attachments[0].externalAttachmentId, "synthetic-attachment-001");
  assert.equal(mapped.json.correlationId, `gmail-${fixture.id}`);

  const prefix = "x".repeat(120);
  const [longIdA, longIdB] = executeMapper([
    { ...fixture, id: `${prefix}-a` },
    { ...fixture, id: `${prefix}-b` },
  ]);
  assert.notEqual(longIdA.json.correlationId, longIdB.json.correlationId);
  assert.equal(longIdA.json.correlationId, `gmail-${prefix}-a`);

  const [missingSender] = executeMapper([{ ...fixture, from: undefined, headers: {} }]);
  assert.equal(missingSender.json.valid, true);
  assert.equal(missingSender.json.payload.senderEmail, null);
});

test("three concurrent message identities keep accepted, duplicate, and retry outcomes paired through bounded retries", () => {
  const synthetic = executeSyntheticFixture();
  assert.equal(synthetic.length, 3);
  const mapped = executeMapper(synthetic.map(item => item.json)).map(item => item.json);
  const requests = executeForEachItem(ingestion, "Build Gmail API Request", mapped).map(item => item.json);
  assert.equal(new Set(requests.map(request => request.providerMessageId)).size, 3);
  assert.equal(new Set(requests.map(request => request.correlationId)).size, 3);

  const initial = executeForEachItem(ingestion, "Classify Gmail API Response", [
    { statusCode: 202 },
    { statusCode: 200 },
    { statusCode: 503 },
  ], { "Build Gmail API Request": requests }).map(item => item.json);
  assert.deepEqual(initial.map(item => item.outcome), ["accepted", "duplicate", "retrying"]);
  assert.deepEqual(initial.map(item => item.providerMessageId), requests.map(item => item.providerMessageId));
  assert.deepEqual(initial.map(item => item.correlationId), requests.map(item => item.correlationId));

  const retrying = initial.filter(item => item.retry);
  const retryRequests = executeForEachItem(ingestion, "Prepare Bounded Retry", retrying).map(item => item.json);
  assert.equal(retryRequests.length, 1);
  assert.equal(retryRequests[0].attempt, 2);
  const retried = executeForEachItem(ingestion, "Classify Retry Response", [{ statusCode: 202 }], {
    "Prepare Bounded Retry": retryRequests,
  })[0].json;
  assert.equal(retried.outcome, "accepted");
  assert.equal(retried.attemptCount, 2);
  assert.equal(retried.providerMessageId, requests[2].providerMessageId);
  assert.equal(retried.correlationId, requests[2].correlationId);

  const finalAttempt = { ...requests[0], attempt: 3 };
  const networkFailure = executeForEachItem(ingestion, "Classify Ingestion Network Error", [{}], {
    "Build Gmail API Request": [finalAttempt],
  })[0].json;
  assert.equal(networkFailure.outcome, "retry-exhausted");
  assert.equal(networkFailure.retry, false);
  assert.equal(networkFailure.providerMessageId, finalAttempt.providerMessageId);
  assert.equal(networkFailure.correlationId, finalAttempt.correlationId);

  const retryAttempt = { ...requests[0], attempt: 2 };
  const secondRetry = executeForEachItem(ingestion, "Classify Retry Response", [{ statusCode: 503 }], {
    "Prepare Bounded Retry": [retryAttempt],
  })[0].json;
  assert.equal(secondRetry.outcome, "retrying");
  const thirdAttempt = executeForEachItem(ingestion, "Prepare Bounded Retry", [secondRetry])[0].json;
  assert.equal(thirdAttempt.attempt, 3);
  const retryExhaustion = executeForEachItem(ingestion, "Classify Retry Response", [{ statusCode: 503 }], {
    "Prepare Bounded Retry": [thirdAttempt],
  })[0].json;
  assert.equal(retryExhaustion.outcome, "retry-exhausted");
  assert.equal(retryExhaustion.retry, false);
  assert.equal(retryExhaustion.correlationId, retryAttempt.correlationId);

  const retryNetwork = executeForEachItem(ingestion, "Classify Retry Network Error", [{}], {
    "Prepare Bounded Retry": [retryAttempt],
  })[0].json;
  assert.equal(retryNetwork.outcome, "retrying-network-error");
  assert.equal(retryNetwork.attempt, 2);
  const thirdNetworkAttempt = executeForEachItem(ingestion, "Prepare Bounded Retry", [retryNetwork])[0].json;
  const retryNetworkExhaustion = executeForEachItem(ingestion, "Classify Retry Network Error", [{}], {
    "Prepare Bounded Retry": [thirdNetworkAttempt],
  })[0].json;
  assert.equal(retryNetworkExhaustion.outcome, "retry-exhausted");
  assert.equal(retryNetworkExhaustion.correlationId, retryAttempt.correlationId);
});

test("HTML-only content is converted to inert text and common entities are decoded", () => {
  const htmlOnly = { ...fixture, text: "", html: "<h1>Invoice</h1><script>leak()</script><p>A &amp; B&nbsp;&#x21;</p>" };
  const [mapped] = executeMapper([htmlOnly]);
  assert.equal(mapped.json.valid, true);
  assert.equal(mapped.json.payload.bodyText, "Invoice\nA & B !");
  assert.equal(mapped.json.payload.bodyText.includes("leak"), false);
});

test("missing bodies, malformed senders, oversized text, invalid headers, dates, and attachment counts are quarantined", () => {
  const cases = [
    [{ ...fixture, text: "", html: "" }, "message-body-empty"],
    [{ ...fixture, from: { text: "bad <not-an-email>" } }, "invalid-sender-address"],
    [{ ...fixture, text: "x".repeat(40001) }, "message-body-too-long"],
    [{ ...fixture, subject: "unsafe\r\nBcc: someone@example.test" }, "invalid-subject"],
    [{ ...fixture, date: "not-a-date" }, "invalid-received-at"],
    [{ ...fixture, attachments: Array.from({ length: 11 }, (_, index) => ({ attachmentId: String(index), filename: "x", contentType: "text/plain", size: 1 })) }, "too-many-attachments"],
  ];
  for (const [message, expectedReason] of cases) {
    const [result] = executeMapper([message]);
    assert.equal(result.json.valid, false);
    assert.equal(result.json.reason, expectedReason);
    assert.equal("payload" in result.json, false);
  }
});

test("ingestion retries only rate limits and server errors with a bounded exponential delay", () => {
  const source = ingestion.nodes.find(node => node.name === "Classify Gmail API Response").parameters.jsCode;
  assert.match(source, /status === 429 \|\| status >= 500/);
  assert.match(source, /request\.attempt < 3/);
  assert.match(source, /status === 409.*outcome = "conflict"/s);
  assert.match(source, /status === 401 \|\| status === 403/);
  const wait = ingestion.nodes.find(node => node.name === "Bounded Exponential Backoff");
  assert.equal(wait.parameters.unit, "seconds");
  assert.match(wait.parameters.amount, /Math\.pow\(2/);
  assert.match(ingestion.nodes.find(node => node.name === "Classify Ingestion Network Error").parameters.jsCode, /retry-exhausted/);
});

test("draft export is disabled by default and the only Gmail mutation node creates a draft", () => {
  const gmailNodes = exportWorkflow.nodes.filter(node => node.type === "n8n-nodes-base.gmail");
  assert.equal(gmailNodes.length, 1);
  assert.equal(gmailNodes[0].parameters.resource, "draft");
  assert.equal(gmailNodes[0].parameters.operation, "create");
  assert.equal(gmailNodes[0].parameters.emailType, "text");
  assert.equal(gmailNodes[0].parameters.subject, "={{$json.body.claim.subject}}");
  assert.equal(gmailNodes[0].parameters.message, "={{$json.body.claim.body}}");
  assert.equal(gmailNodes[0].parameters.options.sendTo, "={{$json.body.claim.recipientEmail}}");
  assert.equal(gmailNodes[0].retryOnFail, undefined);
  assert.match(exportWorkflow.nodes.find(node => node.name === "Require Explicit Fake or Live Mode").parameters.jsCode, /mode === "disabled"/);
  assert.equal(exportWorkflow.nodes.some(node => node.name === "Manual Synthetic Gmail Export Event"), true);
  assert.equal(exportWorkflow.connections["Simulate Ambiguous Fake Outcome?"].main[0][0].node, "Prepare Ambiguous Provider Outcome");
  assert.match(exportWorkflow.nodes.find(node => node.name === "Prepare Ambiguous Provider Outcome").parameters.jsCode, /ProviderOutcomeUnknown/);
  assert.match(exportWorkflow.nodes.find(node => node.name === "Report Claim Status Safely").parameters.jsCode, /claim-request-failed/);
  assert.equal(exportWorkflow.connections["Export Claim Available?"].main[1][0].node, "Report Claim Status Safely");
});

test("Smart Inbox service tokens use n8n-managed Bearer Auth credentials, not container environment variables", () => {
  const requests = [
    [ingestion, "Gmail API Ingestion"],
    [ingestion, "Retry Gmail API Ingestion"],
    [exportWorkflow, "Claim Gmail Export"],
    [exportWorkflow, "Record Gmail Draft ID"],
    [exportWorkflow, "Record Provider Outcome Unknown"],
  ];
  for (const [workflow, name] of requests) {
    const node = workflow.nodes.find(candidate => candidate.name === name);
    assert.equal(node.parameters.authentication, "genericCredentialType");
    assert.equal(node.parameters.genericAuthType, "httpBearerAuth");
    assert.equal(node.credentials, undefined);
    assert.equal(JSON.stringify(node.parameters).includes("Authorization"), false);
  }
  assert.equal(JSON.stringify([ingestion, exportWorkflow]).includes("SMARTINBOX_GMAIL_INGESTION_TOKEN"), false);
  assert.equal(JSON.stringify([ingestion, exportWorkflow]).includes("SMARTINBOX_GMAIL_DRAFT_EXPORT_TOKEN"), false);
});
