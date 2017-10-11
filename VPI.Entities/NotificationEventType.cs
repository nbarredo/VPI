namespace VPI.Entities
{
        public enum NotificationEventType
        {
            None = 0,
            JobStateChange = 1,
            NotificationEndPointRegistration = 2,
            NotificationEndPointUnregistration = 3,
            TaskStateChange = 4,
            TaskProgress = 5
        }
}
