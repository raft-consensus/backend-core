namespace raft_backend.Database;

public static class StoredProcedureNames
{
    public const string Users_GetAll = "usp_Users_GetAll";
    public const string Users_GetById = "usp_Users_GetById";
    public const string Users_Create = "usp_Users_Create";
    public const string Users_Update = "usp_Users_Update";
    public const string Users_SoftDelete = "usp_Users_SoftDelete";
    public const string Users_UpsertFromOAuth = "usp_Users_UpsertFromOAuth";
    public const string Users_RegisterWithPassword = "usp_Users_RegisterWithPassword";
    public const string Users_GetByEmailForLogin = "usp_Users_GetByEmailForLogin";
    public const string Users_GetSharedSqlServerProvisioningState = "usp_Users_GetSharedSqlServerProvisioningState";

    public const string DatabaseInstances_GetAll = "usp_DatabaseInstances_GetAll";
    public const string DatabaseInstances_GetById = "usp_DatabaseInstances_GetById";
    public const string DatabaseInstances_Create = "usp_DatabaseInstances_Create";
    public const string DatabaseInstances_Update = "usp_DatabaseInstances_Update";
    public const string DatabaseInstances_SoftDelete = "usp_DatabaseInstances_SoftDelete";

    public const string AccessCredentials_GetAll = "usp_AccessCredentials_GetAll";
    public const string AccessCredentials_GetById = "usp_AccessCredentials_GetById";
    public const string AccessCredentials_GetByDatabaseInstanceId = "usp_AccessCredentials_GetByDatabaseInstanceId";
    public const string AccessCredentials_Create = "usp_AccessCredentials_Create";
    public const string AccessCredentials_Update = "usp_AccessCredentials_Update";
    public const string AccessCredentials_SoftDelete = "usp_AccessCredentials_SoftDelete";
    public const string AccessCredentials_GetDecryptableByOwner = "usp_AccessCredentials_GetDecryptableByOwner";

    public const string AuditEvents_GetAll = "usp_AuditEvents_GetAll";
    public const string AuditEvents_GetById = "usp_AuditEvents_GetById";
    public const string AuditEvents_Create = "usp_AuditEvents_Create";
    public const string AuditEvents_Update = "usp_AuditEvents_Update";
    public const string AuditEvents_SoftDelete = "usp_AuditEvents_SoftDelete";

    public const string PlatformMetrics_Get = "usp_PlatformMetrics_Get";
    public const string UserDashboard_GetByUserId = "usp_UserDashboard_GetByUserId";

    public const string DatabaseInstances_UpdateStatus = "usp_DatabaseInstances_UpdateStatus";
    public const string DatabaseInstances_GetDueForPause = "usp_DatabaseInstances_GetDueForPause";
    public const string DatabaseInstances_GetDueForDelete = "usp_DatabaseInstances_GetDueForDelete";
    public const string DatabaseInstances_UpdateUsedSpace = "usp_DatabaseInstances_UpdateUsedSpace";
    public const string DatabaseInstances_TouchActivityByDatabaseName = "usp_DatabaseInstances_TouchActivityByDatabaseName";
    public const string DatabaseInstances_GetSharedLoginCleanupState = "usp_DatabaseInstances_GetSharedLoginCleanupState";
}
