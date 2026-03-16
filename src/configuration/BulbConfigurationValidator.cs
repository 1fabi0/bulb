using Microsoft.Extensions.Options;

namespace Bulb.Configuration;

internal static class BulbConfigurationValidator
{
    internal static BulbConfiguration GetBulbConfiguration(IConfiguration configuration)
    {
        bool? isServiceStatusUpdateEnabled = configuration.GetValue<bool?>($"BulbConfiguration:IsServiceStatusUpdateEnabled");
        string? backendSystem = configuration.GetValue<string?>($"BulbConfiguration:BackendSystem");
        bool? isEndpointRoutingEnabled = configuration.GetValue<bool?>($"BulbConfiguration:IsEndpointRoutingEnabled");
        string? nodeName = configuration.GetValue<string?>($"BulbConfiguration:NodeName");
        string? defaultScope = configuration.GetValue<string?>($"BulbConfiguration:DefaultScope");

        var validationResult = Validate(isServiceStatusUpdateEnabled, backendSystem, isEndpointRoutingEnabled, nodeName);
        if (!validationResult.Succeeded)
        {
            throw new OptionsValidationException(nameof(BulbConfiguration), typeof(BulbConfiguration), validationResult.Failures);
        }

        var bulbConfig = new BulbConfiguration(
            isServiceStatusUpdateEnabled: isServiceStatusUpdateEnabled!.Value,
            backendSystem: backendSystem!,
            isEndpointRoutingEnabled: isEndpointRoutingEnabled!.Value,
            nodeName: nodeName!,
            defaultScope: defaultScope
        );

        return bulbConfig;
    }

    private static ValidateOptionsResult Validate(bool? isServiceStatusUpdateEnabled, string? backendSystem, bool? isEndpointRoutingEnabled, string? nodeName)
    {
        var failures = new List<string>();

        if (isServiceStatusUpdateEnabled == null)
        {
            failures.Add($"Missing required configuration: IsServiceStatusUpdateEnabled");
        }

        if (backendSystem == null)
        {
            failures.Add($"Missing required configuration: BackendSystem");
        }
        else if (backendSystem != "ipvs" && backendSystem != "iptables")
        {
            failures.Add($"Invalid value for BackendSystem. Allowed values are 'ipvs' or 'iptables'.");
        }

        if (nodeName == null)
        {
            failures.Add($"Missing required configuration: NodeName");
        }

        if (isEndpointRoutingEnabled == null)
        {
            failures.Add($"Missing required configuration: IsEndpointRoutingEnabled");
        }

        if (failures.Count == 0)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(failures);
    }
}
