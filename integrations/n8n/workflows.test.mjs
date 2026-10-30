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
const [outlookIngestion, outlookExport, outlookFixture, outlookHtmlFixture, outlookMissingSenderFixture, outlookMapperSource, outlookErrors] = await Promise.all([
  readJson("outlook-ingestion.json"),
  readJson("outlook-approved-draft-export.json"),
  readJson("fixtures/outlook-message.json"),
  readJson("fixtures/outlook-message-html.json"),
  readJson("fixtures/outlook-message-missing-sender.json"),
  readFile(new URL("scripts/outlook-message-mapper.js", integrationRoot), "utf8"),
  readJson("fixtures/outlook-graph-errors.json"),
]);

const normalizeSourceNewlines = source => source.replace(/\r\n?/g, "\n").trim();

test("the public deployment keeps the optional n8n runtime unavailable", async () => {
  const compose = await readFile(new URL("../../docker-compose.yml", integrationRoot), "utf8");
  assert.match(compose, /^  postgres:/m);
  assert.doesNotMatch(compose, /^\s*n8n\s*:/m);
  assert.doesNotMatch(compose, /n8nio\/n8n|smartinbox-n8n|N8N_/);
});

test("committed API and integration settings keep provider actions disabled", async () => {
  const settings = await readJson("../../src/SmartInbox.API/appsettings.json");
  assert.equal(settings.EmailProcessing.Enabled, false);
  assert.equal(settings.EmailDrafting.Enabled, false);
  for (const provider of [settings.GmailIntegration, settings.OutlookIntegration]) {
    assert.equal(provider.Enabled, false);
    assert.equal(provider.MailboxId, "");
    assert.equal(provider.IngestionToken, "");
    assert.equal(provider.DraftExportToken, "");
  }
  assert.equal(settings.OpenAI.ApiKey, undefined);
  const environment = await readFile(new URL("../../.env.example", integrationRoot), "utf8");
  assert.match(environment, /^SMARTINBOX_DB_PASSWORD=\r?$/m);
  assert.doesNotMatch(environment, /^SMARTINBOX_(?:GMAIL|OUTLOOK|API_ORIGIN)/m);
});

function executeSyntheticFixture() {
  const source = ingestion.nodes.find(node => node.name === "Synthetic Parsed Gmail Fixture").parameters.jsCode;
  return new Function("$input", source)({ all: () => [] });
}

function executeMapper(messages, sourceForParity = mapperSource) {
  const mapper = ingestion.nodes.find(node => node.name === "Map and Validate Gmail Message").parameters.jsCode;
  assert.equal(normalizeSourceNewlines(mapper), normalizeSourceNewlines(sourceForParity));
  return new Function("$input", mapper)({ all: () => messages.map(json => ({ json })) });
}

