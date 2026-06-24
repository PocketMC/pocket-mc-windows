using System;

namespace PocketMC.Domain.Exceptions
{
    public class PocketMCException : Exception
    {
        public string ErrorCode { get; }

        public PocketMCException(string message, string errorCode = "UNKNOWN") : base(message)
        {
            ErrorCode = errorCode;
        }

        public PocketMCException(string message, Exception innerException, string errorCode = "UNKNOWN") : base(message, innerException)
        {
            ErrorCode = errorCode;
        }
    }
}
