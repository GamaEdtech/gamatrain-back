using FluentValidation;

namespace GamaEdtech.Back.Gateway.Rest.MultiMedias;

public class AddMultiMediaDtoValidator : AbstractValidator<AddMultiMediaDto>
{
	private static readonly string[] ValidExtensions = [".docx", ".doc", ".pptx", ".ppt", ".pdf"];

    public AddMultiMediaDtoValidator()
	{
		RuleFor(x => x.File)
			.NotNull().WithMessage("name is required")
			.Must(t=> ValidExtensions.Contains(Path.GetExtension(t.FileName).ToLowerInvariant())).WithMessage("just doc, docx, ppt, pptx and pdf files are supported");
    }
}
