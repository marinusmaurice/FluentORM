namespace FluentORM.Core.Abstractions;

public interface ITenantContextProvider
{
    string? GetCurrentTenantId();
}

public interface ICurrentUserProvider
{
    string? GetCurrentUserId();
    string? GetCurrentIpAddress();
}

public interface IAdminContext { }
