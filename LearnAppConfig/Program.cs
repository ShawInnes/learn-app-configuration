using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.FeatureFilters;
using Serilog;
using Serilog.Events;

namespace LearnAppConfig
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var appConfigurationName = "cloudscale-app-config";
            var sentinalKey = "LearnAppConfig:Sentinel";

            IConfigurationRefresher configurationRefresher = null;

            var credential = new DefaultAzureCredential();

            IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.development.json", optional: true, reloadOnChange: true)
                .AddAzureAppConfiguration(
                    p =>
                    {
                        if (!string.IsNullOrEmpty(appConfigurationName))
                        {
                            p.Connect(new Uri($"https://{appConfigurationName}.azconfig.io"), credential)
                                .ConfigureKeyVault(kv => { kv.SetCredential(credential); })
                                .ConfigureRefresh(refresh =>
                                {
                                    refresh.Register(sentinalKey, refreshAll: true)
                                        .SetCacheExpiration(new TimeSpan(0, 0, 30));
                                })
                                .UseFeatureFlags();

                            configurationRefresher = p.GetRefresher();
                        }
                    },
                    optional: true)
                .AddUserSecrets<Program>(optional: true)
                .AddEnvironmentVariables()
                .AddCommandLine(args);

            IConfiguration configuration = configurationBuilder.Build();

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .ReadFrom.Configuration(configuration)
                .WriteTo.LiterateConsole()
                .CreateLogger();

            var serviceBuilder = new ServiceCollection();

            serviceBuilder
                .AddOptions()
                .AddOptions<LearnAppConfigOptions>()
                .Bind(configuration.GetSection(LearnAppConfigOptions.SectionName))
                .ValidateDataAnnotations();

            serviceBuilder.AddTransient<IConfiguration>(p => configuration);
            serviceBuilder.AddLogging(builder => builder.AddSerilog(dispose: false));
            serviceBuilder.AddSingleton<IConfigurationRefresher>(configurationRefresher);
            serviceBuilder.AddAzureAppConfiguration();
            serviceBuilder.AddFeatureManagement().AddFeatureFilter<PercentageFilter>();

            var serviceProvider = serviceBuilder.BuildServiceProvider();

            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            var options = serviceProvider.GetRequiredService<IOptionsMonitor<LearnAppConfigOptions>>();
            var refresher = serviceProvider.GetRequiredService<IConfigurationRefresher>();
            var featureManager = serviceProvider.GetRequiredService<IFeatureManager>();

            for (int i = 0; i < 1000; i++)
            {
                await refresher.RefreshAsync();

                logger.LogInformation("Options SampleString = {Value}", options.CurrentValue.SampleString);
                logger.LogInformation("Options SampleSecret = {Value}", options.CurrentValue.SampleSecret);

                if (await featureManager.IsEnabledAsync(nameof(LearnAppConfigFeatureFlags.FeatureA)))
                    logger.LogInformation("Feature A is Enabled");

                if (await featureManager.IsEnabledAsync(nameof(LearnAppConfigFeatureFlags.FeatureB)))
                    logger.LogInformation("Feature B is Enabled");

                if (await featureManager.IsEnabledAsync(nameof(LearnAppConfigFeatureFlags.FeatureC)))
                    logger.LogInformation("Feature C is Enabled");

                Thread.Sleep(5000);
            }

            Log.CloseAndFlush();
        }
    }
}