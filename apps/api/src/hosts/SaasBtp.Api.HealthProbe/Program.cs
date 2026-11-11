using SaasBtp.Api.HealthProbe;

// Docker's HEALTHCHECK reads 0 as healthy and 1 as unhealthy, and reserves 2: RunGuardedAsync
// turns an exception the probe does not expect into 1 as well.
return await ReadinessProbe.RunGuardedAsync(
    () => ReadinessProbe.RunAsync(Environment.GetEnvironmentVariables(), Console.Error, ReadinessProbe.DefaultTimeout),
    Console.Error);
