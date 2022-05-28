namespace DNSUpdater.Library.Services;

public interface IDnsServiceFactory
{
    IDnsService GetDnsService();
}