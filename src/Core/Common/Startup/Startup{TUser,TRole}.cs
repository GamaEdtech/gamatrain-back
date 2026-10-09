namespace GamaEdtech.Common.Startup
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Reflection;
    using System.Text.Encodings.Web;
    using System.Text.Unicode;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Core.Extensions.Collections;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.HttpProvider;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Common.Identity.ApiKey;
    using GamaEdtech.Common.Identity.DataProtection;
    using GamaEdtech.Common.Localization;
    using GamaEdtech.Common.Logging;
    using GamaEdtech.Common.Mapping;
    using GamaEdtech.Common.ModelBinding;
    using GamaEdtech.Common.Mvc.Routing;
    using GamaEdtech.Common.Resources;

    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.DataProtection;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.ApplicationModels;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.WebEncoders;

    public abstract class Startup<TUser, TRole>
        where TUser : class
        where TRole : class
    {
        private readonly StartupOption startupOption;

        protected Startup([NotNull] StartupOption startupOption)
        {
#pragma warning disable S3010 // Static fields should not be updated in constructors
            Constants.ErrorCodePrefix = startupOption.ErrorCodePrefix;
#pragma warning restore S3010 // Static fields should not be updated in constructors
            this.startupOption = startupOption;
        }

        public IConfiguration Configuration => startupOption.Configuration;

        public void ConfigureServices([NotNull] IServiceCollection services)
        {
            var frameworkAssembly = Assembly.GetExecutingAssembly();
            var dir = Path.GetDirectoryName(frameworkAssembly.Location);
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }

            var files = Directory.GetFiles(dir, $"{startupOption.DefaultNamespace}.*.dll").Where(t => !t.Contains(frameworkAssembly.ManifestModule.Name, StringComparison.OrdinalIgnoreCase));

            var assemblies = files.Select(Assembly.LoadFrom)
                .Where(t => t.GetCustomAttribute<InjectableAttribute>() is not null)
                .Union(new[] { frameworkAssembly });
            var allTypes = assemblies.SelectMany(t => t.DefinedTypes);

            var mvcBuilder = ConfigureServicesInternal(services, allTypes);

            AddScopedDynamic(services, assemblies, allTypes);
            ConfigureServicesCore(services, mvcBuilder);
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (startupOption.Localization)
            {
                _ = app.UseRequestLocalization(LocalizationExtensions.RequestLocalizationOptions);
            }

            _ = app.UseExceptionHandler(_ => { });

            if (!env.IsDevelopment() && startupOption.Https)
            {
                _ = app.UseHsts();
                _ = app.UseHttpsRedirection();
            }

            _ = app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = context =>
                {
                    // add time expires header
                    const int durationInSeconds = 60 * 60 * 24 * 30; // Expires time. I set to 30 days
                    context.Context.Response.Headers.CacheControl = $"public,max-age={durationInSeconds},immutable";
                    context.Context.Response.Headers.Vary = "Accept";
                    context.Context.Response.Headers.XContentTypeOptions = "nosniff";
                    context.Context.Response.Headers.AccessControlAllowOrigin = "*";
                }
            });
            _ = app.UseRouting();

            if (startupOption.Authentication)
            {
                _ = app.UseAuthentication().UseAuthorization();
            }

            ConfigureCore(app, env);

            _ = app.UseEndpoints(endpoints =>
            {
                _ = endpoints.MapControllerRoute(
                    name: "areaRoute",
                    pattern: $"{(startupOption.Localization ? "{culture=en}/" : string.Empty)}{{area:slugify:exists}}/{{controller:slugify=Home}}/{{action:slugify=Index}}/{{id?}}");

                _ = endpoints.MapControllerRoute(
                    name: "default",
                    pattern: $"{(startupOption.Localization ? "{culture=en}/" : string.Empty)}{{controller:slugify=Home}}/{{action:slugify=Index}}/{{id?}}");
            });
        }

        protected abstract void ConfigureServicesCore(IServiceCollection services, IMvcBuilder mvcBuilder);

        protected abstract void ConfigureCore(IApplicationBuilder app, IWebHostEnvironment env);

        private static void AddStores(IServiceCollection services, Type userType, Type roleType, Type contextType)
        {
            var identityUserType = FindGenericBaseType(userType, typeof(IdentityUser<>)) ?? throw new InvalidOperationException("NotIdentityUser");
            var keyType = identityUserType.GenericTypeArguments[0];

            if (roleType is not null)
            {
                Type userStoreType;
                Type roleStoreType;
                var identityContext = FindGenericBaseType(contextType, typeof(IdentityDbContext<,,,,,,,,>));
                if (identityContext is null)
                {
                    // If its a custom DbContext, we can only add the default POCOs
                    userStoreType = typeof(UserStore<,,,>).MakeGenericType(userType, roleType, contextType, keyType);
                    roleStoreType = typeof(RoleStore<,,>).MakeGenericType(roleType, contextType, keyType);
                }
                else
                {
                    userStoreType = typeof(UserStore<,,,,,,,,,>).MakeGenericType(userType, roleType, contextType,
                        identityContext.GenericTypeArguments[2],
                        identityContext.GenericTypeArguments[3],
                        identityContext.GenericTypeArguments[4],
                        identityContext.GenericTypeArguments[5],
                        identityContext.GenericTypeArguments[7],
                        identityContext.GenericTypeArguments[6],
                        identityContext.GenericTypeArguments[8]);
                    roleStoreType = typeof(RoleStore<,,,,>).MakeGenericType(roleType, contextType,
                        identityContext.GenericTypeArguments[2],
                        identityContext.GenericTypeArguments[4],
                        identityContext.GenericTypeArguments[6]);
                }

                services.TryAddScoped(typeof(IUserStore<>).MakeGenericType(userType), userStoreType);
                services.TryAddScoped(typeof(IRoleStore<>).MakeGenericType(roleType), roleStoreType);
            }
            else
            { // No Roles
                Type userStoreType;
                var identityContext = FindGenericBaseType(contextType, typeof(IdentityUserContext<,,,,,>));
                if (identityContext is null)
                {
                    // If its a custom DbContext, we can only add the default POCOs
                    userStoreType = typeof(UserOnlyStore<,,>).MakeGenericType(userType, contextType, keyType);
                }
                else
                {
                    userStoreType = typeof(UserOnlyStore<,,,,,>).MakeGenericType(userType, contextType,
                        identityContext.GenericTypeArguments[1],
                        identityContext.GenericTypeArguments[2],
                        identityContext.GenericTypeArguments[3],
                        identityContext.GenericTypeArguments[4],
                        identityContext.GenericTypeArguments[5]);
                }

                services.TryAddScoped(typeof(IUserStore<>).MakeGenericType(userType), userStoreType);
            }
        }

        private static TypeInfo? FindGenericBaseType([NotNull] Type currentType, [NotNull] Type genericBaseType)
        {
            var type = currentType;
            while (type is not null)
            {
                var genericType = type.IsGenericType ? type.GetGenericTypeDefinition() : null;
                if (genericType is not null && genericType == genericBaseType)
                {
                    return type.GetTypeInfo();
                }

                type = type.BaseType;
            }

            return null;
        }

        private IMvcBuilder ConfigureServicesInternal(IServiceCollection services, IEnumerable<TypeInfo> allTypes)
        {
            _ = services.AddTransient(typeof(Lazy<>));

            _ = services.AddExceptionHandler<GlobalExceptionHandler>();

            if (startupOption.Localization)
            {
                services.TryAddSingleton<Microsoft.Extensions.Localization.IStringLocalizerFactory, ResourceManagerStringLocalizerFactory>();

                _ = services.AddLocalization();
                services.ConfigureRequestLocalization();
            }

            _ = services.AddRouting(options =>
            {
                options.ConstraintMap["slugify"] = typeof(SlugifyParameterTransformer);
                options.LowercaseUrls = true;
                options.LowercaseQueryStrings = true;

                var customConstraintMaps = typeof(IRouteConstraint).GetAllTypesImplementingType(allTypes);
                if (customConstraintMaps is not null)
                {
                    foreach (var item in customConstraintMaps)
                    {
                        options.ConstraintMap.Add(item.Name, item);
                    }
                }
            });

            var mvcBuilder = services.AddControllers(ConfigureMvc);

            _ = mvcBuilder.AddJsonOptions(options =>
            {
                // options.JsonSerializerOptions.Converters.Add(new DictionaryEnumerationConverter<int>())
                // options.JsonSerializerOptions.Converters.Add(new DictionaryEnumerationConverter<byte>())
                options.JsonSerializerOptions.Converters.Add(new EnumerationConverterFactory());
                options.JsonSerializerOptions.Converters.Add(new FlagsEnumerationConverterFactory());
                options.JsonSerializerOptions.Converters.Add(new BitArrayConverter());
                options.JsonSerializerOptions.Converters.Add(new UlidJsonConverter());

                // options.JsonSerializerOptions.Converters.Add(new DateTimeConverterFactory())
            });

            _ = mvcBuilder.AddDataAnnotationsLocalization(options => options.DataAnnotationLocalizerProvider = (type, factory) => factory.Create(typeof(GlobalResource)));

            _ = mvcBuilder.ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = actionContext => new OkObjectResult(new ApiResponse<object>(actionContext.ModelState)));

            if (startupOption.Authentication)
            {
                var lifetime = Configuration.GetValue<TimeSpan>("IdentityOptions:DataProtection:Lifetime");
                _ = services.AddDataProtection()
                    .SetApplicationName(startupOption.DefaultNamespace)
                    .PersistKeysToDbContext(lifetime)
                    .SetDefaultKeyLifetime(lifetime);
            }

