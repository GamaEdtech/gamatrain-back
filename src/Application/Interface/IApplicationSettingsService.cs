namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.ApplicationSettings;

    [Injectable]
    public interface IApplicationSettingsService
    {
        Task<ResultData<ApplicationSettingsDto>> GetApplicationSettingsAsync();
        Task<ResultData<T?>> GetSettingAsync<T>(string key);
        Task<ResultData<bool>> ModifyApplicationSettingsAsync(ApplicationSettingsDto settingsDto);

        /// <summary>
        /// Writes one setting (a property name of ApplicationSettingsDto) and leaves the others as they are; null or empty
        /// removes it, so it reads as unset. Clears the settings cache. For a setting with its own admin control, kept out of
        /// the general settings form (whose save rewrites every value it sends).
        /// </summary>
        Task<ResultData<bool>> SetSettingAsync(string key, string? value);
    }
}