function executeOutlookMapper(messages, sourceForParity = outlookMapperSource) {
  const mapper = outlookIngestion.nodes.find(node => node.name === "Map and Validate Outlook Message").parameters.jsCode;
  assert.equal(normalizeSourceNewlines(mapper), normalizeSourceNewlines(sourceForParity));
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

test("both mappers bound raw HTML before conversion and retain the text limit", () => {
  const rawHtmlAtLimit = `<p>ok</p>${" ".repeat(40000 - 9)}`;
  const htmlCases = [
    { body: rawHtmlAtLimit, valid: true },
    { body: `${rawHtmlAtLimit} `, reason: "message-html-too-long" },
    { body: "<".repeat(40001), reason: "message-html-too-long" },
  ];
  for (const { body, valid = false, reason } of htmlCases) {
    const gmail = executeMapper([{ ...fixture, text: "", html: body }])[0].json;
    const outlook = executeOutlookMapper([{ ...outlookFixture, body: { contentType: "html", content: body } }])[0].json;
    for (const result of [gmail, outlook]) {
      assert.equal(result.valid, valid);
      if (valid) assert.equal(result.payload.bodyText, "ok");
      else assert.equal(result.reason, reason);
    }
  }
  assert.equal(executeMapper([{ ...fixture, text: "a".repeat(40001) }])[0].json.reason, "message-body-too-long");
  assert.equal(executeOutlookMapper([{ ...outlookFixture, body: { contentType: "text", content: "a".repeat(40001) } }])[0].json.reason, "message-body-too-long");
});

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

test("mapper source parity accepts CRLF and LF while detecting code changes", () => {
  const mapper = ingestion.nodes.find(node => node.name === "Map and Validate Gmail Message").parameters.jsCode;
  const crlfMapper = mapper.replace(/\n/g, "\r\n");
  assert.doesNotThrow(() => executeMapper([], crlfMapper));
  const changedMapper = crlfMapper.replace("const correlationId =", "const changedCorrelationId =");
  assert.throws(() => executeMapper([], changedMapper), error => error.code === "ERR_ASSERTION");
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

test("Outlook Graph workflows are inactive, credential-free exports with no send operation", () => {
  for (const workflow of [outlookIngestion, outlookExport]) {
    assert.equal(workflow.active, false);
    assert.ok(workflow.nodes.length > 0);
    const names = new Set(workflow.nodes.map(node => node.name));
    assert.equal(names.size, workflow.nodes.length);
    for (const [sourceName, outputs] of Object.entries(workflow.connections)) {
      assert.ok(names.has(sourceName), `Unknown connection source: ${sourceName}`);
      for (const branches of Object.values(outputs)) for (const branch of branches) for (const edge of branch)
        assert.ok(names.has(edge.node), `Unknown connection target: ${edge.node}`);
    }
    assert.doesNotMatch(JSON.stringify(workflow), /[\w.+-]+@(?:outlook|hotmail|live)\.com\b/i);
    assert.equal(JSON.stringify(workflow).includes("localhost"), false);
    for (const node of workflow.nodes) assert.equal(node.credentials, undefined);
  }
  const serialized = JSON.stringify([outlookIngestion, outlookExport]);
  assert.doesNotMatch(serialized, /Mail\.Send|sendMail|send message|n8n-nodes-base\.microsoftOutlook/i);
  assert.equal(outlookExport.nodes.filter(node => node.type === "n8n-nodes-base.httpRequest" && node.parameters.method === "POST" && node.parameters.url === "https://graph.microsoft.com/v1.0/me/messages").length, 1);
  assert.equal(outlookExport.nodes.find(node => node.name === "Create Outlook Draft").retryOnFail, undefined);
});

test("Outlook polling is limited to a configured test folder and cutoff, verifies /me, requests immutable text IDs, and paginates Graph next links", () => {
  const schedule = outlookIngestion.nodes.find(node => node.name === "Poll Outlook Test Folder");
  const scope = outlookIngestion.nodes.find(node => node.name === "Require Outlook Test Scope");
  const identity = outlookIngestion.nodes.find(node => node.name === "Verify Outlook Mailbox");
  const list = outlookIngestion.nodes.find(node => node.name === "List Outlook Test Folder Messages");
  assert.equal(schedule.type, "n8n-nodes-base.scheduleTrigger");
  assert.match(scope.parameters.jsCode, /SMARTINBOX_OUTLOOK_TEST_FOLDER_ID/);
  assert.match(scope.parameters.jsCode, /SMARTINBOX_OUTLOOK_RECEIVED_AFTER/);
  assert.match(scope.parameters.jsCode, /SMARTINBOX_OUTLOOK_MODE.*disabled/);
  assert.match(scope.parameters.jsCode, /reservedFolders/);
  assert.equal(identity.parameters.url, "https://graph.microsoft.com/v1.0/me?$select=mail,userPrincipalName");
  assert.equal(outlookIngestion.connections["Confirm Outlook Mailbox"].main[0][0].node, "Outlook Mailbox Matches?");
  assert.equal(outlookIngestion.connections["Outlook Mailbox Matches?"].main[0][0].node, "Build Outlook Graph Poll Request");
  assert.equal(outlookIngestion.connections["Outlook Mailbox Matches?"].main[1][0].node, "Safe Outlook Graph Poll Diagnostic");
  assert.match(list.parameters.url, /SMARTINBOX_OUTLOOK_TEST_FOLDER_ID/);
  assert.match(list.parameters.url, /SMARTINBOX_OUTLOOK_RECEIVED_AFTER/);
  assert.match(list.parameters.headerParameters.parameters[0].value, /IdType="ImmutableId"/);
  assert.match(list.parameters.headerParameters.parameters[0].value, /outlook\.body-content-type="text"/);
  assert.equal(list.parameters.options.pagination.pagination.paginationMode, "responseContainsNextURL");
  assert.match(list.parameters.options.pagination.pagination.nextURL, /@odata\.nextLink/);
  assert.match(list.parameters.options.pagination.pagination.completeExpression, /@odata\.nextLink/);
  assert.equal(list.parameters.options.response.response.neverError, true);
  assert.equal(outlookIngestion.nodes.some(node => /download.*attachment|mark.*read|move.*message|delete.*message/i.test(node.name)), false);
});

test("Outlook mapper preserves per-message Graph identity, internet ID, conversation and metadata across multi-item and duplicate deliveries", () => {
  const differentInConversation = { ...outlookFixture, id: "AAMkAGI2-test-message-002", internetMessageId: "<synthetic-outlook-002@example.test>" };
  const mapped = executeOutlookMapper([outlookFixture, differentInConversation, outlookFixture]);
  assert.equal(mapped.length, 3);
  assert.deepEqual(mapped.map(item => item.json.valid), [true, true, true]);
  assert.deepEqual(mapped.map(item => item.json.payload.graphMessageId), [outlookFixture.id, differentInConversation.id, outlookFixture.id]);
  assert.equal(mapped[0].json.payload.conversationId, mapped[1].json.payload.conversationId);
  assert.notEqual(mapped[0].json.payload.graphMessageId, mapped[1].json.payload.graphMessageId);
  assert.equal(mapped[0].json.payload.internetMessageId, outlookFixture.internetMessageId);
  assert.equal(mapped[0].json.payload.recipientEmails.length, 2);
  assert.equal(mapped[0].json.payload.attachments[0].externalAttachmentId, "synthetic-attachment-001");
  assert.equal(mapped[0].json.correlationId, mapped[2].json.correlationId);
  assert.notEqual(mapped[0].json.correlationId, mapped[1].json.correlationId);
});

test("Outlook mapper converts HTML to inert text, tolerates a missing sender, and quarantines malformed or oversized content", () => {
  const [html] = executeOutlookMapper([outlookHtmlFixture]);
  assert.equal(html.json.valid, true);
  assert.equal(html.json.payload.bodyText, "Invoice\nA & B !");
  assert.doesNotMatch(html.json.payload.bodyText, /doNotRun/);
  const [missingSender] = executeOutlookMapper([outlookMissingSenderFixture]);
  assert.equal(missingSender.json.valid, true);
  assert.equal(missingSender.json.payload.senderEmail, null);

  const cases = [
    [{ ...outlookFixture, id: "" }, "invalid-outlook-message-id"],
    [{ ...outlookFixture, subject: "unsafe\r\nBcc: victim@example.test" }, "invalid-subject"],
    [{ ...outlookFixture, receivedDateTime: "not-a-date" }, "invalid-received-at"],
    [{ ...outlookFixture, body: { contentType: "text", content: "x".repeat(40001) } }, "message-body-too-long"],
    [{ ...outlookFixture, from: { emailAddress: { address: "invalid" } } }, "invalid-sender-address"],
    [{ ...outlookFixture, toRecipients: Array.from({ length: 101 }, () => ({ emailAddress: { address: "a@example.test" } })) }, "too-many-recipients"],
    [{ ...outlookFixture, attachments: Array.from({ length: 11 }, (_, index) => ({ id: String(index), name: "file", contentType: "text/plain", size: 1 })) }, "too-many-attachments"],
  ];
  for (const [message, expectedReason] of cases) {
    const [result] = executeOutlookMapper([message]);
    assert.equal(result.json.valid, false);
    assert.equal(result.json.reason, expectedReason);
    assert.equal("payload" in result.json, false);
  }
});

test("Outlook Graph status handling respects Retry-After, bounds retries, and does not retry authentication failures", () => {
  const classify = outlookIngestion.nodes.find(node => node.name === "Classify Outlook Graph Poll Response").parameters.jsCode;
  const run = (response, attempt = 1) => new Function("$input", "$", classify)(
    { all: () => [{ json: response }] },
    () => ({ first: () => ({ json: { attempt } }) }),
  )[0].json;
  const throttled = run({ statusCode: 429, headers: { "retry-after": "30" }, body: { error: { code: "TooManyRequests" } } });
  assert.equal(throttled.retry, true);
  assert.equal(throttled.retryAfterSeconds, 30);
  assert.equal(run({ statusCode: 503, headers: {}, body: {} }, 3).retry, false);
  assert.equal(run({ statusCode: 401, headers: {}, body: {} }).retry, false);
  assert.deepEqual(outlookErrors.map(error => error.statusCode), [401, 403, 429, 500, 504]);
});

test("Outlook draft creation uses the claimed approved snapshot and only accepts a confirmed Graph draft response", () => {
  const create = outlookExport.nodes.find(node => node.name === "Create Outlook Draft");
  assert.equal(create.parameters.method, "POST");
  assert.equal(create.parameters.url, "https://graph.microsoft.com/v1.0/me/messages");
  assert.match(create.parameters.jsonBody, /body\.claim\.subject/);
  assert.match(create.parameters.jsonBody, /body\.claim\.body/);
  assert.match(create.parameters.jsonBody, /body\.claim\.recipientEmail/);
  assert.equal(create.parameters.headerParameters.parameters[0].value, 'IdType="ImmutableId"');
  assert.equal(create.retryOnFail, undefined);
  const validation = outlookExport.nodes.find(node => node.name === "Validate Outlook Draft Result").parameters.jsCode;
  assert.match(validation, /statusCode[^\n]*201/);
  assert.match(validation, /body\.isDraft === true/);
  assert.match(validation, /body\.id/);
  assert.equal(outlookExport.connections["Create Outlook Draft"].main[1][0].node, "Prepare Ambiguous Outlook Provider Outcome");
  assert.equal(outlookExport.connections["Outlook Draft Created?"].main[1][0].node, "Prepare Ambiguous Outlook Provider Outcome");
  assert.match(outlookExport.nodes.find(node => node.name === "Prepare Ambiguous Outlook Provider Outcome").parameters.jsCode, /ProviderOutcomeUnknown/);
  assert.match(outlookExport.nodes.find(node => node.name === "Require Explicit Outlook Export Mode").parameters.jsCode, /disabled/);
  assert.equal(outlookExport.nodes.find(node => node.name === "Create Outlook Draft").parameters.options.response.response.neverError, true);
});

test("Outlook live export verifies the Graph identity before claiming and preserves provider-specific callbacks", () => {
  assert.equal(outlookExport.connections["Outlook Export Identity Matches?"].main[0][0].node, "Claim Outlook Export");
  assert.equal(outlookExport.connections["Outlook Export Identity Matches?"].main[1][0].node, "Safe Outlook Export Identity Diagnostic");
  assert.match(outlookExport.nodes.find(node => node.name === "Confirm Outlook Export Identity").parameters.jsCode, /SMARTINBOX_OUTLOOK_MAILBOX_ID/);
  assert.match(outlookExport.nodes.find(node => node.name === "Claim Outlook Export").parameters.url, /integrations\/outlook\/draft-exports\/claim/);
  assert.match(outlookExport.nodes.find(node => node.name === "Record Outlook Draft ID").parameters.jsonBody, /externalDraftId/);
  assert.match(outlookExport.nodes.find(node => node.name === "Record Outlook Outcome Unknown").parameters.url, /integrations\/outlook\/draft-exports/);
  assert.match(outlookExport.nodes.find(node => node.name === "Record Outlook Draft ID").parameters.url, /\/complete/);
  assert.match(outlookExport.nodes.find(node => node.name === "Record Outlook Outcome Unknown").parameters.url, /\/fail/);
});
