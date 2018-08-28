namespace UpdateDonkeybot
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Azure.Management.ContainerInstance.Fluent.Models;
    using Microsoft.Azure.Management.Fluent;
    using Microsoft.Azure.Management.ResourceManager.Fluent;
    using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
    using Microsoft.Azure.WebJobs;
    using Microsoft.Azure.WebJobs.Extensions.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using CancellationToken = System.Threading.CancellationToken;

    [StorageAccount("AzureWebJobsStorage")]
    public static class DockerHubWebhook
    {
        private const string MessageQueue = "update-donkeybot-jobs";

        private const string ResourceGroup = "Donkeybot";

        private const string ContainerGroup = "donkeybot";

        private const string Image = "perrym5/donkeybot";

        [FunctionName("DockerHubWebhook")]
        public static async Task<IActionResult> WebhookHandlerAsync(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequest req,
            [Queue(MessageQueue)] IAsyncCollector<string> outputQueueItem,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            await outputQueueItem.AddAsync("update");
            return new OkResult();
        }

        [FunctionName("UpdateDonkeybotJob")]
        public static async Task QueueMessageHandlerAsync(
            [QueueTrigger(MessageQueue)] string message,
            ILogger log,
            ExecutionContext context,
            CancellationToken token)
        {
            log.LogInformation($"C# Queue trigger function processed: {message}");

            var config = BuildConfig(context.FunctionAppDirectory);
            var azure = AuthenticateToAzure(config, log);
            var resourceGroup = await azure.ResourceGroups.GetByNameAsync(ResourceGroup, token);

            var env = config["HUBOT_ENV_KEYS"]
                .Split(',')
                .ToDictionary(k => k, k => config[k]);

            // Add a timestamp env var to make each deployment different enough for a restart
            env["TIMESTAMP"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            log.LogInformation("Creating container instance");
            await azure.CreateDonkeybotInstance(env, resourceGroup, token);
            log.LogInformation("Successfully created container instance");
        }

        private static Task CreateDonkeybotInstance(
            this IAzure azure,
            IDictionary<string, string> env,
            IResourceGroup group,
            CancellationToken token) => azure.ContainerGroups
            .Define(ContainerGroup)
            .WithRegion(group.Region)
            .WithExistingResourceGroup(group)
            .WithLinux()
            .WithPublicImageRegistryOnly()
            .WithoutVolume()
            .DefineContainerInstance(ContainerGroup)
                .WithImage(Image)
                .WithoutPorts()
                .WithCpuCoreCount(1.0)
                .WithMemorySizeInGB(1.0)
                .WithEnvironmentVariables(env)
                .Attach()
            .WithRestartPolicy(ContainerGroupRestartPolicy.Always)
            .CreateAsync(token);

        private static IAzure AuthenticateToAzure(IConfigurationRoot config, ILogger log)
        {
            try
            {
                log.LogInformation($"Authenticating with Azure...");

                var credentials = new AzureCredentialsFactory().FromServicePrincipal(
                    config["ClientId"],
                    config["ClientSecret"],
                    config["TenantId"],
                    AzureEnvironment.AzureGlobalCloud);

                return Azure
                    .Authenticate(credentials)
                    .WithSubscription(config["SubscriptionId"]);
            }
            catch (Exception ex)
            {
                log.LogError($"Failed to authenticate:\n{ex.Message}");
                throw;
            }
        }

        private static IConfigurationRoot BuildConfig(string path) => new ConfigurationBuilder()
            .SetBasePath(path)
            .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();
    }
}
