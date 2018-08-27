namespace UpdateDonkeybot
{
    using System;
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

    public static class DockerHubWebhook
    {
        private const string ResourceGroup = "Donkeybot";

        private const string ContainerGroup = "donkeybot";

        private const string Image = "perrym5/donkeybot";

        [FunctionName("DockerHubWebhook")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequest req,
            ILogger log,
            ExecutionContext context,
            CancellationToken token)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            var config = BuildConfig(context.FunctionAppDirectory);
            var azure = AuthenticateToAzure(config, log);
            await CreateDonkeybot(azure, log, token);

            return new OkObjectResult("Output...");
        }

        private static async Task CreateDonkeybot(IAzure azure, ILogger log, CancellationToken token)
        {
            log.LogInformation($"Creating container group '{ContainerGroup}'...");

            var resourceGroup = await azure.ResourceGroups.GetByNameAsync(ResourceGroup, token);
            var containerGroup = await azure.ContainerGroups
                .Define(ContainerGroup)
                .WithRegion(resourceGroup.Region)
                .WithExistingResourceGroup(resourceGroup)
                .WithLinux()
                .WithPublicImageRegistryOnly()
                .WithoutVolume()
                .DefineContainerInstance(ContainerGroup)
                    .WithImage(Image)
                    .WithoutPorts()
                    .WithCpuCoreCount(1.0)
                    .WithMemorySizeInGB(1.0)
                    //.WithEnvironmentVariables
                    .Attach()
                .WithRestartPolicy(ContainerGroupRestartPolicy.Always)
                .CreateAsync(token);

            log.LogInformation("Created container group successfully...");
        }

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
