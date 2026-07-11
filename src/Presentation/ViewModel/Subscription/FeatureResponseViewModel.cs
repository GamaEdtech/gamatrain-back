namespace GamaEdtech.Presentation.ViewModel.Subscription
{
    public sealed class FeatureResponseViewModel
    {
        public int Id { get; set; }

        public string? Code { get; set; }

        public string? Name { get; set; }

        public string? Description { get; set; }

        public bool IsActive { get; set; }
    }
}
