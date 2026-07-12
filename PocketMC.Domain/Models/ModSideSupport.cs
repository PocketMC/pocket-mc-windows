namespace PocketMC.Domain.Models
{
    public enum ModSideSupport
    {
        Unknown,
        ClientOnly,
        ServerOnly,
        ClientAndServer,
        OptionalOnServer,
        OptionalOnClient
    }
}
