namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;
    using GamaEdtech.Presentation.ViewModel.Contact;

    using Microsoft.AspNetCore.Mvc;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    public class ContactsController(Lazy<ILogger<ContactsController>> logger, Lazy<IContactService> contactService
        , Lazy<IEnumerable<ICaptchaProvider>> captchaProviders, Lazy<IConfiguration> configuration)
        : ApiControllerBase<ContactsController>(logger)
    {
        [HttpPost, Produces<ApiResponse<ManageContactResponseViewModel>>()]
        public async Task<IActionResult<ManageContactResponseViewModel>> CreateContact([NotNull, FromBody] CreateContactRequestViewModel request)
        {
            try
            {
                _ = configuration.Value.GetValue<string?>("Captcha:Type").TryGetFromNameOrValue<CaptchaProviderType, byte>(out var captchaProviderType);
                var validateCaptcha = await captchaProviders.Value.FirstOrDefault(t => t.ProviderType == captchaProviderType)!.VerifyCaptchaAsync(request.Captcha);
                if (!validateCaptcha.Data)
                {
                    return Ok<ManageContactResponseViewModel>(new(new Error { Message = "Invalid Captcha" }));
                }

                var result = await contactService.Value.CreateContactAsync(new()
                {
                    Body = request.Body,
                    Email = request.Email,
                    FullName = request.FullName,
                    Subject = request.Subject,
                });
                return Ok<ManageContactResponseViewModel>(new(result.Errors)
                {
                    Data = new() { Id = result.Data },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManageContactResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
