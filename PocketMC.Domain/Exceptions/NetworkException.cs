using System;

namespace PocketMC.Domain.Exceptions
{
    public class NetworkException : PocketMCException
    {
        public NetworkException(string message, string errorCode = "NETWORK_ERROR") : base(message, errorCode)
        {
        }

        public NetworkException(string message, Exception innerException, string errorCode = "NETWORK_ERROR") : base(message, innerException, errorCode)
        {
        }
    }
}
