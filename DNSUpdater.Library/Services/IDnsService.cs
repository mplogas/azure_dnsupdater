namespace DNSUpdater.Library.Services;

public interface IDnsService
{
    Task<bool> IsKnown(string fqdn);
    Task<UpdateStatus> Update(string fqdn, string ip);
}