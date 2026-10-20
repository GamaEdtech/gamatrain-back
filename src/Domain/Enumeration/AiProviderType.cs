namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>
    /// The text-generation (LLM) provider behind <c>IAiService</c>, picked by the <c>Ai:Type</c> setting.
    /// </summary>
    public sealed class AiProviderType : Enumeration<AiProviderType, byte>
    {
        [Display]
        public static readonly AiProviderType OpenAi = new(nameof(OpenAi), 0);

        public AiProviderType()
        {
        }

        public AiProviderType(string name, byte value) : base(name, value)
        {
        }
    }
}
