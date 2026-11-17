namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Presentation.ViewModel.File;

    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// Images inserted in the rich editors (CKEditor), for any signed-in user: stored in public storage and answered as a link,
    /// so the HTML never carries a base64 image. See docs/architecture/cross-cutting-concerns.md, "Rich editor images".
    /// </summary>
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    [ApiVersion("1.0")]
    [Permission(policy: null)]
    public class EditorImagesController(Lazy<ILogger<EditorImagesController>> logger, Lazy<IEditorImageService> editorImageService)
        : ApiControllerBase<EditorImagesController>(logger)
    {
        // 5 MB of image plus room for the form around it.
        private const long MaxRequestBytes = IEditorImageService.MaxBytes + (512 * 1024);

        /// <summary>Uploads one image (JPG, PNG, GIF or WebP, at most 5 MB); its metadata is removed. Answers its public
        /// address (<c>url</c>), to put in the editor's <c>img src</c>.</summary>
        [HttpPost, Produces<ApiResponse<EditorImageResponseViewModel>>()]
        [RequestSizeLimit(MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
        public async Task<IActionResult<EditorImageResponseViewModel>> Upload([NotNull] IFormFile file)
        {
            try
            {
                using MemoryStream content = new();
                await file.CopyToAsync(content);
                var result = await editorImageService.Value.UploadAsync(new() { UserId = User.UserId(), FileName = file.FileName, Content = content.ToArray() });
                return Ok<EditorImageResponseViewModel>(new(result.Errors) { Data = result.Data is null ? null : new() { Url = result.Data } });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<EditorImageResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
