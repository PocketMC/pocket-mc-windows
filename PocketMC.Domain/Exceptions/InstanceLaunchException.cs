using System;

namespace PocketMC.Domain.Exceptions
{
    public class InstanceLaunchException : PocketMCException
    {
        public InstanceLaunchException(string message, string errorCode = "INSTANCE_LAUNCH_FAILED") : base(message, errorCode)
        {
        }

        public InstanceLaunchException(string message, Exception innerException, string errorCode = "INSTANCE_LAUNCH_FAILED") : base(message, innerException, errorCode)
        {
        }
    }
}
