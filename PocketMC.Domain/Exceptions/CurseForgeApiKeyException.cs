using System;

namespace PocketMC.Domain.Exceptions
{
    public sealed class CurseForgeApiKeyException : MarketplaceException
    {
        public bool IsMissingKey { get; }

        public CurseForgeApiKeyException(string message, bool isMissingKey)
            : base(message, isMissingKey ? "CURSEFORGE_API_KEY_MISSING" : "CURSEFORGE_API_KEY_INVALID")
        {
            IsMissingKey = isMissingKey;
        }

        public CurseForgeApiKeyException(string message, bool isMissingKey, Exception innerException)
            : base(message, innerException, isMissingKey ? "CURSEFORGE_API_KEY_MISSING" : "CURSEFORGE_API_KEY_INVALID")
        {
            IsMissingKey = isMissingKey;
        }
    }
}
