namespace ALNoor.Api.Shared.Data;

/// <summary>Database settings; validated at startup so a missing connection string fails fast.</summary>
public sealed class DatabaseOptions
{
    public string? ConnectionString { get; set; }
}
