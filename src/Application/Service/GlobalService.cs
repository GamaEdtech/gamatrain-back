namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Text;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Core.Extensions.Linq;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.SiteMap;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    using Error = Common.Data.Error;

    public class GlobalService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor, Lazy<IStringLocalizer<GlobalService>> localizer, Lazy<ILogger<GlobalService>> logger
            , Lazy<IGenericFactory<ICaptchaProvider, CaptchaProviderType>> genericFactory, Lazy<IConfiguration> configuration, Lazy<IEnumerable<ISiteMapHandler>> siteMapHandlers, Lazy<IWebHostEnvironment> environment)
        : LocalizableServiceBase<GlobalService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IGlobalService
    {
        public static readonly double DefaultPriority = 1;
        public static readonly ChangeFrequency DefaultChangeFrequency = ChangeFrequency.Monthly;
        public static readonly int MaxItem = 50000;

        public async Task<ResultData<bool>> VerifyCaptchaAsync(string? captcha)
        {
            try
            {
                _ = configuration.Value.GetValue<string?>("Captcha:Type").TryGetFromNameOrValue<CaptchaProviderType, byte>(out var captchaProviderType);

                return await genericFactory.Value.GetProvider(captchaProviderType!)!.VerifyCaptchaAsync(captcha);
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }

        public async Task<ResultData<ListDataSource<SiteMapDto>>> GetSiteMapsAsync(ListRequestDto<SiteMap>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<SiteMap>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                var lst = await result.List.Select(t => new SiteMapDto
                {
                    Id = t.Id,
                    Priority = t.Priority,
                    ChangeFrequency = t.ChangeFrequency,
                    IdentifierId = t.IdentifierId!.Value,
                    ItemType = t.ItemType,
                }).ToListAsync();
                return new(OperationResult.Succeeded) { Data = new() { List = lst, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<long>> ManageSiteMapAsync([NotNull] ManageSiteMapRequestDto requestDto)
        {
            try
            {
                if (requestDto.ChangeFrequency is null && !requestDto.Priority.HasValue)
                {
                    return new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["ChangeFrequencyAndPriorityCanNotBeNull"] },],
                    };
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<SiteMap>();
                SiteMap? siteMap = null;

                if (requestDto.Id.HasValue)
                {
                    siteMap = await repository.GetAsync(requestDto.Id.Value);
                    if (siteMap is null)
                    {
                        return new(OperationResult.NotFound)
                        {
                            Errors = [new() { Message = Localizer.Value["SiteMapNotFound"] },],
                        };
                    }

                    siteMap.Priority = requestDto.Priority;
                    siteMap.ChangeFrequency = requestDto.ChangeFrequency;

                    _ = repository.Update(siteMap);
                }
                else
                {
                    siteMap = await repository.GetAsync(t => t.IdentifierId == requestDto.IdentifierId && t.ItemType == requestDto.ItemType);
                    if (siteMap is null)
                    {
                        siteMap = new SiteMap
                        {
                            ChangeFrequency = requestDto.ChangeFrequency,
                            IdentifierId = requestDto.IdentifierId,
                            ItemType = requestDto.ItemType,
                            Priority = requestDto.Priority,
                        };
                        repository.Add(siteMap);
                    }
                    else
                    {
                        siteMap.Priority = requestDto.Priority;
                        siteMap.ChangeFrequency = requestDto.ChangeFrequency;

                        _ = repository.Update(siteMap);
                    }

                }

                _ = await uow.SaveChangesAsync();

                return new(OperationResult.Succeeded) { Data = siteMap.Id };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<bool>> RemoveSiteMapAsync([NotNull] ISpecification<SiteMap> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<SiteMap>();
                var siteMap = await repository.GetAsync(specification);
                if (siteMap is null)
                {
                    return new(OperationResult.NotFound)
                    {
                        Data = false,
                        Errors = [new() { Message = Localizer.Value["SiteMapNotFound"] },],
                    };
                }

                repository.Remove(siteMap);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> GenerateSiteMapAsync()
        {
            try
            {
                var dir = Path.Combine(environment.Value.WebRootPath, "sitemap");
                if (!Directory.Exists(dir))
                {
                    _ = Directory.CreateDirectory(dir);
                }
                var oldFiles = Directory.GetFiles(dir);
                foreach (var file in oldFiles)
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<SiteMap>();

                StringBuilder sb = new();
                _ = sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                _ = sb.Append("<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
                foreach (var handler in siteMapHandlers.Value)
                {
                    var lst = from t1 in handler.GetSiteMapData(uow)
                              join t2 in repository.GetManyQueryable(t => t.ItemType == handler.ItemType) on t1.Id equals t2.IdentifierId into g
                              from t3 in g.DefaultIfEmpty()
                              orderby t1.Id
                              select new
                              {
                                  t1.Id,
                                  t1.LastModifyDate,
                                  t1.Path1,
                                  t1.Path2,
                                  t3.Priority,
                                  t3.ChangeFrequency,
                              };
                    var i = 0;
                    while (true)
                    {
                        var result = await lst.Skip(MaxItem * i).Take(MaxItem).ToListAsync();
                        if (result is null || result.Count == 0)
                        {
                            break;
                        }

                        var fileName = $"sitemap-{handler.ItemType.Identifier}{i}";
                        _ = sb.AppendFormat(@"
<sitemap>
    <loc>https://gamatrain.com/sitemap/{0}</loc>
</sitemap>", fileName);

                        StringBuilder nested = new();
                        _ = nested.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
                        for (var j = 0; j < result.Count; j++)
                        {
                            // Slugify before the empty check: a name with no Latin characters slugifies to "",
                            // and "/school/12/" isn't the URL the page declares as canonical.
                            var slug = result[j].Path2.Slugify();
                            _ = nested.AppendFormat(@"
<url>
    <loc>https://gamatrain.com/{0}/{1}</loc>
    <lastmod>{2}</lastmod>
    <changefreq>{3}</changefreq>
    <priority>{4}</priority>
</url>
", handler.ItemType.Identifier, $"{result[j].Path1}{(string.IsNullOrEmpty(slug) ? "" : $"/{slug}")}".TrimStart('/'), result[j].LastModifyDate?.ToString("O"), (result[j].ChangeFrequency ?? DefaultChangeFrequency).Name.ToLowerInvariant(), result[j].Priority ?? DefaultPriority);
                        }
                        _ = nested.Append("</urlset>");
                        await File.WriteAllTextAsync(Path.Combine(dir, $"{fileName}.xml"), nested.ToString());

                        i++;
                        if (result.Count < MaxItem)
                        {
                            break;
                        }
                    }
                }

                _ = sb.Append("</sitemapindex>");
                await File.WriteAllTextAsync(Path.Combine(dir, "sitemap.xml"), sb.ToString());

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = new[] { new Error { Message = exc.Message }, } };
            }
        }
    }
}
