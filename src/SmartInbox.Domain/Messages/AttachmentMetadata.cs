namespace SmartInbox.Domain.Messages;

public sealed record AttachmentMetadata
{
    public string ExternalAttachmentId { get; }
    public string Name { get; }
    public string ContentType { get; }
    public long SizeBytes { get; }

    public AttachmentMetadata(
        string externalAttachmentId,
        string name,
        string contentType,
        long sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(externalAttachmentId) ||
            externalAttachmentId.Length > InboundMessageLimits.ExternalId)
            throw new ArgumentException("A valid external attachment ID is required.", nameof(externalAttachmentId));
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > InboundMessageLimits.AttachmentName)
            throw new ArgumentException("A valid attachment name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(contentType) || contentType.Trim().Length > InboundMessageLimits.ContentType)
            throw new ArgumentException("A valid attachment content type is required.", nameof(contentType));
        if (sizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(sizeBytes));

        ExternalAttachmentId = externalAttachmentId;
        Name = name.Trim();
        ContentType = contentType.Trim().ToLowerInvariant();
        SizeBytes = sizeBytes;
    }
}
