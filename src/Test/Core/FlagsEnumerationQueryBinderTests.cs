namespace GamaEdtech.Test.Core
{
    using GamaEdtech.Common.ModelBinding;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Mvc.ModelBinding;

    using Xunit;

    // A flags smart enum in a query string (admin/identities?Roles=Student) must get the flags binder. The provider used
    // Type.IsSubclassOf against the open generic FlagsEnumeration<>, which never matches, so the filter was ignored.
    public class FlagsEnumerationQueryBinderTests
    {
        private sealed class ProviderContext(ModelMetadata metadata) : ModelBinderProviderContext
        {
            public override BindingInfo BindingInfo { get; } = new();
            public override ModelMetadata Metadata { get; } = metadata;
            public override IModelMetadataProvider MetadataProvider { get; } = new EmptyModelMetadataProvider();
            public override IModelBinder CreateBinder(ModelMetadata metadata) => throw new NotSupportedException();
        }

        private static IModelBinder? BinderFor<T>() =>
            new FlagsEnumerationQueryStringModelBinderProvider().GetBinder(new ProviderContext(new EmptyModelMetadataProvider().GetMetadataForType(typeof(T))));

        [Fact]
        public void RoleGetsTheFlagsBinder() =>
            Assert.IsType<FlagsEnumerationQueryStringModelBinder<Role>>(BinderFor<Role>());

        [Fact]
        public void SystemClaimGetsTheFlagsBinder() =>
            Assert.IsType<FlagsEnumerationQueryStringModelBinder<SystemClaim>>(BinderFor<SystemClaim>());

        [Fact]
        public void OtherTypesGetNoBinder() =>
            Assert.Null(BinderFor<string>());
    }
}
