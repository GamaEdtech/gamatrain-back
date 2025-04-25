namespace GamaEdtech.Presentation.Api.Configuration
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Application.Service;

    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Options;

    public static class AppConfiguration
    {
        public static void AddFileUploader([NotNull] this IServiceCollection services)
        {
            _ = services.AddScoped<IFileUploader>(provider =>
            {
                var config = provider.GetRequiredService<IOptions<AppSetting>>().Value;
                if (config?.FileUpload?.Uploaders == null ||
                    !config.FileUpload.Uploaders.TryGetValue("Azure", out var fileUploadConfig))
                {
                    throw new ArgumentException("Azure file upload configuration is missing.");
                }

                var azureConnection = config.ConnectionStrings?.Azure;
                if (string.IsNullOrWhiteSpace(azureConnection))
                {
                    throw new ArgumentException("Azure connection string is not configured.");
                }

                var containerNames = fileUploadConfig.ContainerNames ?? [];
                return new AzureUploadFile(azureConnection, containerNames);
            });

            _ = services.AddScoped<IFileManager>(provider =>
            {
                var config = provider.GetRequiredService<IOptions<AppSetting>>().Value;

                if (config?.FileUpload?.Uploaders == null || config.FileUpload.Uploaders.Count == 0)
                {
                    throw new ArgumentException("File uploaders configuration is missing or empty.");
                }

                var fileUploaders = provider.GetRequiredService<IEnumerable<IFileUploader>>();
                var uploaderKeys = config.FileUpload.Uploaders.Keys.ToArray();

                return new FileManager(fileUploaders, uploaderKeys);
            });
        }
    }
}
