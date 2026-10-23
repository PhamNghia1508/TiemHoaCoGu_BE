using HoaSap.Domain;

namespace HoaSap.BLL.Notifications;

public interface INotificationOutboxWriter
{
    void Enqueue(NotificationOutbox message);
}

public sealed record NotificationResult(Guid MessageId, NotificationStatus Status);
