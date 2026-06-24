using System;

namespace PocketMC.Domain.Exceptions
{
    public class MarketplaceException : PocketMCException
    {
        public MarketplaceException(string message, string errorCode = "MARKETPLACE_ERROR") : base(message, errorCode)
        {
        }

        public MarketplaceException(string message, Exception innerException, string errorCode = "MARKETPLACE_ERROR") : base(message, innerException, errorCode)
        {
        }
    }
}