#pragma warning disable S4830 // Server certificates should be verified during SSL/TLS connections
            var httpClientBuilder = services.AddHttpClient(Constants.HttpClientIgnoreSslAndAutoRedirect).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual,
                ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true,
                AllowAutoRedirect = false,
            });
#pragma warning restore S4830 // Server certificates should be verified during SSL/TLS connections
            if (startupOption.HttpClientMessageHandler is not null)
            {
                _ = httpClientBuilder.AddHttpMessageHandler(startupOption.HttpClientMessageHandler);
            }

#pragma warning disable CA5398 // Avoid hardcoded SslProtocols values
#pragma warning disable S4830 // Server certificates should be verified during SSL/TLS connections
            var httpClientBuilder13 = services.AddHttpClient(Constants.HttpClientIgnoreSslAndAutoRedirectTls13).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual,
                ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true,
                AllowAutoRedirect = false,
                SslProtocols = System.Security.Authentication.SslProtocols.Tls13,
            });
#pragma warning restore S4830 // Server certificates should be verified during SSL/TLS connections
#pragma warning restore CA5398 // Avoid hardcoded SslProtocols values
            if (startupOption.HttpClientMessageHandler is not null)
            {
                _ = httpClientBuilder13.AddHttpMessageHandler(startupOption.HttpClientMessageHandler);
            }

            _ = services.AddHttpClient(PublicNetworkHttpClient.Name, t => t.Timeout = TimeSpan.FromSeconds(30)).ConfigurePrimaryHttpMessageHandler(PublicNetworkHttpClient.CreateHandler);

            _ = services.AddHttpContextAccessor();
            _ = services.Configure<WebEncoderOptions>(options => options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
            _ = services.ConfigureOptions<ConfigureJsonOptions>();

            if (startupOption.Authentication)
            {
                _ = services.AddAuthorization(options => options.AddPolicy(PermissionConstants.PermissionPolicy, policy => policy.RequireAssertion(context =>
                    {
                        if (context.Resource is not HttpContext httpContext)
                        {
                            return false;
                        }

                        var endPoint = httpContext.GetEndpoint();
                        if (endPoint is null)
                        {
                            return false;
                        }

                        var permission = endPoint.Metadata.OfType<PermissionAttribute>().LastOrDefault();
                        if (permission is null)
                        {
                            return false;
                        }

                        if (permission.Roles?.Exists(t => context.User.IsInRole(t)) == true)
                        {
                            return true;
                        }

                        var claims = context.User.Claims.Where(t => t.Type == PermissionConstants.PermissionPolicy);
                        return claims.Any(t => t.Value.Equals(endPoint?.DisplayName, StringComparison.OrdinalIgnoreCase));
                    })));

                _ = services.AddAuthorization(options => options.AddPolicy(PermissionConstants.ApiKeyPolicy, policy => policy.RequireAssertion(context =>
                {
                    if (context.Resource is not HttpContext httpContext)
                    {
                        return false;
                    }

                    var endPoint = httpContext.GetEndpoint();
                    if (endPoint is null)
                    {
                        return false;
                    }

                    var permission = endPoint.Metadata.OfType<ApiKeyAttribute>().LastOrDefault();
                    return permission is not null && context.User.Claims.Any(t => t.Type == PermissionConstants.ApiKeyPolicy);
                })));

                _ = services.AddAuthentication()
                    .AddScheme<TokenAuthenticationSchemeOptions, TokenAuthenticationHandler>(PermissionConstants.TokenAuthenticationScheme, t => { })
                    .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(PermissionConstants.ApiKeyAuthenticationScheme, t => { });
                _ = services.Configure<ApiDataProtectorTokenProviderOptions>(Configuration.GetSection("IdentityOptions:Tokens:ApiDataProtectorTokenProviderOptions"));
                _ = services.Configure<IdentityOptions>(options => options.Tokens.ProviderMap[PermissionConstants.ApiDataProtectorTokenProvider] = new TokenProviderDescriptor(typeof(IApiDataProtectorTokenProvider<TUser>)));
            }

            return mvcBuilder;

            static void ConfigureMvc(MvcOptions options)
            {
                options.ModelMetadataDetailsProviders.Add(new DisplayMetadataProvider());

                options.ModelBindingMessageProvider.SetMissingBindRequiredValueAccessor((v) =>
                {
                    var msg = GlobalResource.Validation_MissingBindRequiredValueAccessor;
                    return string.Format(msg, v);
                });
                options.ModelBindingMessageProvider.SetMissingKeyOrValueAccessor(() => GlobalResource.Validation_MissingKeyOrValueAccessor);
                options.ModelBindingMessageProvider.SetMissingRequestBodyRequiredValueAccessor(() => GlobalResource.Validation_MissingRequestBodyRequiredValueAccessor);
                options.ModelBindingMessageProvider.SetValueMustNotBeNullAccessor((v) =>
                {
                    var msg = GlobalResource.Validation_ValueMustNotBeNullAccessor;
                    return string.Format(msg, v);
                });
                options.ModelBindingMessageProvider.SetAttemptedValueIsInvalidAccessor((v1, v2) =>
                {
                    var msg = GlobalResource.Validation_AttemptedValueIsInvalidAccessor;
                    return string.Format(msg, v1, v2);
                });
                options.ModelBindingMessageProvider.SetNonPropertyAttemptedValueIsInvalidAccessor((v) =>
                {
                    var msg = GlobalResource.Validation_NonPropertyAttemptedValueIsInvalidAccessor;
                    return string.Format(msg, v);
                });
                options.ModelBindingMessageProvider.SetUnknownValueIsInvalidAccessor((v) =>
                {
                    var msg = GlobalResource.Validation_UnknownValueIsInvalidAccessor;
                    return string.Format(msg, v);
                });
                options.ModelBindingMessageProvider.SetNonPropertyUnknownValueIsInvalidAccessor(() => GlobalResource.Validation_NonPropertyUnknownValueIsInvalidAccessor);
                options.ModelBindingMessageProvider.SetValueIsInvalidAccessor((v) =>
                {
                    var msg = GlobalResource.Validation_ValueIsInvalidAccessor;
                    return string.Format(msg, v);
                });
                options.ModelBindingMessageProvider.SetValueMustBeANumberAccessor((v) =>
                {
                    var msg = GlobalResource.Validation_ValueMustBeANumberAccessor;
                    return string.Format(msg, v);
                });
                options.ModelBindingMessageProvider.SetNonPropertyValueMustBeANumberAccessor(() => GlobalResource.Validation_NonPropertyValueMustBeANumberAccessor);

                options.Conventions.Add(new RouteTokenTransformerConvention(new SlugifyParameterTransformer()));

                options.ModelBinderProviders.Insert(0, new EnumerationQueryStringModelBinderProvider());
                options.ModelBinderProviders.Insert(0, new FlagsEnumerationQueryStringModelBinderProvider());
                options.ModelBinderProviders.Insert(0, new DateTimeOffsetQueryStringModelBinderProvider());
                options.ModelBinderProviders.Insert(0, new UlidQueryStringModelBinderProvider());
            }
        }

        private void AddScopedDynamic(IServiceCollection services, [NotNull] IEnumerable<Assembly> assemblies, IEnumerable<TypeInfo> allTypes)
        {
            var injectableTypes = allTypes.Where(t => t.GetCustomAttribute<InjectableAttribute>() is not null);
            foreach (var serviceType in injectableTypes)
            {
                var implementationTypes = serviceType.GetAllTypesImplementingType(allTypes);
                if (implementationTypes is null || !implementationTypes.Any())
                {
                    continue;
                }

                foreach (var implementationType in implementationTypes)
                {
                    var serviceLifetimeAttribute = implementationType.GetCustomAttribute<ServiceLifetimeAttribute>();
                    serviceLifetimeAttribute ??= new ServiceLifetimeAttribute(ServiceLifetime.Transient);

                    if (serviceLifetimeAttribute.Parameters?.Any() == true)
                    {
                        services.Add(new ServiceDescriptor(serviceType, provider =>
                        {
                            var parameters = serviceLifetimeAttribute.Parameters.Select(t => provider.GetService(t!)).ToArray();
                            return Activator.CreateInstance(implementationType, parameters)!;
                        }, serviceLifetimeAttribute.ServiceLifetime));

                        services.Add(new ServiceDescriptor(implementationType, provider =>
                        {
                            var parameters = serviceLifetimeAttribute.Parameters.Select(t => provider.GetService(t!)).ToArray();
                            return Activator.CreateInstance(implementationType, parameters)!;
                        }, serviceLifetimeAttribute.ServiceLifetime));
                    }
                    else
                    {
                        services.Add(new ServiceDescriptor(serviceType, implementationType, serviceLifetimeAttribute.ServiceLifetime));
                    }

                    if (serviceType == typeof(IEntityContext))
                    {
                        var arguments = implementationType?.BaseType?.GetGenericArguments();
                        if (arguments is not null && startupOption.Identity)
                        {
                            AddStores(services, arguments[1], arguments[2], arguments[0]);
                        }
                    }
                }

                if (startupOption.Identity && serviceType == typeof(IEntityContext))
                {
                    _ = services
                        .AddIdentity<TUser, TRole>(options =>
                        {
                            Configuration.Bind("IdentityOptions", options);
                            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
                        })
                        .AddDefaultTokenProviders()
                        .AddErrorDescriber<LocalizedIdentityErrorDescriber>();

                    _ = services.Configure<SecurityStampValidatorOptions>(options => Configuration.Bind("IdentityOptions:SecurityStampValidator", options));
                }
            }

            var config = new TypeAdapterConfig();
            _ = config.Scan([.. assemblies]);
            _ = services.AddSingleton(config);
        }
    }
}
