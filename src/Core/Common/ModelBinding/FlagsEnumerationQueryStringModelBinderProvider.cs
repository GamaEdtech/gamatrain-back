namespace GamaEdtech.Common.ModelBinding
{
    using System;

    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data.Enumeration;

    using Microsoft.AspNetCore.Mvc.ModelBinding;

    public class FlagsEnumerationQueryStringModelBinderProvider : IModelBinderProvider
    {
        /// <inheritdoc />
        public IModelBinder? GetBinder(ModelBinderProviderContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            var fullyQualifiedAssemblyName = context.Metadata.ModelType.FullName;
            if (fullyQualifiedAssemblyName is null)
            {
                return null;
            }

            var flagsEnumType = context.Metadata.ModelType.Assembly.GetType(fullyQualifiedAssemblyName, false);
            if (flagsEnumType is null)
            {
                return null;
            }

            // Type.IsSubclassOf never matches an open generic base, so this provider used to bind nothing and every
            // flags-enum query parameter (e.g. admin/identities?Roles=Student) arrived as null. Globals.IsSubclassOf
            // compares generic type definitions, as EnumerationQueryStringModelBinderProvider already does.
            return !Globals.IsSubclassOf(flagsEnumType, typeof(FlagsEnumeration<>))
                ? null
                : Activator.CreateInstance(typeof(FlagsEnumerationQueryStringModelBinder<>).MakeGenericType(flagsEnumType)) as IModelBinder;
        }
    }
}
