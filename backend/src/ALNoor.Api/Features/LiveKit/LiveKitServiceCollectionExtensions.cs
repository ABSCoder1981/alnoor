namespace ALNoor.Api.Features.LiveKit;

internal static class LiveKitServiceCollectionExtensions
{
    /// <summary>Registers LiveKit settings (validated at startup, so misconfiguration fails fast) and the token service.</summary>
    public static IServiceCollection AddLiveKit(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LiveKitOptions>()
            .Bind(configuration.GetSection(LiveKitOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                o => o.Url is { IsAbsoluteUri: true } url && (url.Scheme == "ws" || url.Scheme == "wss"),
                "LiveKit:Url must be an absolute ws:// or wss:// URL.")
            .ValidateOnStart();

        services.AddSingleton<LiveKitTokenService>();
        services.AddHttpClient<ILiveKitRoomService, LiveKitRoomService>(client => client.Timeout = TimeSpan.FromSeconds(10));
        return services;
    }
}
