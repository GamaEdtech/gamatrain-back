namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.File;

    /// <summary>
    /// Images inserted in the rich editors (CKEditor: posts and the blog, tickets, tutorials...). They are stored in the
    /// public bucket's <c>EditorImage</c> container and linked from the HTML by their CDN address, never embedded as base64.
    /// See docs/architecture/cross-cutting-concerns.md, "Rich editor images".
    /// </summary>
    [Injectable]
    public interface IEditorImageService
    {
        /// <summary>Checks the image (JPG, PNG, GIF or WebP by its content, at most <see cref="MaxBytes"/>), removes its
        /// metadata (EXIF, a phone's location) by re-encoding it (an animated GIF is kept as it is), stores it and answers its
        /// public address.</summary>
        Task<ResultData<Uri>> UploadAsync([NotNull] EditorImageUploadDto requestDto);

        /// <summary>The largest image accepted: 5 MB.</summary>
        const long MaxBytes = 5L * 1024 * 1024;
    }
}
