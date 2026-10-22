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

function mapMessage(message) {
  const messageId = typeof message.id === "string" ? message.id.trim() : "";
  const invalid = reason => ({ json: { valid: false, reason, providerMessageId: messageId || null } });
  if (!messageId || messageId.length > 512) return invalid("invalid-gmail-message-id");

  const from = message.from?.value?.[0];
  const fromText = typeof message.from?.text === "string"
    ? message.from.text
    : typeof message.headers?.from === "string"
      ? message.headers.from.replace(/^from:\s*/i, "")
      : "";
  const senderEmail = typeof from?.address === "string"
    ? from.address.trim()
    : fromText.match(/<([^<>]+)>/)?.[1]?.trim() ?? (fromText.includes("@") ? fromText.trim() : null);
  const senderName = typeof from?.name === "string" && from.name.trim()
    ? from.name.trim()
    : null;
  if (senderEmail && (!/^[^\s<>@]+@[^\s<>@]+\.[^\s<>@]+$/.test(senderEmail) || senderEmail.length > 320))
    return invalid("invalid-sender-address");
  if (senderName && senderName.length > 256) return invalid("sender-name-too-long");

  const subject = typeof message.subject === "string" ? message.subject : "";
  if (subject.length > 998 || /[\r\n]/.test(subject)) return invalid("invalid-subject");
  const bodyText = typeof message.text === "string" && message.text.trim()
    ? message.text
    : typeof message.html === "string"
      ? htmlToText(message.html)
      : "";
  if (!bodyText.trim()) return invalid("message-body-empty");
  if (bodyText.length > 40000) return invalid("message-body-too-long");

  const receivedAt = message.date ?? message.internalDate;
  const receivedDate = receivedAt instanceof Date ? receivedAt : new Date(receivedAt);
  if (!receivedAt || Number.isNaN(receivedDate.getTime())) return invalid("invalid-received-at");

  const rawAttachments = Array.isArray(message.attachments) ? message.attachments : [];
  if (rawAttachments.length > 10) return invalid("too-many-attachments");
  const attachments = rawAttachments.flatMap(attachment => {
    if (!attachment || typeof attachment !== "object") return [];
    const id = attachment.attachmentId ?? attachment.partId ?? attachment.id;
    const name = attachment.filename ?? attachment.name;
    const contentType = attachment.contentType ?? attachment.mimeType;
    const sizeBytes = attachment.sizeBytes ?? attachment.size;
    if (id == null || name == null || contentType == null || !Number.isSafeInteger(sizeBytes) || sizeBytes < 0)
      return [];
    return [{ externalAttachmentId: String(id), name: String(name), contentType: String(contentType), sizeBytes }];
  });

  const threadId = typeof message.threadId === "string" && message.threadId.length <= 512
    ? message.threadId
    : null;
  const correlationId = `gmail-${messageId}`;
  return {
    json: {
      valid: true,
      correlationId,
      payload: {
        gmailMessageId: messageId,
        gmailThreadId: threadId,
        senderEmail,
        senderName,
        subject,
        bodyText,
        receivedAt: receivedDate.toISOString(),
        attachments,
      },
    },
  };
}

return incoming.map((item, index) => ({ ...mapMessage(item.json ?? {}), pairedItem: { item: index } }));
