const incoming = $input.all();

function htmlToText(html) {
  const entities = { amp: "&", lt: "<", gt: ">", quot: '"', apos: "'", nbsp: " " };
  return html
    .replace(/<script\b[^>]*>[\s\S]*?<\/script\s*>/gi, " ")
    .replace(/<style\b[^>]*>[\s\S]*?<\/style\s*>/gi, " ")
    .replace(/<!--([\s\S]*?)-->/g, " ")
    .replace(/<(br|\/p|\/div|\/li|\/tr|\/h[1-6])\b[^>]*>/gi, "\n")
    .replace(/<[^>]*>/g, " ")
    .replace(/&(#(?:x[\da-f]+|\d+)|[a-z]+);/gi, (entity, name) => {
      if (name[0] !== "#") return entities[name.toLowerCase()] ?? entity;
      const codePoint = name[1].toLowerCase() === "x"
        ? Number.parseInt(name.slice(2), 16)
        : Number.parseInt(name.slice(1), 10);
      return Number.isInteger(codePoint) && codePoint >= 0 && codePoint <= 0x10ffff
        ? String.fromCodePoint(codePoint)
        : " ";
    })
    .replace(/\r/g, "")
    .replace(/[\t ]+\n/g, "\n")
    .replace(/\n[\t ]+/g, "\n")
    .trim();
}

function stableCorrelationId(messageId) {
  const hash = seed => {
    let value = seed;
    for (let index = 0; index < messageId.length; index++) {
      value ^= messageId.charCodeAt(index);
      value = Math.imul(value, 16777619);
    }
    return (value >>> 0).toString(16).padStart(8, "0");
  };
  return `outlook-${hash(2166136261)}${hash(2246822519)}`;
}

function mapMessage(message) {
  const messageId = typeof message.id === "string" ? message.id.trim() : "";
  const invalid = reason => ({ json: { valid: false, reason, providerMessageId: messageId || null } });
  if (!messageId || messageId.length > 512) return invalid("invalid-outlook-message-id");

  const sender = message.from?.emailAddress ?? message.sender?.emailAddress;
  const senderEmail = typeof sender?.address === "string" && sender.address.trim() ? sender.address.trim() : null;
  const senderName = typeof sender?.name === "string" && sender.name.trim() ? sender.name.trim() : null;
  if (senderEmail && (!/^[^\s<>@]+@[^\s<>@]+\.[^\s<>@]+$/.test(senderEmail) || senderEmail.length > 320))
    return invalid("invalid-sender-address");
  if (senderName && senderName.length > 256) return invalid("sender-name-too-long");

  const subject = typeof message.subject === "string" ? message.subject : "";
  if (subject.length > 998 || /[\r\n]/.test(subject)) return invalid("invalid-subject");

  const content = typeof message.body === "string" ? message.body : message.body?.content;
  const contentType = String(message.body?.contentType ?? "text").toLowerCase();
  // Bound raw HTML before regex conversion, including messages with little visible text.
  if (typeof content === "string" && contentType === "html" && content.length > 40000)
    return invalid("message-html-too-long");
  const bodyText = typeof content !== "string" ? "" : contentType === "html" ? htmlToText(content) : content.trim();
  if (!bodyText.trim()) return invalid("message-body-empty");
  if (bodyText.length > 40000) return invalid("message-body-too-long");

  const receivedAt = message.receivedDateTime;
  const receivedDate = receivedAt instanceof Date ? receivedAt : new Date(receivedAt);
  if (!receivedAt || Number.isNaN(receivedDate.getTime())) return invalid("invalid-received-at");

  const conversationId = typeof message.conversationId === "string" && message.conversationId.length <= 512
    ? message.conversationId
    : null;
  const internetMessageId = typeof message.internetMessageId === "string" && message.internetMessageId.length <= 512 &&
      !/[\r\n]/.test(message.internetMessageId)
    ? message.internetMessageId
    : null;
  const recipientEmails = [message.toRecipients, message.ccRecipients, message.bccRecipients]
    .flatMap(recipients => Array.isArray(recipients) ? recipients : [])
    .flatMap(recipient => {
      const address = recipient?.emailAddress?.address;
      if (typeof address !== "string" || !address.trim()) return [];
      const normalized = address.trim();
      return /^[^\s<>@]+@[^\s<>@]+\.[^\s<>@]+$/.test(normalized) && normalized.length <= 320
        ? [normalized]
        : [{ invalid: true }];
    });
  if (recipientEmails.some(address => typeof address !== "string")) return invalid("invalid-recipient-address");
  if (recipientEmails.length > 100) return invalid("too-many-recipients");

  const rawAttachments = Array.isArray(message.attachments) ? message.attachments : [];
  if (rawAttachments.length > 10) return invalid("too-many-attachments");
  const attachments = rawAttachments.flatMap(attachment => {
    if (!attachment || typeof attachment !== "object") return [];
    const id = attachment.id;
    const name = attachment.name;
    const contentType = attachment.contentType;
    const sizeBytes = attachment.size;
    if (id == null || name == null || contentType == null || !Number.isSafeInteger(sizeBytes) || sizeBytes < 0)
      return [];
    return [{ externalAttachmentId: String(id), name: String(name), contentType: String(contentType), sizeBytes }];
  });

  const correlationId = stableCorrelationId(messageId);
  return {
    json: {
      valid: true,
      correlationId,
      payload: {
        graphMessageId: messageId,
        internetMessageId,
        conversationId,
        senderEmail,
        senderName,
        recipientEmails,
        subject,
        bodyText,
        receivedAt: receivedDate.toISOString(),
        attachments,
      },
    },
  };
}

return incoming.map((item, index) => ({ ...mapMessage(item.json ?? {}), pairedItem: { item: index } }));
