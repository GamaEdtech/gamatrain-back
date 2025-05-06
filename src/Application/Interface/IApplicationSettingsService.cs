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
    }
}
